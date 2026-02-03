using AgentSandbox.Core;
using BenchmarkDotNet.Attributes;

namespace AgentSandbox.Benchmarks;

/// <summary>
/// Benchmarks for sandbox initialization with pre-populated files.
/// </summary>
[MemoryDiagnoser]
public class SandboxWithFilesBenchmark
{
    [Params(10, 100, 500)]
    public int FileCount { get; set; }

    [Benchmark]
    public Sandbox CreateSandboxWithFiles()
    {
        var sandbox = new Sandbox();
        sandbox.Execute("mkdir -p /data");
        for (int i = 0; i < FileCount; i++)
        {
            sandbox.Execute($"echo \"content-{i}\" > /data/file-{i}.txt");
        }
        return sandbox;
    }
}
