namespace AgentSandbox.Core;

/// <summary>
/// Persists sandbox snapshots for cross-session restore workflows.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>
    /// Saves a snapshot and returns its store identifier.
    /// </summary>
    string Save(SandboxSnapshot snapshot);

    /// <summary>
    /// Attempts to read a snapshot by its store identifier.
    /// </summary>
    bool TryGet(string snapshotId, out SandboxSnapshot? snapshot);
}
