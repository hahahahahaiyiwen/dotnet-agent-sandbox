# AgentSandbox.Capabilities.SQL

Read-only SQLite capability project for AgentSandbox.

## Usage

```csharp
var capability = new SqlSandboxCapability(new SqlCapabilityOptions
{
    ConnectionString = "Data Source=my.db",
    MaxRowsPerPage = 200,
    MaxResponseBytes = 256 * 1024,
    MaxConcurrentQueries = 2
});

var sandbox = new Sandbox(options: new SandboxOptions
{
    Capabilities = [capability]
});

var db = sandbox.GetCapability<ISqlCapability>();
var result = db.ExecuteSql("SELECT * FROM my_table");
```

You can also provide `ConnectionFactory` to control connection creation (for example shared in-memory SQLite).

## In-memory streaming datasource

```csharp
using var dataSource = new InMemorySqlDataSource();

await dataSource.InsertRowsAsync(
    table: "requests",
    rows: rowsFromLoader, // IAsyncEnumerable<IReadOnlyDictionary<string, object?>>
    options: new InsertRowsOptions { CreateIfNotExists = true });

var capability = new SqlSandboxCapability(new SqlCapabilityOptions
{
    ConnectionFactory = dataSource.CreateConnection
});
```

## Read-only policy

- Allowed statement categories: `SELECT`, read-only `WITH ... SELECT`, read `EXPLAIN`, read `PRAGMA`
- Blocked categories: DML/DDL and transaction control statements
- Multi-statement execution is blocked

## Error codes

- `AUTH_DENIED`: statement is not allowed by read-only policy
- `TIMEOUT`: query exceeds configured timeout
- `RESOURCE_LIMIT`: query breaches row/response/concurrency guardrails
- `SYNTAX_ERROR`: SQL parser error
- `BACKEND_UNAVAILABLE`: connection/engine execution failure


