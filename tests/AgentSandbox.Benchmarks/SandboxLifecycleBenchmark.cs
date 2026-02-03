using AgentSandbox.Core;
using BenchmarkDotNet.Attributes;

namespace AgentSandbox.Benchmarks;

/// <summary>
/// Benchmarks for sandbox creation and lifecycle operations.
/// </summary>
[MemoryDiagnoser]
public class SandboxLifecycleBenchmark
{
    [Params(1, 10, 100)]
    public int SandboxCount { get; set; }

    [Benchmark]
    public List<Sandbox> CreateSandboxes()
    {
        var sandboxes = new List<Sandbox>(SandboxCount);
        for (int i = 0; i < SandboxCount; i++)
        {
            sandboxes.Add(new Sandbox());
        }
        return sandboxes;
    }

    [Benchmark]
    public void CreateAndDisposeSandboxes()
    {
        for (int i = 0; i < SandboxCount; i++)
        {
            using var sandbox = new Sandbox();
        }
    }
}
