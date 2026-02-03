// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Running;

namespace AgentSandbox.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
