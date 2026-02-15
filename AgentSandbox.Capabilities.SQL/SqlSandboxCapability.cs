using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AgentSandbox.Core.Capabilities;
using AgentSandbox.Core.Telemetry;
using Microsoft.Data.Sqlite;

namespace AgentSandbox.Capabilities.SQL;

public sealed class SqlSandboxCapability : ISandboxCapability, ISqlCapability, IDisposable
{
    private static readonly Regex FirstTokenRegex = new(@"^\s*(?<token>[A-Za-z_]+)", RegexOptions.Compiled);
    private static readonly HashSet<string> ReadOnlyPragmasWithArguments =
    [
        "table_info",
        "table_xinfo",
        "index_info",
        "index_xinfo",
        "index_list",
        "foreign_key_list"
    ];

    private readonly SqlCapabilityOptions _options;
    private readonly SemaphoreSlim _concurrencyGate;
    private ISandboxEventEmitter? _eventEmitter;
    private string _sandboxId = "unknown";

    public string Name => "database-sqlite";

    public SqlSandboxCapability(SqlCapabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ConnectionFactory is null && string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new ArgumentException("Either ConnectionString or ConnectionFactory is required.", nameof(options));
        }
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive.");
        }
        if (options.MaxRowsPerPage <= 0 || options.MaxOffset <= 0 || options.MaxResponseBytes <= 0 || options.MaxConcurrentQueries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Guardrail limits must be positive.");
        }

        _options = options;
        _concurrencyGate = new SemaphoreSlim(options.MaxConcurrentQueries, options.MaxConcurrentQueries);
    }

    public void Initialize(ISandboxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _sandboxId = context.SandboxId;
        _eventEmitter = context.EventEmitter;
    }

    public SqlQueryResult ExecuteSql(string statement, SqlQueryOptions? options = null)
    {
        options ??= new SqlQueryOptions();
        try
        {
            EnsureReadOnlyStatement(statement);
        }
        catch (SqlCapabilityException ex)
        {
            EmitFailure(options, statement, ex.ErrorCode, ex.Message, TimeSpan.Zero);
            throw;
        }

        if (!_concurrencyGate.Wait(0))
        {
            throw CreateFailure(
                SqlCapabilityErrorCodes.ResourceLimit,
                "Max concurrent queries exceeded.",
                options,
                statement,
                TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var limit = options.Limit ?? _options.MaxRowsPerPage;
            if (limit <= 0 || limit > _options.MaxRowsPerPage)
            {
                throw CreateFailure(
                    SqlCapabilityErrorCodes.ResourceLimit,
                    $"Requested limit must be between 1 and {_options.MaxRowsPerPage}.",
                    options,
                    statement,
                    stopwatch.Elapsed);
            }

            if (options.Offset < 0 || options.Offset > _options.MaxOffset)
            {
                throw CreateFailure(
                    SqlCapabilityErrorCodes.ResourceLimit,
                    $"Offset must be between 0 and {_options.MaxOffset}.",
                    options,
                    statement,
                    stopwatch.Elapsed);
            }

            using var connection = CreateConnection();
            connection.Open();
            using (var readOnlyCommand = connection.CreateCommand())
            {
                readOnlyCommand.CommandText = "PRAGMA query_only=ON;";
                readOnlyCommand.ExecuteNonQuery();
            }

            // Apply LIMIT/OFFSET in SQL to reduce database work
            var paginatedStatement = statement;
            if (options.Offset > 0 || limit < int.MaxValue)
            {
                paginatedStatement = $"SELECT * FROM ({statement}) LIMIT {limit + 1} OFFSET {options.Offset}";
            }

            using var command = connection.CreateCommand();
            command.CommandText = paginatedStatement;
            command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(_options.Timeout.TotalSeconds));

            using var reader = command.ExecuteReader();
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            var responseBytes = 0;
            var hasMore = false;

            while (reader.Read())
            {
                // Check if we've hit the limit (we requested limit+1 to detect hasMore)
                if (rows.Count >= limit)
                {
                    hasMore = true;
                    break;
                }

                var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                responseBytes += EstimateRowBytes(row);
                if (responseBytes > _options.MaxResponseBytes)
                {
                    throw CreateFailure(
                        SqlCapabilityErrorCodes.ResourceLimit,
                        $"Response exceeded max bytes ({responseBytes} > {_options.MaxResponseBytes}).",
                        options,
                        statement,
                        stopwatch.Elapsed);
                }

                rows.Add(row);
            }

            stopwatch.Stop();
            EmitSuccess(options, statement, rows.Count, responseBytes, stopwatch.Elapsed);
            return new SqlQueryResult(columns, rows, hasMore, new SqlQueryUsage(rows.Count, responseBytes, stopwatch.Elapsed));
        }
        catch (SqliteException ex) when (IsTimeout(ex))
        {
            throw CreateFailure(SqlCapabilityErrorCodes.Timeout, "SQL execution timed out.", options, statement, stopwatch.Elapsed, ex);
        }
        catch (SqliteException ex) when (IsSyntaxError(ex))
        {
            throw CreateFailure(SqlCapabilityErrorCodes.SyntaxError, ex.Message, options, statement, stopwatch.Elapsed, ex);
        }
        catch (SqliteException ex)
        {
            throw CreateFailure(SqlCapabilityErrorCodes.BackendUnavailable, ex.Message, options, statement, stopwatch.Elapsed, ex);
        }
        catch (Exception ex) when (ex is not SqlCapabilityException)
        {
            throw CreateFailure(SqlCapabilityErrorCodes.BackendUnavailable, ex.Message, options, statement, stopwatch.Elapsed, ex);
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    public void Dispose()
    {
        _concurrencyGate.Dispose();
    }

    private static bool IsSyntaxError(SqliteException ex)
        => ex.SqliteErrorCode == 1 && ex.Message.Contains("syntax", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimeout(SqliteException ex)
        => ex.SqliteErrorCode == 9 ||
           ex.Message.Contains("interrupted", StringComparison.OrdinalIgnoreCase);

    private void EnsureReadOnlyStatement(string statement)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            throw new SqlCapabilityException(SqlCapabilityErrorCodes.AuthDenied, "SQL statement is required.");
        }

        var normalized = statement.Trim();
        while (normalized.EndsWith(';'))
        {
            normalized = normalized[..^1].TrimEnd();
        }
        if (ContainsUnquotedSemicolon(normalized))
        {
            throw new SqlCapabilityException(SqlCapabilityErrorCodes.AuthDenied, "Multiple SQL statements are not allowed.");
        }

        var token = FirstTokenRegex.Match(normalized).Groups["token"].Value.ToUpperInvariant();
        if (token is not ("SELECT" or "PRAGMA" or "EXPLAIN" or "WITH"))
        {
            throw new SqlCapabilityException(SqlCapabilityErrorCodes.AuthDenied, $"Statement '{token}' is not allowed in read-only mode.");
        }

        if (token == "PRAGMA")
        {
            EnsurePragmaIsReadOnly(normalized);
        }

    }

    private SqlCapabilityException CreateFailure(
        string errorCode,
        string message,
        SqlQueryOptions options,
        string statement,
        TimeSpan duration,
        Exception? innerException = null)
    {
        EmitFailure(options, statement, errorCode, message, duration);
        return new SqlCapabilityException(errorCode, message, innerException);
    }

    private void EmitSuccess(SqlQueryOptions options, string statement, int returnedRows, int responseBytes, TimeSpan duration)
    {
            _eventEmitter?.Emit(SandboxCapabilityEventHelper.CreateSuccessEvent(
            sandboxId: _sandboxId,
            capabilityName: Name,
            operationType: "capability.database.query",
            operationName: "ExecuteSql",
            duration: duration,
            metadata: BuildMetadata(options, statement, "success", returnedRows, responseBytes)));
    }

    private void EmitFailure(SqlQueryOptions options, string statement, string errorCode, string message, TimeSpan duration)
    {
        _eventEmitter?.Emit(SandboxCapabilityEventHelper.CreateFailureEvent(
            sandboxId: _sandboxId,
            capabilityName: Name,
            operationType: "capability.database.query",
            operationName: "ExecuteSql",
            errorMessage: message,
            errorCode: errorCode,
            duration: duration,
            metadata: BuildMetadata(options, statement, "failure", 0, 0)));
    }

    private static IReadOnlyDictionary<string, object?> BuildMetadata(SqlQueryOptions options, string statement, string outcome, int rows, int responseBytes)
    {
        return new Dictionary<string, object?>
        {
            ["principal"] = options.Principal,
            ["resource"] = options.Resource,
            ["statement.hash"] = ComputeHash(statement),
            ["outcome"] = outcome,
            ["usage.rows"] = rows,
            ["usage.responseBytes"] = responseBytes
        };
    }

    private static string ComputeHash(string statement)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(statement));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int EstimateRowBytes(IReadOnlyDictionary<string, object?> row)
    {
        var bytes = 0;
        foreach (var item in row)
        {
            bytes += Encoding.UTF8.GetByteCount(item.Key);
            bytes += item.Value switch
            {
                null => Encoding.UTF8.GetByteCount("null"),
                byte[] data => data.Length,
                string text => Encoding.UTF8.GetByteCount(text),
                _ => Encoding.UTF8.GetByteCount(item.Value.ToString() ?? string.Empty)
            };
        }

        return bytes;
    }

    private static bool ContainsUnquotedSemicolon(string sql)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n')
                {
                    inLineComment = false;
                }
                continue;
            }

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == '-' && next == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (!inDoubleQuote && c == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && c == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && c == ';')
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsurePragmaIsReadOnly(string statement)
    {
        var pragmaText = statement["PRAGMA".Length..].Trim();
        if (string.IsNullOrWhiteSpace(pragmaText))
        {
            throw new SqlCapabilityException(SqlCapabilityErrorCodes.AuthDenied, "PRAGMA statement is incomplete.");
        }

        if (pragmaText.Contains('='))
        {
            throw new SqlCapabilityException(SqlCapabilityErrorCodes.AuthDenied, "Mutable PRAGMA statements are not allowed.");
        }

        var nameEnd = pragmaText.IndexOfAny([' ', '\t', '\r', '\n', '(']);
        var pragmaName = (nameEnd >= 0 ? pragmaText[..nameEnd] : pragmaText).Trim();
        var trailing = nameEnd >= 0 ? pragmaText[nameEnd..].TrimStart() : string.Empty;
        var baseName = pragmaName.Contains('.') ? pragmaName[(pragmaName.LastIndexOf('.') + 1)..] : pragmaName;
        if (string.IsNullOrWhiteSpace(baseName))
        {
            throw new SqlCapabilityException(SqlCapabilityErrorCodes.AuthDenied, "PRAGMA statement is incomplete.");
        }

        if (string.IsNullOrEmpty(trailing))
        {
            return;
        }

        if (!trailing.StartsWith('('))
        {
            throw new SqlCapabilityException(SqlCapabilityErrorCodes.AuthDenied, "Mutable PRAGMA statements are not allowed.");
        }

        if (!ReadOnlyPragmasWithArguments.Contains(baseName.ToLowerInvariant()))
        {
            throw new SqlCapabilityException(SqlCapabilityErrorCodes.AuthDenied, "Mutable PRAGMA statements are not allowed.");
        }
    }

    private SqliteConnection CreateConnection()
    {
        if (_options.ConnectionFactory is not null)
        {
            var connection = _options.ConnectionFactory();
            if (connection is null)
            {
                throw new InvalidOperationException("ConnectionFactory returned null.");
            }

            return connection;
        }

        return new SqliteConnection(_options.ConnectionString!);
    }
}


