using AgentSandbox.Core;
using AgentSandbox.Core.Shell;
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
