namespace AgentSandbox.Capabilities.SQL;

public sealed record SqlQueryUsage(int ReturnedRows, int ResponseBytes, TimeSpan Duration);

public sealed record SqlQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool HasMore,
    SqlQueryUsage Usage);


