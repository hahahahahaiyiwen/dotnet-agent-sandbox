using AgentSandbox.Core;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Extensions;

namespace AgentSandbox.Tests;

public class SandboxCapabilityTests
{
    [Fact]
    public void Sandbox_RegistersShellCommand_FromCapability()
    {
        var options = new SandboxOptions
        {
            Capabilities =
            [
                new ShellCommandsCapability([new HelloCommand()])
            ]
        };

        using var sandbox = new Sandbox(options: options);
        var result = sandbox.Execute("hello");

        Assert.True(result.Success);
        Assert.Equal("from-capability", result.Stdout);
    }

    [Fact]
    public void SandboxOptions_Clone_IncludesCapabilities()
    {
        var options = new SandboxOptions
        {
            Capabilities =
            [
                new ShellCommandsCapability([new HelloCommand()])
            ]
        };

        var clone = options.Clone();

        Assert.Single(clone.Capabilities);
    }

    [Fact]
    public void Sandbox_GetCapability_ReturnsFeatureInterface()
    {
        var options = new SandboxOptions
        {
            Capabilities =
            [
                new TestCapability()
            ]
        };

        using var sandbox = new Sandbox(options: options);
        var capability = sandbox.GetCapability<ITestCapability>();

        Assert.Equal("pong", capability.Ping());
    }

    [Fact]
    public void Sandbox_TryGetCapability_ReturnsFalse_WhenNotRegistered()
    {
        using var sandbox = new Sandbox();

        var found = sandbox.TryGetCapability<ITestCapability>(out var capability);

        Assert.False(found);
        Assert.Null(capability);
    }

    [Fact]
    public void Capability_CanEmitTelemetryOperation_ThroughSandboxTelemetry()
    {
        var errors = new List<SandboxErrorEvent>();
        var observer = new DelegateSandboxObserver(onError: e => errors.Add(e));
        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true },
            Capabilities =
            [
                new TelemetryCapability()
            ]
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);

        var capability = sandbox.GetCapability<ITelemetryCapability>();
        capability.Run();

        Assert.Empty(errors);
    }

    private sealed class HelloCommand : IShellCommand
    {
        public string Name => "hello";
        public string Description => "Returns a test value.";

        public ShellResult Execute(string[] args, IShellContext context)
        {
            return ShellResult.Ok("from-capability");
        }
    }

    private interface ITestCapability
    {
        string Ping();
    }

    private sealed class TestCapability : ISandboxCapability, ITestCapability
    {
        public string Name => "test-capability";

        public void Initialize(ISandboxContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
        }

        public string Ping() => "pong";
    }

    private interface ITelemetryCapability
    {
        void Run();
    }

    private sealed class TelemetryCapability : ISandboxCapability, ITelemetryCapability
    {
        private ISandboxTelemetry? _telemetry;
        public string Name => "telemetry-capability";

        public void Initialize(ISandboxContext context)
        {
            _telemetry = context.Telemetry;
        }

        public void Run()
        {
            var operation = _telemetry!.StartOperation(
                operationType: "capability.demo",
                operationName: "run",
                capabilityName: Name,
                tags: new Dictionary<string, object?> { ["demo.step"] = "sample" });
            operation.Complete();
        }
    }
}
