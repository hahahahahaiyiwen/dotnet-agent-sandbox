using AgentSandbox.Core.Telemetry;

namespace AgentSandbox.Core.Capabilities;

/// <summary>
/// Helper for creating standardized capability operation events.
/// </summary>
public static class SandboxCapabilityEventHelper
{
    public static CapabilityOperationEvent CreateSuccessEvent(
        string sandboxId,
        string capabilityName,
        string operationType,
        string operationName,
        TimeSpan? duration = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        return new CapabilityOperationEvent
        {
            SandboxId = sandboxId,
            CapabilityName = capabilityName,
            OperationType = operationType,
            OperationName = operationName,
            Duration = duration,
            Success = true,
            Metadata = metadata
        };
    }

    public static CapabilityOperationEvent CreateFailureEvent(
        string sandboxId,
        string capabilityName,
        string operationType,
        string operationName,
        string errorMessage,
        string? errorCode = null,
        TimeSpan? duration = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        return new CapabilityOperationEvent
        {
            SandboxId = sandboxId,
            CapabilityName = capabilityName,
            OperationType = operationType,
            OperationName = operationName,
            Duration = duration,
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Metadata = metadata
        };
    }
}
