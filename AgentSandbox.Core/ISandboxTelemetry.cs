namespace AgentSandbox.Core;

/// <summary>
/// Minimal telemetry abstraction exposed to capabilities.
/// </summary>
public interface ISandboxTelemetry
{
    bool Enabled { get; }

    /// <summary>
    /// Starts a telemetry operation scope that can be completed or failed.
    /// </summary>
    ISandboxTelemetryOperation StartOperation(
        string operationType,
        string operationName,
        string? capabilityName = null,
        IReadOnlyDictionary<string, object?>? tags = null);
}

/// <summary>
/// Represents an in-flight telemetry operation.
/// </summary>
public interface ISandboxTelemetryOperation : IDisposable
{
    /// <summary>
    /// Completes the operation as success.
    /// </summary>
    void Complete(IReadOnlyDictionary<string, object?>? tags = null);

    /// <summary>
    /// Completes the operation as failure.
    /// </summary>
    void Fail(
        string errorMessage,
        string? errorCode = null,
        IReadOnlyDictionary<string, object?>? tags = null);
}
