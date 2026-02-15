using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AgentSandbox.Capabilities.SQL;

public sealed class InMemorySqlDataSource : IDisposable
{
    private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex DataSourceNameRegex = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private readonly SqliteConnection _keeperConnection;
    private readonly string _connectionString;
    private bool _disposed;

    public InMemorySqlDataSource(string? name = null)
    {
        var dataSourceName = string.IsNullOrWhiteSpace(name) ? $"memdb_{Guid.NewGuid():N}" : name;
        if (!DataSourceNameRegex.IsMatch(dataSourceName))
        {
            throw new ArgumentException("Data source name contains invalid characters.", nameof(name));
        }

        _connectionString = $"Data Source={dataSourceName};Mode=Memory;Cache=Shared;Pooling=False";
        _keeperConnection = new SqliteConnection(_connectionString);
        _keeperConnection.Open();
    }

    public SqliteConnection CreateConnection()
    {
        ThrowIfDisposed();
        return new SqliteConnection(_connectionString);
    }

    public async Task InsertRowsAsync(
        string table,
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows,
        InsertRowsOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateIdentifier(table, nameof(table));
        ArgumentNullException.ThrowIfNull(rows);
        options ??= new InsertRowsOptions();

        await using var enumerator = rows.GetAsyncEnumerator(cancellationToken);
        if (!await enumerator.MoveNextAsync())
        {
            return;
        }

        var firstRow = enumerator.Current ?? throw new InvalidOperationException("Row cannot be null.");
        if (firstRow.Count == 0)
        {
            throw new InvalidOperationException("Row must include at least one column.");
        }

        var columns = firstRow.Keys.ToArray();
        foreach (var column in columns)
        {
            ValidateIdentifier(column, "rows");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (options.CreateIfNotExists)
        {
            await CreateTableIfNeededAsync(connection, transaction, table, firstRow, options, cancellationToken);
        }

        await InsertRowAsync(connection, transaction, table, columns, firstRow, cancellationToken);
        while (await enumerator.MoveNextAsync())
        {
            var row = enumerator.Current ?? throw new InvalidOperationException("Row cannot be null.");
            ValidateRowShape(row, columns);
            await InsertRowAsync(connection, transaction, table, columns, row, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _keeperConnection.Dispose();
    }

    private static async Task CreateTableIfNeededAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyDictionary<string, object?> firstRow,
        InsertRowsOptions options,
        CancellationToken cancellationToken)
    {
        var columnDefinitions = firstRow.Select(pair =>
        {
            var type = ResolveColumnType(pair.Key, pair.Value, options.ColumnTypes);
            return $"{QuoteIdentifier(pair.Key)} {type}";
        });

        var sql = $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(table)} ({string.Join(", ", columnDefinitions)});";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRowAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken)
    {
        var quotedColumns = columns.Select(QuoteIdentifier).ToArray();
        var parameterNames = columns.Select((_, i) => $"$p{i}").ToArray();
        var sql = $"INSERT INTO {QuoteIdentifier(table)} ({string.Join(", ", quotedColumns)}) VALUES ({string.Join(", ", parameterNames)});";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        for (var i = 0; i < columns.Count; i++)
        {
            row.TryGetValue(columns[i], out var value);
            command.Parameters.AddWithValue(parameterNames[i], value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ResolveColumnType(string column, object? value, IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(column, out var explicitType))
        {
            return explicitType;
        }

        return value switch
        {
            null => "TEXT",
            byte[] => "BLOB",
            bool or byte or sbyte or short or ushort or int or uint or long or ulong => "INTEGER",
            float or double or decimal => "REAL",
            _ => "TEXT"
        };
    }

    private static string QuoteIdentifier(string identifier) => $"\"{identifier}\"";

    private static void ValidateIdentifier(string identifier, string paramName)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !IdentifierRegex.IsMatch(identifier))
        {
            throw new ArgumentException($"Identifier '{identifier}' is invalid.", paramName);
        }
    }

    private static void ValidateRowShape(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> columns)
    {
        if (row.Count != columns.Count || columns.Any(column => !row.ContainsKey(column)))
        {
            throw new InvalidOperationException("All rows must share the same column set.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemorySqlDataSource));
        }
    }
}

