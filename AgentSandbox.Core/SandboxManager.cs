using System.Collections.Concurrent;

namespace AgentSandbox.Core;

/// <summary>
/// Manages multiple sandbox instances. Thread-safe singleton for server-side usage.
/// </summary>
public class SandboxManager : IDisposable
{
    private readonly ConcurrentDictionary<string, Sandbox> _sandboxes = new();
    private readonly SandboxOptions _defaultOptions;
    private readonly TimeSpan _inactivityTimeout;
    private readonly int? _maxActiveSandboxes;
    private readonly ISnapshotStore? _snapshotStore;
    private readonly object _sync = new();
    private Timer? _cleanupTimer;
    private bool _disposed;

    public SandboxManager(SandboxOptions? defaultOptions = null, TimeSpan? inactivityTimeout = null)
        : this(defaultOptions, new SandboxManagerOptions
        {
            InactivityTimeout = inactivityTimeout ?? TimeSpan.FromHours(1)
        })
    {
    }

    public SandboxManager(SandboxOptions? defaultOptions, SandboxManagerOptions? managerOptions)
    {
        _defaultOptions = defaultOptions ?? new SandboxOptions();
        var options = managerOptions ?? new SandboxManagerOptions();
        _inactivityTimeout = options.InactivityTimeout;
        _maxActiveSandboxes = options.MaxActiveSandboxes;
        _snapshotStore = options.SnapshotStore;

        if (_maxActiveSandboxes.HasValue && _maxActiveSandboxes.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(managerOptions), "MaxActiveSandboxes must be greater than zero.");
        }

        if (options.CleanupInterval.HasValue)
        {
            StartCleanupScheduler(options.CleanupInterval.Value);
        }
    }

    /// <summary>
    /// Creates and returns a new sandbox instance.
    /// </summary>
    public Sandbox Get(SandboxOptions? options = null)
    {
        ThrowIfDisposed();
        var sandbox = new Sandbox(null, options ?? _defaultOptions, OnSandboxDisposed);

        lock (_sync)
        {
            if (_sandboxes.ContainsKey(sandbox.Id))
            {
                sandbox.Dispose();
                throw new InvalidOperationException($"Sandbox with ID '{sandbox.Id}' already exists");
            }

            EnsureCapacity();
            _sandboxes[sandbox.Id] = sandbox;
        }

        return sandbox;
    }

    /// <summary>
    /// Saves a snapshot for an active sandbox through the configured snapshot store.
    /// </summary>
    public string SaveSnapshot(string sandboxId)
    {
        ThrowIfDisposed();
        EnsureSnapshotStoreConfigured();

        lock (_sync)
        {
            if (!_sandboxes.TryGetValue(sandboxId, out var sandbox))
            {
                throw new KeyNotFoundException($"Sandbox '{sandboxId}' not found.");
            }

            return _snapshotStore!.Save(sandbox.CreateSnapshot());
        }
    }

    /// <summary>
    /// Restores a snapshot into a new sandbox instance (with a new sandbox ID).
    /// </summary>
    public Sandbox RestoreSnapshot(string snapshotId, SandboxOptions? options = null)
    {
        ThrowIfDisposed();
        EnsureSnapshotStoreConfigured();

        if (!_snapshotStore!.TryGet(snapshotId, out var snapshot) || snapshot is null)
        {
            throw new KeyNotFoundException($"Snapshot '{snapshotId}' not found.");
        }

        var sandbox = Get(options);
        sandbox.RestoreSnapshot(snapshot);
        return sandbox;
    }

    /// <summary>
    /// Releases an active sandbox by persisting a snapshot and disposing the sandbox.
    /// </summary>
    public string Release(string sandboxId)
    {
        ThrowIfDisposed();
        EnsureSnapshotStoreConfigured();

        Sandbox sandbox;
        string snapshotId;
        lock (_sync)
        {
            if (!_sandboxes.TryGetValue(sandboxId, out sandbox!))
            {
                throw new KeyNotFoundException($"Sandbox '{sandboxId}' not found.");
            }

            snapshotId = _snapshotStore!.Save(sandbox.CreateSnapshot());
            _sandboxes.TryRemove(sandboxId, out _);
        }

        sandbox.Dispose();
        return snapshotId;
    }

    /// <summary>
    /// Called when a sandbox is disposed directly.
    /// </summary>
    private void OnSandboxDisposed(string id)
    {
        _sandboxes.TryRemove(id, out _);
    }

    private int CleanupInactive()
    {
        ThrowIfDisposed();
        var cutoff = DateTime.UtcNow - _inactivityTimeout;
        var toRemove = _sandboxes
            .Where(kvp => kvp.Value.LastActivityAt < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in toRemove)
        {
            RemoveAndDispose(id);
        }

        return toRemove.Count;
    }

    private void StartCleanupScheduler(TimeSpan interval)
    {
        ThrowIfDisposed();
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Cleanup interval must be greater than zero.");
        }

        lock (_sync)
        {
            _cleanupTimer?.Dispose();
            _cleanupTimer = new Timer(CleanupTimerCallback, null, interval, interval);
        }
    }

    private void CleanupTimerCallback(object? _)
    {
        try
        {
            CleanupInactive();
        }
        catch (ObjectDisposedException)
        {
            StopCleanupScheduler();
        }
    }

    private void StopCleanupScheduler()
    {
        lock (_sync)
        {
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
        }
    }

    private void EnsureCapacity()
    {
        if (_maxActiveSandboxes.HasValue && _sandboxes.Count >= _maxActiveSandboxes.Value)
        {
            throw new InvalidOperationException($"Maximum active sandboxes limit ({_maxActiveSandboxes.Value}) reached");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SandboxManager));
        }
    }

    private void EnsureSnapshotStoreConfigured()
    {
        if (_snapshotStore is null)
        {
            throw new InvalidOperationException("Snapshot store is not configured on SandboxManager.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopCleanupScheduler();

        foreach (var id in _sandboxes.Keys.ToList())
        {
            RemoveAndDispose(id);
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void RemoveAndDispose(string id)
    {
        if (_sandboxes.TryRemove(id, out var sandbox))
        {
            sandbox.Dispose();
        }
    }
}
