using AgentSandbox.Core;
using AgentSandbox.Core.Capabilities;
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
        var capabilityEvents = new List<CapabilityOperationEvent>();
        var observer = new DelegateSandboxObserver(onEvent: e =>
        {
            if (e is CapabilityOperationEvent capabilityEvent)
            {
                capabilityEvents.Add(capabilityEvent);
            }
        });
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

        Assert.Single(capabilityEvents);
        Assert.True(capabilityEvents[0].Success);
        Assert.Equal("telemetry-capability", capabilityEvents[0].CapabilityName);
    }

    [Fact]
    public void Sandbox_Ctor_Throws_WhenCapabilityInitializeFails()
    {
        var options = new SandboxOptions
        {
            Capabilities = [new FailingCapability()]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new Sandbox(options: options));

        Assert.Contains("failing", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(FailingCapability), ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.InnerException);
        Assert.Contains("init failed", ex.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sandbox_Ctor_WhenCapabilityInitializeFails_DisposesPreviouslyInitializedCapabilitiesInReverseOrder()
    {
        var disposeOrder = new List<string>();
        var first = new OrderedDisposableCapability("first", disposeOrder);
        var second = new OrderedDisposableCapability("second", disposeOrder);
        var options = new SandboxOptions
        {
            Capabilities =
            [
                first,
                second,
                new FailingCapability()
            ]
        };

        Assert.Throws<InvalidOperationException>(() => new Sandbox(options: options));

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.Equal(["second", "first"], disposeOrder);
    }

    [Fact]
    public void Sandbox_Ctor_Throws_WhenDuplicateCapabilityInterfaceRegistered()
    {
        var options = new SandboxOptions
        {
            Capabilities =
            [
                new DuplicateCapabilityA(),
                new DuplicateCapabilityB()
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new Sandbox(options: options));

        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public void Sandbox_Ctor_Throws_WhenCapabilityRegistersInvalidCommand()
    {
        var options = new SandboxOptions
        {
            Capabilities =
            [
                new ShellCommandsCapability([new InvalidShellCommand()])
            ]
        };

        Assert.ThrowsAny<Exception>(() => new Sandbox(options: options));
    }

    [Fact]
    public void Capability_CanResolveOtherCapability_DuringInitialize()
    {
        var options = new SandboxOptions
        {
            Capabilities =
            [
                new DependencyProviderCapability(),
                new DependencyConsumerCapability()
            ]
        };

        using var sandbox = new Sandbox(options: options);
        var consumer = sandbox.GetCapability<IDependencyConsumerCapability>();

        Assert.True(consumer.DependencyResolved);
    }

    [Fact]
    public void SandboxOptions_CapabilityInstances_AreInitializedPerSandbox_WhenReused()
    {
        var reusable = new ReusableCapability();
        var options = new SandboxOptions
        {
            Capabilities = [reusable]
        };

        using var sandbox1 = new Sandbox(options: options);
        using var sandbox2 = new Sandbox(options: options);

        Assert.Equal(2, reusable.InitializeCount);
    }

    [Fact]
    public void Sandbox_Dispose_DisposesDisposableCapabilities_Once()
    {
        var disposableCapability = new DisposableCapability();
        var options = new SandboxOptions
        {
            Capabilities = [disposableCapability]
        };

        using (var sandbox = new Sandbox(options: options))
        {
            // dispose by using
        }

        Assert.Equal(1, disposableCapability.DisposeCount);
    }

    [Fact]
    public void Sandbox_Dispose_DisposesCapabilitiesInReverseRegistrationOrder_AndIsIdempotent()
    {
        var disposeOrder = new List<string>();
        var first = new OrderedDisposableCapability("first", disposeOrder);
        var second = new OrderedDisposableCapability("second", disposeOrder);
        var options = new SandboxOptions
        {
            Capabilities = [first, second]
        };

        var sandbox = new Sandbox(options: options);
        sandbox.Dispose();
        sandbox.Dispose();

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(1, second.DisposeCount);
        Assert.Equal(["second", "first"], disposeOrder);
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

    private sealed class FailingCapability : ISandboxCapability
    {
        public string Name => "failing";
        public void Initialize(ISandboxContext context) => throw new InvalidOperationException("init failed");
    }

    private interface IConflictingCapability
    {
        string Marker();
    }

    private sealed class DuplicateCapabilityA : ISandboxCapability, IConflictingCapability
    {
        public string Name => "dup-a";
        public void Initialize(ISandboxContext context) { }
        public string Marker() => "a";
    }

    private sealed class DuplicateCapabilityB : ISandboxCapability, IConflictingCapability
    {
        public string Name => "dup-b";
        public void Initialize(ISandboxContext context) { }
        public string Marker() => "b";
    }

    private interface IDependencyProviderCapability
    {
        string Value { get; }
    }

    private sealed class DependencyProviderCapability : ISandboxCapability, IDependencyProviderCapability
    {
        public string Name => "provider";
        public string Value => "ready";
        public void Initialize(ISandboxContext context) { }
    }

    private interface IDependencyConsumerCapability
    {
        bool DependencyResolved { get; }
    }

    private sealed class DependencyConsumerCapability : ISandboxCapability, IDependencyConsumerCapability
    {
        public string Name => "consumer";
        public bool DependencyResolved { get; private set; }

        public void Initialize(ISandboxContext context)
        {
            var provider = context.GetCapability<IDependencyProviderCapability>();
            DependencyResolved = provider.Value == "ready";
        }
    }

    private sealed class ReusableCapability : ISandboxCapability
    {
        public string Name => "reusable";
        public int InitializeCount { get; private set; }

        public void Initialize(ISandboxContext context)
        {
            InitializeCount++;
        }
    }

    private sealed class DisposableCapability : ISandboxCapability, IDisposable
    {
        public string Name => "disposable";
        public int DisposeCount { get; private set; }
        public void Initialize(ISandboxContext context) { }
        public void Dispose() => DisposeCount++;
    }

    private sealed class OrderedDisposableCapability : ISandboxCapability, IDisposable
    {
        private readonly List<string> _disposeOrder;
        public string Name { get; }
        public int DisposeCount { get; private set; }

        public OrderedDisposableCapability(string name, List<string> disposeOrder)
        {
            Name = name;
            _disposeOrder = disposeOrder;
        }

        public void Initialize(ISandboxContext context) { }

        public void Dispose()
        {
            DisposeCount++;
            _disposeOrder.Add(Name);
        }
    }

    private sealed class InvalidShellCommand : IShellCommand
    {
        public string Name => null!;
        public string Description => "invalid";

        public ShellResult Execute(string[] args, IShellContext context)
        {
            return ShellResult.Ok();
        }
    }

    private interface ITelemetryCapability
    {
        void Run();
    }

    private sealed class TelemetryCapability : ISandboxCapability, ITelemetryCapability
    {
        private ISandboxEventEmitter? _eventEmitter;
        public string Name => "telemetry-capability";

        public void Initialize(ISandboxContext context)
        {
            _eventEmitter = context.EventEmitter;
        }

        public void Run()
        {
            _eventEmitter!.Emit(SandboxCapabilityEventHelper.CreateSuccessEvent(
                sandboxId: "capability-test",
                capabilityName: Name,
                operationType: "capability.demo",
                operationName: "run",
                metadata: new Dictionary<string, object?> { ["demo.step"] = "sample" }));
        }
    }
}
