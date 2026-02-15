namespace AgentSandbox.Core.Capabilities;

/// <summary>
/// Represents a reusable sandbox capability that can be initialized at runtime.
/// </summary>
public interface ISandboxCapability
{
    /// <summary>
    /// Capability name for diagnostics and reporting.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initializes the capability with a stable sandbox context abstraction.
    /// </summary>
    /// <param name="context">Sandbox runtime context.</param>
    void Initialize(ISandboxContext context);
}
