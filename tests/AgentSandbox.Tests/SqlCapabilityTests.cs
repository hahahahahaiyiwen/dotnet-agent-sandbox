using AgentSandbox.Core;
using AgentSandbox.Core.Capabilities;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Capabilities.SQL;
using Microsoft.Data.Sqlite;

namespace AgentSandbox.Tests;

public class SqlCapabilityTests
{
    [Fact]
    public void ExecuteSql_AllowsSelect_AndReturnsRows()
    {
        var dbPath = CreateDatabaseWithSampleRows();
        try
        {
            using (var sandbox = CreateSandbox(dbPath, out _))
            {
                var capability = sandbox.GetCapability<ISqlCapability>();
                var result = capability.ExecuteSql("SELECT id, name FROM users ORDER BY id");

                Assert.Equal(["id", "name"], result.Columns);
                Assert.Equal(2, result.Rows.Count);
                Assert.Equal("Ada", result.Rows[0]["name"]);
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_BlocksDml_WithAuthDenied()
    {
        var dbPath = CreateDatabaseWithSampleRows();
        try
        {
            using (var sandbox = CreateSandbox(dbPath, out _))
            {
                var capability = sandbox.GetCapability<ISqlCapability>();

                var ex = Assert.Throws<SqlCapabilityException>(() =>
                    capability.ExecuteSql("INSERT INTO users(name) VALUES ('Mallory')"));

                Assert.Equal(SqlCapabilityErrorCodes.AuthDenied, ex.ErrorCode);
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_MapsSyntaxFailures_ToSyntaxError()
    {
        var dbPath = CreateDatabaseWithSampleRows();
        try
        {
            using (var sandbox = CreateSandbox(dbPath, out _))
            {
                var capability = sandbox.GetCapability<ISqlCapability>();

                var ex = Assert.Throws<SqlCapabilityException>(() => capability.ExecuteSql("SELECT FROM users"));

                Assert.Equal(SqlCapabilityErrorCodes.SyntaxError, ex.ErrorCode);
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_BlocksMultipleStatements_WithAuthDenied()
    {
        var dbPath = CreateDatabaseWithSampleRows();
        try
        {
            using (var sandbox = CreateSandbox(dbPath, out _))
            {
                var capability = sandbox.GetCapability<ISqlCapability>();
                var ex = Assert.Throws<SqlCapabilityException>(() => capability.ExecuteSql("SELECT 1; SELECT 2"));
                Assert.Equal(SqlCapabilityErrorCodes.AuthDenied, ex.ErrorCode);
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_AllowsQuotedKeywords_InReadOnlyQuery()
    {
        var dbPath = CreateDatabaseWithSampleRows();
        try
        {
            using (var sandbox = CreateSandbox(dbPath, out _))
            {
                var capability = sandbox.GetCapability<ISqlCapability>();
                var result = capability.ExecuteSql("SELECT 'DELETE' AS text_value");
                Assert.Single(result.Rows);
                Assert.Equal("DELETE", result.Rows[0]["text_value"]);
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_EnforcesResponseByteLimit()
    {
        var dbPath = CreateDatabaseWithSampleRows();
        try
        {
            var capability = new SqlSandboxCapability(new SqlCapabilityOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                MaxResponseBytes = 8,
                MaxRowsPerPage = 10,
                MaxConcurrentQueries = 1
            });

            var options = new SandboxOptions
            {
                Capabilities = [capability]
            };

            using (var sandbox = new Sandbox(options: options))
            {
                var db = sandbox.GetCapability<ISqlCapability>();
                var ex = Assert.Throws<SqlCapabilityException>(() => db.ExecuteSql("SELECT name FROM users ORDER BY id"));
                Assert.Equal(SqlCapabilityErrorCodes.ResourceLimit, ex.ErrorCode);
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_EmitsAuditEventMetadata_ForSuccessAndFailure()
    {
        var dbPath = CreateDatabaseWithSampleRows();
        try
        {
            var events = new List<CapabilityOperationEvent>();
            using var sandbox = CreateSandbox(
                dbPath,
                out _,
                new DelegateSandboxObserver(onEvent: e =>
                {
                    if (e is CapabilityOperationEvent capabilityEvent)
                    {
                        events.Add(capabilityEvent);
                    }
                }));

            var capability = sandbox.GetCapability<ISqlCapability>();
            _ = capability.ExecuteSql("SELECT id FROM users", new SqlQueryOptions { Principal = "agent-1", Resource = "users" });
            Assert.Throws<SqlCapabilityException>(() => capability.ExecuteSql("DELETE FROM users"));

            var success = Assert.Single(events.Where(e => e.Success == true));
            Assert.Equal(sandbox.Id, success.SandboxId);
            Assert.Equal("agent-1", success.Metadata!["principal"]);
            Assert.Equal("users", success.Metadata["resource"]);
            Assert.Equal("success", success.Metadata["outcome"]);
            Assert.NotNull(success.Metadata["statement.hash"]);

            var failure = Assert.Single(events.Where(e => e.Success == false));
            Assert.Equal(SqlCapabilityErrorCodes.AuthDenied, failure.ErrorCode);
            Assert.Equal("failure", failure.Metadata!["outcome"]);
            Assert.Equal(sandbox.Id, failure.SandboxId);
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_MapsUnexpectedException_ToBackendUnavailable()
    {
        var capability = new SqlSandboxCapability(new SqlCapabilityOptions
        {
            ConnectionFactory = () => null!,
            MaxRowsPerPage = 10,
            MaxResponseBytes = 4096,
            MaxConcurrentQueries = 1
        });

        using var sandbox = new Sandbox(options: new SandboxOptions { Capabilities = [capability] });
        var sql = sandbox.GetCapability<ISqlCapability>();
        var ex = Assert.Throws<SqlCapabilityException>(() => sql.ExecuteSql("SELECT 1"));
        Assert.Equal(SqlCapabilityErrorCodes.BackendUnavailable, ex.ErrorCode);
    }

    [Fact]
    public void ExecuteSql_RejectsOffsetBeyondConfiguredLimit()
    {
        var dbPath = CreateDatabaseWithManyRows(20);
        try
        {
            var capability = new SqlSandboxCapability(new SqlCapabilityOptions
            {
                ConnectionString = $"Data Source={dbPath};Pooling=False",
                MaxRowsPerPage = 10,
                MaxOffset = 10,
                MaxResponseBytes = 4096,
                MaxConcurrentQueries = 1
            });
            using var sandbox = new Sandbox(options: new SandboxOptions { Capabilities = [capability] });
            var sql = sandbox.GetCapability<ISqlCapability>();
            
            // Offset at exactly MaxOffset should succeed
            var result = sql.ExecuteSql("SELECT id FROM users ORDER BY id", new SqlQueryOptions { Offset = 10 });
            Assert.NotNull(result);
            Assert.Equal(10, result.Rows.Count);
            
            // Offset beyond MaxOffset should fail
            var ex = Assert.Throws<SqlCapabilityException>(() => 
                sql.ExecuteSql("SELECT id FROM users", new SqlQueryOptions { Offset = 11 }));
            Assert.Equal(SqlCapabilityErrorCodes.ResourceLimit, ex.ErrorCode);
            Assert.Contains("Offset must be between 0 and 10", ex.Message);
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_AppliesLimitAndOffsetInSql()
    {
        var dbPath = CreateDatabaseWithManyRows(100);
        try
        {
            using (var sandbox = CreateSandbox(dbPath, out _))
            {
                var capability = sandbox.GetCapability<ISqlCapability>();
                
                // Request page 2 (offset 10, limit 5)
                var result = capability.ExecuteSql("SELECT id FROM users ORDER BY id", new SqlQueryOptions { Limit = 5, Offset = 10 });
                
                // Should return rows 11-15 (ids are 1-based)
                Assert.Equal(5, result.Rows.Count);
                Assert.Equal(11L, result.Rows[0]["id"]);
                Assert.Equal(15L, result.Rows[4]["id"]);
                Assert.True(result.HasMore); // More rows available
            }
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void ExecuteSql_SupportsSharedInMemory_WhenUsingConnectionFactory()
    {
        var sharedName = $"memdb-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={sharedName};Mode=Memory;Cache=Shared;Pooling=False";
        using var keeper = new SqliteConnection(connectionString);
        keeper.Open();

        using (var seed = keeper.CreateCommand())
        {
            seed.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL); INSERT INTO users(name) VALUES ('Ada');";
            seed.ExecuteNonQuery();
        }

        var capability = new SqlSandboxCapability(new SqlCapabilityOptions
        {
            ConnectionFactory = () => new SqliteConnection(connectionString),
            MaxRowsPerPage = 10,
            MaxResponseBytes = 4096,
            MaxConcurrentQueries = 1
        });

        using var sandbox = new Sandbox(options: new SandboxOptions { Capabilities = [capability] });
        var db = sandbox.GetCapability<ISqlCapability>();
        var result = db.ExecuteSql("SELECT name FROM users");

        Assert.Single(result.Rows);
        Assert.Equal("Ada", result.Rows[0]["name"]);
    }

    private static Sandbox CreateSandbox(string dbPath, out SqlSandboxCapability capability, DelegateSandboxObserver? observer = null)
    {
        capability = new SqlSandboxCapability(new SqlCapabilityOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False",
            MaxRowsPerPage = 10,
            MaxResponseBytes = 4096,
            MaxConcurrentQueries = 1
        });

        var options = new SandboxOptions
        {
            Telemetry = new AgentSandbox.Core.Telemetry.SandboxTelemetryOptions { Enabled = true },
            Capabilities = [capability]
        };

        var sandbox = new Sandbox(options: options);
        if (observer is not null)
        {
            sandbox.Subscribe(observer);
        }

        return sandbox;
    }

    private static string CreateDatabaseWithSampleRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-sandbox-db-{Guid.NewGuid():N}.db");
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);";
        create.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO users(name) VALUES ('Ada'), ('Linus');";
        insert.ExecuteNonQuery();

        return path;
    }

    private static string CreateDatabaseWithManyRows(int rowCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-sandbox-db-{Guid.NewGuid():N}.db");
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);";
        create.ExecuteNonQuery();

        // Insert rows efficiently using a parameterized command
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO users(name) VALUES (@name);";
        var nameParam = insert.CreateParameter();
        nameParam.ParameterName = "@name";
        insert.Parameters.Add(nameParam);
        
        for (var i = 1; i <= rowCount; i++)
        {
            nameParam.Value = $"User{i}";
            insert.ExecuteNonQuery();
        }
        transaction.Commit();

        return path;
    }
}


