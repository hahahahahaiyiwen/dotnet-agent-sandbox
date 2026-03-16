using AgentSandbox.Core;
using AgentSandbox.Core.Shell;
using System.Diagnostics;
using System.Threading;

namespace AgentSandbox.Tests;

public class SandboxConcurrencyTests
{
    [Fact]
    public void WriteFile_WhenCommandIsInProgress_FailsFastWithDeterministicError()
    {
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            ShellExtensions = [new BlockingCommand(started, release)]
        });

        var commandTask = Task.Run(() => sandbox.Execute("block"));
        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));

        var ex = Assert.Throws<InvalidOperationException>(() => sandbox.WriteFile("/race.txt", "content"));
        Assert.Contains("already in progress", ex.Message, StringComparison.OrdinalIgnoreCase);

        release.Set();
        var result = commandTask.GetAwaiter().GetResult();
        Assert.True(result.Success);
    }

    [Fact]
    public void Timeout_BlocksSubsequentOperationsUntilBackgroundCommandCompletes()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            CommandTimeout = TimeSpan.FromMilliseconds(20),
            ShellExtensions = [new SlowCommand()]
        });

        var timeoutResult = sandbox.Execute("slow 200");
        Assert.False(timeoutResult.Success);
        Assert.Contains("timed out", timeoutResult.Stderr, StringComparison.OrdinalIgnoreCase);

        var busyEx = Assert.Throws<InvalidOperationException>(() => sandbox.WriteFile("/after-timeout.txt", "blocked"));
        Assert.Contains("timed-out command", busyEx.Message, StringComparison.OrdinalIgnoreCase);

        var wait = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                sandbox.WriteFile("/after-timeout.txt", "ready");
                break;
            }
            catch (InvalidOperationException ex) when (
                IsPhase1BusyError(ex) &&
                wait.Elapsed < TimeSpan.FromSeconds(2))
            {
                Thread.Sleep(20);
            }
        }

        var persisted = string.Join("\n", sandbox.ReadFileLines("/after-timeout.txt"));
        Assert.Equal("ready", persisted);
    }

    private sealed class BlockingCommand : IShellCommand
    {
        private readonly ManualResetEventSlim _started;
        private readonly ManualResetEventSlim _release;

        public BlockingCommand(ManualResetEventSlim started, ManualResetEventSlim release)
        {
            _started = started;
            _release = release;
        }

        public string Name => "block";
        public string Description => "Blocks until released by test";

        public ShellResult Execute(string[] args, IShellContext context)
        {
            _started.Set();
            _release.Wait();
            return ShellResult.Ok("done");
        }
    }

    private sealed class SlowCommand : IShellCommand
    {
        public string Name => "slow";
        public string Description => "Sleeps for the given milliseconds.";

        public ShellResult Execute(string[] args, IShellContext context)
        {
            var delayMs = int.Parse(args[0]);
            Thread.Sleep(delayMs);
            return ShellResult.Ok("done");
        }
    }

    private static bool IsPhase1BusyError(InvalidOperationException ex)
    {
        return ex.Message.Contains("timed-out command", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("already in progress", StringComparison.OrdinalIgnoreCase);
    }
}
