namespace AgentSandbox.Core;

/// <summary>
/// Minimal telemetry abstraction exposed to capabilities.
/// </summary>
public interface ISandboxTelemetry
{
    bool Enabled { get; }
}
