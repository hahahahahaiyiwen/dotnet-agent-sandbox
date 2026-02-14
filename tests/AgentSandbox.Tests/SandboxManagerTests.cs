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
    public void Get_TracksCreatedSandboxInManager()
    {
        var manager = new SandboxManager();

        var sandbox = manager.Get();

        Assert.Contains(manager.GetAllStats(), s => s.Id == sandbox.Id);
    }

    [Fact]
    public void DisposeSandbox_RemovesFromList()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Get();

        sandbox.Dispose();

        Assert.DoesNotContain(manager.GetAllStats(), s => s.Id == sandbox.Id);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void DisposeSandbox_RemovesSandboxFromStats()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Get();

        sandbox.Dispose();

        Assert.DoesNotContain(manager.GetAllStats(), s => s.Id == sandbox.Id);
    }

    [Fact]
    public void GetAllStats_ReturnsAllActiveSandboxes()
    {
        var manager = new SandboxManager();
        var sandbox1 = manager.Get();
        var sandbox2 = manager.Get();
        var sandbox3 = manager.Get();

        var sandboxes = manager.GetAllStats().ToList();

        Assert.Equal(3, sandboxes.Count);
        Assert.Contains(sandboxes, s => s.Id == sandbox1.Id);
        Assert.Contains(sandboxes, s => s.Id == sandbox2.Id);
        Assert.Contains(sandboxes, s => s.Id == sandbox3.Id);
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

        Assert.False(result.Success, $"Expected failure but got success. Stdout: {result.Stdout}, Stderr: {result.Stderr}");
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
        var modifiedResult = sandbox.Execute("cat /file.txt");
        Assert.Equal("modified", modifiedResult.Stdout.Trim());

        sandbox.RestoreSnapshot(snapshot);
        var restoredResult = sandbox.Execute("cat /file.txt");
        Assert.Equal("original", restoredResult.Stdout.Trim());
    }

    [Fact]
    public void GetStats_ReturnsCorrectStats()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Get();

        sandbox.Execute("mkdir /dir");
        sandbox.Execute("echo 'content' > /file.txt");
        sandbox.Execute("ls");

        var stats = sandbox.GetStats();

        Assert.Equal(sandbox.Id, stats.Id);
        Assert.Equal(3, stats.FileCount);
        Assert.True(stats.CommandCount >= 3);
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
                InactivityTimeout = TimeSpan.FromMilliseconds(30),
                CleanupInterval = TimeSpan.FromMilliseconds(20)
            });
        var sandbox = manager.Get();

        var cleaned = SpinWait.SpinUntil(
            () => !manager.GetAllStats().Any(s => s.Id == sandbox.Id),
            TimeSpan.FromSeconds(2));

        Assert.True(cleaned);
    }

    [Fact]
    public void Execute_UsesDefaultCommandTimeout_FromManagerOptions()
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
