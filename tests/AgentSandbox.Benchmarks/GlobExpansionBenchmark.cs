using AgentSandbox.Core;
using AgentSandbox.Core.Shell;
using BenchmarkDotNet.Attributes;

namespace AgentSandbox.Benchmarks;

/// <summary>
/// Benchmarks for glob expansion with varying directory sizes.
/// </summary>
[MemoryDiagnoser]
public class GlobExpansionBenchmark
{
    [Params(100, 500, 1000)]
    public int FileCount { get; set; }

    private Sandbox _sandbox = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sandbox = new Sandbox();
        _sandbox.Execute("mkdir -p /data");
        for (int i = 0; i < FileCount; i++)
        {
            _sandbox.Execute($"touch /data/file-{i}.txt");
        }
        // Add some non-matching files
        for (int i = 0; i < 50; i++)
        {
            _sandbox.Execute($"touch /data/other-{i}.log");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sandbox.Dispose();
    }

    [Benchmark]
    public ShellResult GlobLs()
    {
        return _sandbox.Execute("ls /data/*.txt");
    }

    [Benchmark]
    public ShellResult GlobCat()
    {
        return _sandbox.Execute("cat /data/file-0.txt /data/file-1.txt /data/file-2.txt");
    }

    [Benchmark]
    public ShellResult FindByName()
    {
        return _sandbox.Execute("find /data -name \"*.txt\"");
    }
}
