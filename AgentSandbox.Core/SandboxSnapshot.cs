namespace AgentSandbox.Core;

/// <summary>
/// Snapshot of sandbox state for persistence/restoration.
/// </summary>
public class SandboxSnapshot
{
    public string Id { get; set; } = string.Empty;
    public byte[] FileSystemData { get; set; } = Array.Empty<byte>();
    public string CurrentDirectory { get; set; } = "/";
    public Dictionary<string, string> Environment { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public SnapshotMetadata? Metadata { get; set; }
}

/// <summary>
/// Metadata attached to a sandbox snapshot.
/// </summary>
public sealed class SnapshotMetadata
{
    /// <summary>
    /// Schema version for backward/forward compatibility handling.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Size of serialized snapshot file system payload in bytes.
    /// </summary>
    public long SnapshotSizeBytes { get; set; }

    /// <summary>
    /// File-system node count (files + directories) at snapshot creation.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// UTC timestamp when the snapshot was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Sandbox that produced this snapshot.
    /// </summary>
    public string SourceSandboxId { get; set; } = string.Empty;

    /// <summary>
    /// Logical source session identifier. In core, this maps to sandbox ownership.
    /// </summary>
    public string SourceSessionId { get; set; } = string.Empty;

    public static SnapshotMetadata CreateDefault(DateTime? createdAt = null) => new()
    {
        CreatedAt = createdAt ?? DateTime.UtcNow
    };

    public static SnapshotMetadata FromSnapshot(SandboxSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new SnapshotMetadata
        {
            SchemaVersion = 1,
            SnapshotSizeBytes = snapshot.FileSystemData.LongLength,
            CreatedAt = snapshot.CreatedAt,
            SourceSandboxId = snapshot.Id,
            SourceSessionId = snapshot.Id
        };
    }

    public SnapshotMetadata Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        SnapshotSizeBytes = SnapshotSizeBytes,
        FileCount = FileCount,
        CreatedAt = CreatedAt,
        SourceSandboxId = SourceSandboxId,
        SourceSessionId = SourceSessionId
    };
}
