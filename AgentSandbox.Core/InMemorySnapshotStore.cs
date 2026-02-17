using System.Collections.Concurrent;

namespace AgentSandbox.Core;

/// <summary>
/// In-memory snapshot store implementation for local/dev scenarios.
/// </summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<string, SandboxSnapshot> _snapshots = new();

    public string Save(SandboxSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var clonedSnapshot = Clone(snapshot);

        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var snapshotId = Guid.NewGuid().ToString("N")[..12];
            if (_snapshots.TryAdd(snapshotId, clonedSnapshot))
            {
                return snapshotId;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique snapshot ID after multiple attempts.");
    }

    public bool TryGet(string snapshotId, out SandboxSnapshot? snapshot)
    {
        if (_snapshots.TryGetValue(snapshotId, out var stored))
        {
            snapshot = Clone(stored);
            return true;
        }

        snapshot = null;
        return false;
    }

    private static SandboxSnapshot Clone(SandboxSnapshot snapshot)
    {
        return new SandboxSnapshot
        {
            Id = snapshot.Id,
            FileSystemData = snapshot.FileSystemData.ToArray(),
            CurrentDirectory = snapshot.CurrentDirectory,
            Environment = new Dictionary<string, string>(snapshot.Environment),
            CreatedAt = snapshot.CreatedAt,
            Metadata = (snapshot.Metadata ?? SnapshotMetadata.FromSnapshot(snapshot)).Clone()
        };
    }
}
