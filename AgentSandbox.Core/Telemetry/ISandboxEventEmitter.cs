namespace AgentSandbox.Core.Telemetry;

/// <summary>
/// Emits sandbox events to subscribed observers.
/// </summary>
public interface ISandboxEventEmitter
{
    void Emit(SandboxEvent sandboxEvent);
}
