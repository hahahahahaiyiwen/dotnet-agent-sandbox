using AgentSandbox.Core;
using AgentSandbox.Core.Shell;
using System.Diagnostics;
using System.Threading;

namespace AgentSandbox.Tests;

public class SandboxConcurrencyTests
{
    [Fact]
    public async Task Execute_DefaultMode_ConcurrentCommandFailsFast()
    {
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            ShellExtensions = [new BlockingCommand(started, release)]
        });

        var first = Task.Run(() => sandbox.Execute("block"));
        Assert.True(started.Wait(TimeSpan.FromSeconds(1)));

        var ex = Assert.Throws<InvalidOperationException>(() => sandbox.Execute("pwd"));
        Assert.Contains("already in progress", ex.Message, StringComparison.OrdinalIgnoreCase);

        release.Set();
        var result = await first;
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ConcurrentReadFileLines_AllowedForSameSandbox()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteFile("/shared.txt", "line1\nline2\nline3");

        using var start = new ManualResetEventSlim(false);
        var read1 = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            return sandbox.ReadFileLines("/shared.txt", 1, null).ToArray();
        });
        var read2 = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            return sandbox.ReadFileLines("/shared.txt", 2, null).ToArray();
        });

        start.Set();
        await Task.WhenAll(read1, read2);

        Assert.Equal(["line1", "line2", "line3"], read1.Result);
        Assert.Equal(["line2", "line3"], read2.Result);
    }

    [Fact]
    public async Task ConcurrentWriteFile_SerializedWithoutConflictErrors()
    {
        using var sandbox = new Sandbox();
        using var start = new ManualResetEventSlim(false);

        var write1 = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            sandbox.WriteFile("/shared.txt", "first");
        });
        var write2 = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            sandbox.WriteFile("/shared.txt", "second");
        });

        start.Set();
        await Task.WhenAll(write1, write2);

        var persisted = string.Join("\n", sandbox.ReadFileLines("/shared.txt"));
        Assert.Contains(persisted, ["first", "second"]);
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

    [Fact]
    public void Dispose_WhenTimedOutCommandIsRunning_DoesNotThrow_AndRejectsNewOperations()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            CommandTimeout = TimeSpan.FromMilliseconds(20),
            ShellExtensions = [new SlowCommand()]
        });

        var timeoutResult = sandbox.Execute("slow 200");
        Assert.False(timeoutResult.Success);
        Assert.Contains("timed out", timeoutResult.Stderr, StringComparison.OrdinalIgnoreCase);

        var ex = Record.Exception(() => sandbox.Dispose());
        Assert.Null(ex);

        Assert.Throws<ObjectDisposedException>(() => sandbox.WriteFile("/after-dispose.txt", "blocked"));
    }

    [Fact]
    public async Task Execute_WhenIsolatedParallelEnabled_AllowsConcurrentCommands()
    {
        var overlap = new OverlapTracker();
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            ShellExtensions = [new ParallelSafeSlowCommand(overlap)]
        });

        using var start = new ManualResetEventSlim(false);
        var t1 = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            return sandbox.Execute("pslow 250");
        });
        var t2 = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            return sandbox.Execute("pslow 250");
        });

        start.Set();
        var results = await Task.WhenAll(t1, t2);

        Assert.All(results, r => Assert.True(r.Success, r.Stderr));
        Assert.True(overlap.ObservedOverlap, "Expected overlapping isolated command execution.");
    }

    [Fact]
    public void Execute_WhenIsolatedParallelEnabled_DoesNotPersistCommandLocalEnvironment()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true
        });

        var setResult = sandbox.Execute("export PHASE3_VAR=ok && echo $PHASE3_VAR");
        Assert.True(setResult.Success);
        Assert.Contains("ok", setResult.Stdout, StringComparison.Ordinal);

        var snapshot = sandbox.CreateSnapshot();
        Assert.False(snapshot.Environment.ContainsKey("PHASE3_VAR"));
    }

    [Fact]
    public void Execute_WhenIsolatedParallelEnabled_RejectsNonParallelSafeExtensions()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            ShellExtensions = [new NonParallelSafeCommand()]
        });

        var result = sandbox.Execute("unsafe");

        Assert.False(result.Success);
        Assert.Contains("Parallel isolated execution is not supported", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WhenIsolatedParallelEnabled_DoesNotPersistCwdMutation()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true
        });

        var result = sandbox.Execute("mkdir /tmp-cwd && cd /tmp-cwd && pwd");
        Assert.True(result.Success);
        Assert.Contains("/tmp-cwd", result.Stdout, StringComparison.Ordinal);

        Assert.Equal("/", sandbox.CurrentDirectory);
    }

    [Fact]
    public void Execute_WhenIsolatedParallelEnabled_DoesNotPersistFilesystemMutation()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true
        });

        var result = sandbox.Execute("echo 'phase3' > /isolated.txt");
        Assert.True(result.Success);

        Assert.Throws<FileNotFoundException>(() => sandbox.ReadFileLines("/isolated.txt").ToList());
    }

    [Fact]
    public void IsolatedTimeout_BlocksSubsequentOperationsUntilBackgroundCommandCompletes()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            CommandTimeout = TimeSpan.FromMilliseconds(20),
            ShellExtensions = [new ParallelSafeSlowCommand()]
        });

        var timeoutResult = sandbox.Execute("pslow 200");
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

    [Fact]
    public void Dispose_WhenIsolatedTimedOutCommandIsRunning_DoesNotThrow_AndRejectsNewOperations()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            CommandTimeout = TimeSpan.FromMilliseconds(20),
            ShellExtensions = [new ParallelSafeSlowCommand()]
        });

        var timeoutResult = sandbox.Execute("pslow 200");
        Assert.False(timeoutResult.Success);
        Assert.Contains("timed out", timeoutResult.Stderr, StringComparison.OrdinalIgnoreCase);

        var ex = Record.Exception(() => sandbox.Dispose());
        Assert.Null(ex);

        Assert.Throws<ObjectDisposedException>(() => sandbox.WriteFile("/after-dispose.txt", "blocked"));
    }

    [Fact]
    public async Task RestoreSnapshot_WaitsForInFlightIsolatedCommand()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            ShellExtensions = [new ParallelSafeSlowCommand()]
        });

        var commandTask = Task.Run(() => sandbox.Execute("pslow 250"));
        await Task.Delay(40);

        var restoreCompleted = false;
        var restoreTask = Task.Run(() =>
        {
            var snapshot = sandbox.CreateSnapshot();
            sandbox.RestoreSnapshot(snapshot);
            restoreCompleted = true;
        });

        await Task.Delay(40);
        Assert.False(restoreCompleted);

        await commandTask;
        await restoreTask;
        Assert.True(restoreCompleted);
    }

    [Fact]
    public async Task CreateSnapshot_CanRunDuringInFlightIsolatedCommand()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            ShellExtensions = [new ParallelSafeSlowCommand()]
        });

        var commandTask = Task.Run(() => sandbox.Execute("pslow 250"));
        await Task.Delay(40);

        var snapshotTask = Task.Run(() => sandbox.CreateSnapshot());
        var completed = await Task.WhenAny(snapshotTask, Task.Delay(500));

        Assert.Same(snapshotTask, completed);
        var snapshot = await snapshotTask;
        Assert.NotNull(snapshot);

        await commandTask;
    }

    [Fact]
    public async Task IsolatedMode_TimeoutThenImmediateRetryEventuallySucceeds()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            CommandTimeout = TimeSpan.FromMilliseconds(20),
            ShellExtensions = [new ParallelSafeSlowCommand()]
        });

        var timeoutResult = sandbox.Execute("pslow 200");
        Assert.False(timeoutResult.Success);

        var wait = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var retry = sandbox.Execute("pwd");
                if (retry.Success)
                {
                    Assert.Contains("/", retry.Stdout, StringComparison.Ordinal);
                    break;
                }
            }
            catch (InvalidOperationException ex) when (IsPhase1BusyError(ex))
            {
                if (wait.Elapsed > TimeSpan.FromSeconds(2))
                {
                    throw new TimeoutException("Isolated-mode retry did not recover after timed-out command completion.");
                }
            }

            Thread.Sleep(20);
        }
    }

    [Fact]
    public async Task IsolatedParallel_OneTimedOutCommand_OneSuccessfulCommand()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            CommandTimeout = TimeSpan.FromMilliseconds(80),
            ShellExtensions = [new ParallelSafeSlowCommand()]
        });

        using var start = new ManualResetEventSlim(false);
        var slow = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            return sandbox.Execute("pslow 250");
        });
        var fast = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            return sandbox.Execute("pwd");
        });

        start.Set();
        var results = await Task.WhenAll(slow, fast);

        Assert.Contains(results, r => !r.Success && r.Stderr.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => r.Success);
    }

    [Fact]
    public async Task IsolatedMode_ParallelCommands_DoNotLeakEnvironmentAcrossCommands()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true
        });

        using var start = new ManualResetEventSlim(false);
        var setter = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            return sandbox.Execute("export TEMP_KEY=shadow && echo set");
        });
        var reader = Task.Run(() =>
        {
            Assert.True(start.Wait(TimeSpan.FromSeconds(1)));
            return sandbox.Execute("echo $TEMP_KEY");
        });

        start.Set();
        var results = await Task.WhenAll(setter, reader);

        Assert.All(results, r => Assert.True(r.Success, r.Stderr));
        Assert.Contains(results, r => r.Stdout.Contains("set", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => string.IsNullOrWhiteSpace(r.Stdout));
    }

    [Fact]
    public void IsolatedMode_CommandHistory_RecordsBothSuccessAndTimeout()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            EnableIsolatedParallelCommandExecution = true,
            CommandTimeout = TimeSpan.FromMilliseconds(20),
            ShellExtensions = [new ParallelSafeSlowCommand()]
        });

        var timeoutResult = sandbox.Execute("pslow 200");
        Assert.False(timeoutResult.Success);

        Thread.Sleep(250);

        var successResult = sandbox.Execute("pwd");
        Assert.True(successResult.Success);

        var history = sandbox.GetHistory();
        Assert.True(history.Count >= 2);
        Assert.Contains(history, h => !h.Success && h.Command.Contains("pslow", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(history, h => h.Success && h.Command.Contains("pwd", StringComparison.OrdinalIgnoreCase));
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
            if (!_release.Wait(TimeSpan.FromSeconds(2)))
            {
                return ShellResult.Error("timeout waiting for test release");
            }

            return ShellResult.Ok("done");
        }
    }

    private sealed class ParallelSafeSlowCommand : IParallelSafeShellCommand
    {
        private readonly OverlapTracker? _overlap;

        public ParallelSafeSlowCommand(OverlapTracker? overlap = null)
        {
            _overlap = overlap;
        }

        public string Name => "pslow";
        public string Description => "Sleeps for the given milliseconds.";

        public ShellResult Execute(string[] args, IShellContext context)
        {
            var delayMs = int.Parse(args[0]);
            _overlap?.Enter();
            try
            {
                Thread.Sleep(delayMs);
                return ShellResult.Ok("done");
            }
            finally
            {
                _overlap?.Exit();
            }
        }
    }

    private sealed class NonParallelSafeCommand : IShellCommand
    {
        public string Name => "unsafe";
        public string Description => "Not marked as parallel safe.";

        public ShellResult Execute(string[] args, IShellContext context)
        {
            return ShellResult.Ok("unsafe");
        }
    }

    private static bool IsPhase1BusyError(InvalidOperationException ex)
    {
        return ex.Message.Contains("timed-out command", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("already in progress", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OverlapTracker
    {
        private int _active;

        public bool ObservedOverlap { get; private set; }

        public void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            if (active > 1)
            {
                ObservedOverlap = true;
            }
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
