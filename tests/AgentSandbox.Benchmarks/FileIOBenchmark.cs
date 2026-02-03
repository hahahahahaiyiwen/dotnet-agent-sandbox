using AgentSandbox.Core;
using AgentSandbox.Core.Shell;
using BenchmarkDotNet.Attributes;

namespace AgentSandbox.Benchmarks;

/// <summary>
/// Benchmarks for file I/O operations with varying file sizes.
/// </summary>
[MemoryDiagnoser]
public class FileIOBenchmark
{
    [Params(1024, 10 * 1024, 100 * 1024, 1024 * 1024)]
    public int FileSizeBytes { get; set; }

    private Sandbox _sandbox = null!;
    private string _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sandbox = new Sandbox();
        _payload = new string('a', FileSizeBytes);
        _sandbox.Execute("mkdir -p /data");
        // Pre-create file for read benchmark
        _sandbox.Execute($"echo \"{_payload}\" > /data/existing.txt");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _sandbox.Dispose();
    }

    [Benchmark]
    public ShellResult WriteFile()
    {
        return _sandbox.Execute($"echo \"{_payload}\" > /data/payload.txt");
    }

    [Benchmark]
    public ShellResult ReadFile()
    {
        return _sandbox.Execute("cat /data/existing.txt");
    }
}
