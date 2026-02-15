using AgentSandbox.Core.Telemetry;

namespace AgentSandbox.Core.Capabilities;

/// <summary>
/// Event raised when a capability operation is emitted.
/// </summary>
public record CapabilityOperationEvent : SandboxEvent
{
    public required string CapabilityName { get; init; }
    public required string OperationType { get; init; }
    public required string OperationName { get; init; }
    public TimeSpan? Duration { get; init; }
    public bool? Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}
