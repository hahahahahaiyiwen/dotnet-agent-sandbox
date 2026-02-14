namespace AgentSandbox.Core;

/// <summary>
/// Configuration options for sandbox manager resource controls.
/// </summary>
public class SandboxManagerOptions
{
    /// <summary>
    /// Maximum number of active sandboxes allowed at a time. Null means unlimited.
    /// </summary>
    public int? MaxActiveSandboxes { get; set; }

    /// <summary>
    /// Inactivity window before a sandbox is eligible for cleanup.
    /// </summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Optional interval for automatic cleanup of inactive sandboxes. Null disables scheduling.
    /// </summary>
    public TimeSpan? CleanupInterval { get; set; }
}
