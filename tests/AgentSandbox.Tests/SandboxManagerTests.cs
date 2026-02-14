using AgentSandbox.Core;
using AgentSandbox.Core.Shell;

namespace AgentSandbox.Tests;

public class SandboxManagerTests
{
    [Fact]
    public void Create_ReturnsSandboxWithId()
    {
        var manager = new SandboxManager();
        
        var sandbox = manager.Create();
        
        Assert.NotNull(sandbox);
        Assert.NotEmpty(sandbox.Id);
    }

    [Fact]
    public void Create_WithCustomId_UsesProvidedId()
    {
        var manager = new SandboxManager();
        
        var sandbox = manager.Create("my-sandbox");
        
        Assert.Equal("my-sandbox", sandbox.Id);
    }

    [Fact]
    public void Create_DuplicateId_Throws()
    {
        var manager = new SandboxManager();
        manager.Create("test-id");
        
        Assert.Throws<InvalidOperationException>(() => manager.Create("test-id"));
    }

    [Fact]
    public void Get_ReturnsExistingSandbox()
    {
        var manager = new SandboxManager();
        var created = manager.Create("test");
        
        var retrieved = manager.Get("test");
        
        Assert.Same(created, retrieved);
    }

    [Fact]
    public void Get_ReturnsNullForNonexistent()
    {
        var manager = new SandboxManager();
        
        var result = manager.Get("nonexistent");
        
        Assert.Null(result);
    }

    [Fact]
    public void GetOrCreate_CreatesIfNotExists()
    {
        var manager = new SandboxManager();
        
        var sandbox = manager.GetOrCreate("new-sandbox");
        
        Assert.NotNull(sandbox);
        Assert.Equal("new-sandbox", sandbox.Id);
    }

    [Fact]
    public void GetOrCreate_WhenSandboxExists_Throws()
    {
        var manager = new SandboxManager();
        manager.Create("existing");
        
        var ex = Assert.Throws<InvalidOperationException>(() => manager.GetOrCreate("existing"));
        
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void Destroy_RemovesSandbox()
    {
        var manager = new SandboxManager();
        manager.Create("to-delete");
        
        var result = manager.Destroy("to-delete");
        
        Assert.True(result);
        Assert.Null(manager.Get("to-delete"));
    }

    [Fact]
    public void Destroy_ReturnsFalseForNonexistent()
    {
        var manager = new SandboxManager();
        
        var result = manager.Destroy("nonexistent");
        
        Assert.False(result);
    }

    [Fact]
    public void List_ReturnsAllSandboxIds()
    {
        var manager = new SandboxManager();
        manager.Create("sandbox1");
        manager.Create("sandbox2");
        manager.Create("sandbox3");
        
        var ids = manager.List().ToList();
        
        Assert.Equal(3, ids.Count);
        Assert.Contains("sandbox1", ids);
        Assert.Contains("sandbox2", ids);
        Assert.Contains("sandbox3", ids);
    }

    [Fact]
    public void Execute_RunsCommandAndReturnsResult()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Create();
        
        var result = sandbox.Execute("echo Hello");
        
        Assert.True(result.Success);
        Assert.Equal("Hello", result.Stdout);
    }

    [Fact]
    public void WriteFile_ViaShell_EnforcesMaxFileSize()
    {
        var options = new SandboxOptions { MaxFileSize = 10 };
        var manager = new SandboxManager(options);
        var sandbox = manager.Create();
        
        // Writing via shell command - should fail due to size limit
        var result = sandbox.Execute($"echo '{new string('x', 100)}' > /large.txt");
        
        Assert.False(result.Success, $"Expected failure but got success. Stdout: {result.Stdout}, Stderr: {result.Stderr}");
        Assert.Contains("exceeds", result.Stderr.ToLower());
    }

    [Fact]
    public void Snapshot_And_Restore_PreservesState()
    {
        var manager = new SandboxManager();
        var sandbox = manager.Create();
        
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
        var sandbox = manager.Create("stats-test");
        
        sandbox.Execute("mkdir /dir");
        sandbox.Execute("echo 'content' > /file.txt");
        sandbox.Execute("ls");
        
        var stats = sandbox.GetStats();
        
        Assert.Equal("stats-test", stats.Id);
        Assert.Equal(3, stats.FileCount); // root + dir + file
        Assert.True(stats.CommandCount >= 3);
    }

    [Fact]
    public void Create_WhenMaxActiveSandboxesReached_Throws()
    {
        var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions { MaxActiveSandboxes = 1 });
        manager.Create("first");

        var ex = Assert.Throws<InvalidOperationException>(() => manager.Create("second"));

        Assert.Contains("Maximum active sandboxes limit", ex.Message);
    }

    [Fact]
    public void GetOrCreate_WhenAtCapacityForExistingSandbox_Throws()
    {
        var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions { MaxActiveSandboxes = 1 });
        manager.Create("existing");

        var ex = Assert.Throws<InvalidOperationException>(() => manager.GetOrCreate("existing"));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void GetOrCreate_WhenAtCapacityForNewSandbox_Throws()
    {
        var manager = new SandboxManager(
            defaultOptions: null,
            managerOptions: new SandboxManagerOptions { MaxActiveSandboxes = 1 });
        manager.Create("first");

        var ex = Assert.Throws<InvalidOperationException>(() => manager.GetOrCreate("second"));

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
        manager.Create("temp");

        var cleaned = SpinWait.SpinUntil(() => manager.Get("temp") is null, TimeSpan.FromSeconds(2));

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
        var sandbox = manager.Create();

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
