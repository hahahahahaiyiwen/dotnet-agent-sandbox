using AgentSandbox.Core;
using AgentSandbox.Core.Shell;

namespace AgentSandbox.Tests;

public class SandboxManagerTests
{
    [Fact]
    public void Get_ReturnsSandboxWithId()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Get();

        Assert.NotNull(sandbox);
        Assert.NotEmpty(sandbox.Id);
    }

    [Fact]
    public void Get_CreatesNewSandboxEachCall()
    {
        var manager = new SandboxManager();
        var first = manager.Get();
        var second = manager.Get();

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void DisposeSandbox_RemovesTrackedInstance()
    {
        var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions { MaxActiveSandboxes = 1 });
        var sandbox = manager.Get();

        sandbox.Dispose();
        var next = manager.Get();

        Assert.NotEqual(sandbox.Id, next.Id);
    }

    [Fact]
    public void Get_WhenMaxActiveSandboxesReached_Throws()
    {
        var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions { MaxActiveSandboxes = 1 });
        manager.Get();

        var ex = Assert.Throws<InvalidOperationException>(() => manager.Get());

        Assert.Contains("Maximum active sandboxes limit", ex.Message);
    }

    [Fact]
    public void CleanupScheduler_RemovesInactiveSandboxes()
    {
        using var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions
            {
                InactivityTimeout = TimeSpan.FromMilliseconds(250),
                CleanupInterval = TimeSpan.FromMilliseconds(150),
                MaxActiveSandboxes = 1
            });

        var sandbox = manager.Get();
        var cleaned = SpinWait.SpinUntil(() =>
        {
            try
            {
                _ = manager.Get();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }, TimeSpan.FromSeconds(3));

        Assert.True(cleaned);
        sandbox.Dispose();
    }

    [Fact]
    public void Execute_RunsCommandAndReturnsResult()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Get();

        var result = sandbox.Execute("echo Hello");

        Assert.True(result.Success);
        Assert.Equal("Hello", result.Stdout);
    }

    [Fact]
    public void WriteFile_ViaShell_EnforcesMaxFileSize()
    {
        var options = new SandboxOptions { MaxFileSize = 10 };
        var manager = new SandboxManager(options);
        var sandbox = manager.Get();

        var result = sandbox.Execute($"echo '{new string('x', 100)}' > /large.txt");

        Assert.False(result.Success);
        Assert.Contains("exceeds", result.Stderr.ToLower());
    }

    [Fact]
    public void Snapshot_And_Restore_PreservesState()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Get();

        sandbox.Execute("echo 'original' > /file.txt");
        var snapshot = sandbox.CreateSnapshot();
        sandbox.Execute("echo 'modified' > /file.txt");
        sandbox.RestoreSnapshot(snapshot);

        var restoredResult = sandbox.Execute("cat /file.txt");
        Assert.Equal("original", restoredResult.Stdout.Trim());
    }

    [Fact]
    public void SaveSnapshot_And_RestoreSnapshot_CreatesNewSandboxId()
    {
        var store = new InMemorySnapshotStore();
        var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions { SnapshotStore = store });
        var sandbox = manager.Get();
        sandbox.Execute("echo 'original' > /state.txt");

        var snapshotId = manager.SaveSnapshot(sandbox.Id);
        var restored = manager.RestoreSnapshot(snapshotId);
        var restoredResult = restored.Execute("cat /state.txt");

        Assert.NotEqual(sandbox.Id, restored.Id);
        Assert.Equal("original", restoredResult.Stdout.Trim());
    }

    [Fact]
    public void SaveSnapshot_WithoutStoreConfigured_Throws()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Get();

        var ex = Assert.Throws<InvalidOperationException>(() => manager.SaveSnapshot(sandbox.Id));
        Assert.Contains("Snapshot store is not configured", ex.Message);
    }

    [Fact]
    public void SaveSnapshot_MultipleFromSameSandbox_ReturnsDistinctSnapshotIds()
    {
        var store = new InMemorySnapshotStore();
        var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions { SnapshotStore = store });
        var sandbox = manager.Get();

        sandbox.Execute("echo 'v1' > /state.txt");
        var snapshotIdV1 = manager.SaveSnapshot(sandbox.Id);
        sandbox.Execute("echo 'v2' > /state.txt");
        var snapshotIdV2 = manager.SaveSnapshot(sandbox.Id);

        var restoredV1 = manager.RestoreSnapshot(snapshotIdV1);
        var restoredV2 = manager.RestoreSnapshot(snapshotIdV2);

        Assert.NotEqual(snapshotIdV1, snapshotIdV2);
        Assert.Equal("v1", restoredV1.Execute("cat /state.txt").Stdout.Trim());
        Assert.Equal("v2", restoredV2.Execute("cat /state.txt").Stdout.Trim());
    }

    [Fact]
    public void RestoreSnapshot_WithoutStoreConfigured_Throws()
    {
        var manager = new SandboxManager();

        var ex = Assert.Throws<InvalidOperationException>(() => manager.RestoreSnapshot("snapshot-id"));
        Assert.Contains("Snapshot store is not configured", ex.Message);
    }

    [Fact]
    public void Release_PersistsSnapshot_And_DisposesSandbox()
    {
        var store = new InMemorySnapshotStore();
        var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions
            {
                SnapshotStore = store,
                MaxActiveSandboxes = 2
            });
        var sandbox = manager.Get();
        sandbox.Execute("echo 'release-state' > /state.txt");

        var snapshotId = manager.Release(sandbox.Id);

        Assert.Throws<ObjectDisposedException>(() => sandbox.Execute("pwd"));
        var restored = manager.RestoreSnapshot(snapshotId);
        var restoredResult = restored.Execute("cat /state.txt");

        Assert.NotEmpty(snapshotId);
        Assert.Equal("release-state", restoredResult.Stdout.Trim());
    }

    [Fact]
    public void Release_WithoutStoreConfigured_Throws()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Get();

        var ex = Assert.Throws<InvalidOperationException>(() => manager.Release(sandbox.Id));
        Assert.Contains("Snapshot store is not configured", ex.Message);
    }

    [Fact]
    public void Execute_UsesDefaultCommandTimeout_FromDefaultOptions()
    {
        var options = new SandboxOptions
        {
            CommandTimeout = TimeSpan.FromMilliseconds(20),
            ShellExtensions = [new SlowCommand()]
        };
        var manager = new SandboxManager(options);
        var sandbox = manager.Get();

        var result = sandbox.Execute("slow 200");

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SlowCommand : IShellCommand
    {
        public string Name => "slow";
        public string Description => "Sleeps for the given number of milliseconds";

        public ShellResult Execute(string[] args, IShellContext context)
        {
            var delayMs = int.Parse(args[0]);
            Thread.Sleep(delayMs);
            return ShellResult.Ok("done");
        }
    }
}
