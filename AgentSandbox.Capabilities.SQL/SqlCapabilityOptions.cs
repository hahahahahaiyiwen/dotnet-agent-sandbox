using Microsoft.Data.Sqlite;

namespace AgentSandbox.Capabilities.SQL;

public sealed class SqlCapabilityOptions
{
    public string? ConnectionString { get; init; }
    public Func<SqliteConnection>? ConnectionFactory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxRowsPerPage { get; init; } = 200;
    public int MaxOffset { get; init; } = 10_000;
    public int MaxResponseBytes { get; init; } = 256 * 1024;
    public int MaxConcurrentQueries { get; init; } = 2;
}


