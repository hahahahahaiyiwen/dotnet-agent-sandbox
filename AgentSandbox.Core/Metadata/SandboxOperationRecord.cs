using AgentSandbox.Core.Shell;

namespace AgentSandbox.Core.Metadata;

internal sealed record SandboxOperationRecord
{
    public required DateTime Timestamp { get; init; }
    public required string Category { get; init; }
    public required string Operation { get; init; }
    public string? Target { get; init; }
    public bool Success { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
    public ShellResult? ShellResult { get; init; }
}
