namespace AgentSandbox.Capabilities.SQL;

public sealed class InsertRowsOptions
{
    public bool CreateIfNotExists { get; init; }
    public IReadOnlyDictionary<string, string>? ColumnTypes { get; init; }
}

