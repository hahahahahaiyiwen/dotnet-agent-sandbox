using AgentSandbox.Core;
using BenchmarkDotNet.Attributes;

namespace AgentSandbox.Benchmarks;

/// <summary>
/// Benchmarks for resource limit enforcement.
/// </summary>
[MemoryDiagnoser]
public class ResourceLimitsBenchmark
{
    [Params(512, 1024, 4096)]
    public int MaxTotalSizeBytes { get; set; }

    private SandboxOptions _options = null!;

    [GlobalSetup]
    public void Setup()
    {
        _options = new SandboxOptions
        {
            MaxTotalSize = MaxTotalSizeBytes,
            MaxFileSize = MaxTotalSizeBytes / 2
        };
    }

    [Benchmark]
    public int WriteUntilQuotaExceeded()
    {
        using var sandbox = new Sandbox(options: _options);
        sandbox.Execute("mkdir -p /data");

        var payload = new string('x', 100); // 100 bytes per file
        int writesSucceeded = 0;

        for (int i = 0; i < 100; i++)
        {
            var result = sandbox.Execute($"echo \"{payload}\" > /data/file-{i}.txt");
            if (!result.Success)
                break;
            writesSucceeded++;
        }

        return writesSucceeded;
    }
}
