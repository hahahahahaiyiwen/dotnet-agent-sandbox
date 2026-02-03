using AgentSandbox.Core;
using AgentSandbox.Core.Shell;
using BenchmarkDotNet.Attributes;

namespace AgentSandbox.Benchmarks;

/// <summary>
/// Benchmarks for command execution latency.
/// </summary>
[MemoryDiagnoser]
public class CommandExecutionBenchmark
{
    private static readonly string[] Commands =
    [
        "mkdir -p /project/data",
        "echo \"hello\" > /project/data/readme.txt",
        "cat /project/data/readme.txt",
        "ls /project",
        "find /project -name \"*.txt\""
    ];

    private Sandbox _sandbox = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sandbox = new Sandbox();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sandbox.Dispose();
    }

    [Benchmark]
    public void CommandMix()
    {
        foreach (var command in Commands)
        {
            _sandbox.Execute(command);
        }
    }

    [Benchmark]
    public ShellResult SingleEcho()
    {
        return _sandbox.Execute("echo hello");
    }

    [Benchmark]
    public ShellResult SingleLs()
    {
        return _sandbox.Execute("ls /");
    }

    [Benchmark]
    public ShellResult MkdirAndTouch()
    {
        _sandbox.Execute("mkdir -p /bench/dir");
        return _sandbox.Execute("touch /bench/dir/file.txt");
    }
}
