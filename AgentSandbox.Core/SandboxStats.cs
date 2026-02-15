namespace AgentSandbox.Core;

/// <summary>
/// Runtime statistics for a sandbox.
/// </summary>
public class SandboxStats
{
    public string Id { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
    public int CommandCount { get; set; }
    public int CapabilityOperationCount { get; set; }
    public string CurrentDirectory { get; set; } = "/";
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
}
