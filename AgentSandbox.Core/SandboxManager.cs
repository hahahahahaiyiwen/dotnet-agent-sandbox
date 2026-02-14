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
    /// Called when a sandbox is disposed directly.
    /// </summary>
    private void OnSandboxDisposed(string id)
    {
        _sandboxes.TryRemove(id, out _);
    }

    /// <summary>
    /// Gets statistics for all sandboxes.
    /// </summary>
    public IEnumerable<SandboxStats> GetAllStats() => 
        _sandboxes.Values.Select(s => s.GetStats());

    /// <summary>
    /// Cleans up inactive sandboxes.
    /// </summary>
    public int CleanupInactive()
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

    /// <summary>
    /// Gets total count of active sandboxes.
    /// </summary>
    public int Count => _sandboxes.Count;

    /// <summary>
    /// Starts periodic cleanup for inactive sandboxes.
    /// </summary>
    public void StartCleanupScheduler(TimeSpan interval)
    {
        ThrowIfDisposed();
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Cleanup interval must be greater than zero.");
        }

        lock (_sync)
        {
            _cleanupTimer?.Dispose();
            _cleanupTimer = new Timer(_ => CleanupInactive(), null, interval, interval);
        }
    }

    /// <summary>
    /// Stops periodic cleanup.
    /// </summary>
    public void StopCleanupScheduler()
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
