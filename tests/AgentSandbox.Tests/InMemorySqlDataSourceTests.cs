using AgentSandbox.Core;
using AgentSandbox.Core.Capabilities;
using AgentSandbox.Capabilities.SQL;
using Microsoft.Data.Sqlite;

namespace AgentSandbox.Tests;

public class InMemorySqlDataSourceTests
{
    [Fact]
    public void Ctor_Throws_ForInvalidDataSourceName()
    {
        Assert.Throws<ArgumentException>(() => new InMemorySqlDataSource("bad name with spaces"));
    }

    [Fact]
    public async Task InsertRowsAsync_CreatesTable_WhenEnabled_AndAgentCanQuery()
    {
        using var dataSource = new InMemorySqlDataSource();
        await dataSource.InsertRowsAsync(
            table: "requests",
            rows: ToAsyncRows(new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["result_code"] = "200", ["duration_ms"] = 120.5 },
                new Dictionary<string, object?> { ["result_code"] = "500", ["duration_ms"] = 990.0 }
            }),
            options: new InsertRowsOptions { CreateIfNotExists = true });

        var capability = new SqlSandboxCapability(new SqlCapabilityOptions
        {
            ConnectionFactory = dataSource.CreateConnection,
            MaxRowsPerPage = 50,
            MaxResponseBytes = 64 * 1024,
            MaxConcurrentQueries = 1
        });

        using var sandbox = new Sandbox(options: new SandboxOptions { Capabilities = [capability] });
        var db = sandbox.GetCapability<ISqlCapability>();
        var result = db.ExecuteSql("SELECT result_code FROM requests ORDER BY result_code");

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("200", result.Rows[0]["result_code"]?.ToString());
        Assert.Equal("500", result.Rows[1]["result_code"]?.ToString());
    }

    [Fact]
    public async Task InsertRowsAsync_Throws_WhenTableMissing_AndCreateNotEnabled()
    {
        using var dataSource = new InMemorySqlDataSource();

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
            await dataSource.InsertRowsAsync(
                table: "requests",
                rows: ToAsyncRows(new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?> { ["result_code"] = "200" }
                })));
    }

    [Fact]
    public async Task InsertRowsAsync_Throws_ForInvalidTableName()
    {
        using var dataSource = new InMemorySqlDataSource();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await dataSource.InsertRowsAsync(
                table: "bad-table-name",
                rows: ToAsyncRows(new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?> { ["result_code"] = "200" }
                }),
                options: new InsertRowsOptions { CreateIfNotExists = true }));
    }

    [Fact]
    public async Task InsertRowsAsync_Throws_ForEmptyFirstRow()
    {
        using var dataSource = new InMemorySqlDataSource();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dataSource.InsertRowsAsync(
                table: "requests",
                rows: ToAsyncRows(new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>()
                }),
                options: new InsertRowsOptions { CreateIfNotExists = true }));
    }

    [Fact]
    public async Task InsertRowsAsync_RollsBack_WhenRowShapeMismatches()
    {
        using var dataSource = new InMemorySqlDataSource();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dataSource.InsertRowsAsync(
                table: "requests",
                rows: ToAsyncRows(new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?> { ["result_code"] = "200", ["duration_ms"] = 10.0 },
                    new Dictionary<string, object?> { ["result_code"] = "500" }
                }),
                options: new InsertRowsOptions { CreateIfNotExists = true }));

        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM requests";
        await Assert.ThrowsAsync<SqliteException>(async () => _ = await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task InsertRowsAsync_AppliesColumnTypeOverrides()
    {
        using var dataSource = new InMemorySqlDataSource();
        await dataSource.InsertRowsAsync(
            table: "requests",
            rows: ToAsyncRows(new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["result_code"] = "200", ["payload"] = "{\"ok\":true}" }
            }),
            options: new InsertRowsOptions
            {
                CreateIfNotExists = true,
                ColumnTypes = new Dictionary<string, string> { ["payload"] = "BLOB" }
            });

        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(requests)";
        await using var reader = await command.ExecuteReaderAsync();

        var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            types[reader.GetString(1)] = reader.GetString(2);
        }

        Assert.Equal("BLOB", types["payload"]);
    }

    [Fact]
    public async Task DataSource_Throws_WhenUsedAfterDispose()
    {
        var dataSource = new InMemorySqlDataSource();
        dataSource.Dispose();

        Assert.Throws<ObjectDisposedException>(() => dataSource.CreateConnection());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await dataSource.InsertRowsAsync(
                table: "requests",
                rows: ToAsyncRows(new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?> { ["result_code"] = "200" }
                }),
                options: new InsertRowsOptions { CreateIfNotExists = true }));
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ToAsyncRows(IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            await Task.Yield();
        }
    }
}

