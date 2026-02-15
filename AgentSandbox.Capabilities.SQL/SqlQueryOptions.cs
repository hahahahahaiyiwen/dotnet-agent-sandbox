namespace AgentSandbox.Capabilities.SQL;

public sealed class SqlQueryOptions
{
    public int Offset { get; init; }
    public int? Limit { get; init; }
    public string Principal { get; init; } = "unknown";
    public string Resource { get; init; } = "database";
}


