using AgentSandbox.Core;
using AgentSandbox.Core.Telemetry;

namespace AgentSandbox.Tests;

public class TelemetryTests
{
    [Fact]
    public void Sandbox_WithTelemetryDisabled_DoesNotEmitEvents()
    {
        var events = new List<SandboxEvent>();
        var observer = new DelegateSandboxObserver(
            onCommandExecuted: e => events.Add(e),
            onLifecycleEvent: e => events.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = false }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);
        sandbox.Execute("echo hello");

        // No events since telemetry is disabled
        Assert.Empty(events);
    }

    [Fact]
    public void Sandbox_WithTelemetryEnabled_EmitsLifecycleEvents()
    {
        var events = new List<SandboxLifecycleEvent>();
        var observer = new DelegateSandboxObserver(
            onLifecycleEvent: e => events.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);
        sandbox.Dispose();

        Assert.Contains(events, e => e.LifecycleType == SandboxLifecycleType.Disposed);
    }

    [Fact]
    public void Sandbox_WithTelemetryEnabled_EmitsCommandExecutedEvents()
    {
        var events = new List<CommandExecutedEvent>();
        var observer = new DelegateSandboxObserver(
            onCommandExecuted: e => events.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);
        
        sandbox.Execute("echo hello");
        sandbox.Execute("pwd");

        Assert.Equal(2, events.Count);
        Assert.Equal("echo", events[0].CommandName);
        Assert.Equal("pwd", events[1].CommandName);
    }

    [Fact]
    public void Sandbox_CommandExecutedEvent_IncludesDetails()
    {
        CommandExecutedEvent? capturedEvent = null;
        var observer = new DelegateSandboxObserver(
            onCommandExecuted: e => capturedEvent = e);

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox("test-id", options);
        sandbox.Subscribe(observer);
        
        sandbox.Execute("echo hello world");

        Assert.NotNull(capturedEvent);
        Assert.Equal("test-id", capturedEvent.SandboxId);
        Assert.Equal("echo hello world", capturedEvent.Command);
        Assert.Equal("echo", capturedEvent.CommandName);
        Assert.Equal(0, capturedEvent.ExitCode);
        Assert.Contains("hello world", capturedEvent.Output);
        Assert.Equal("/", capturedEvent.WorkingDirectory);
        Assert.True(capturedEvent.Duration.TotalMilliseconds >= 0);
    }

    [Fact]
    public void Sandbox_FailedCommand_HasNonZeroExitCode()
    {
        CommandExecutedEvent? capturedEvent = null;
        var observer = new DelegateSandboxObserver(
            onCommandExecuted: e => capturedEvent = e);

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);
        
        sandbox.Execute("cat /nonexistent/file.txt");

        Assert.NotNull(capturedEvent);
        Assert.NotEqual(0, capturedEvent.ExitCode);
    }

    [Fact]
    public void Sandbox_Observer_CanUnsubscribe()
    {
        var events = new List<CommandExecutedEvent>();
        var observer = new DelegateSandboxObserver(
            onCommandExecuted: e => events.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox(options: options);
        var subscription = sandbox.Subscribe(observer);
        
        sandbox.Execute("echo first");
        Assert.Single(events);

        subscription.Dispose();
        
        sandbox.Execute("echo second");
        Assert.Single(events); // Still only 1 event after unsubscribe
    }

    [Fact]
    public void Sandbox_MultipleObservers_AllReceiveEvents()
    {
        var events1 = new List<CommandExecutedEvent>();
        var events2 = new List<CommandExecutedEvent>();
        
        var observer1 = new DelegateSandboxObserver(onCommandExecuted: e => events1.Add(e));
        var observer2 = new DelegateSandboxObserver(onCommandExecuted: e => events2.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer1);
        sandbox.Subscribe(observer2);
        
        sandbox.Execute("echo hello");

        Assert.Single(events1);
        Assert.Single(events2);
    }

    [Fact]
    public void Sandbox_OutputTruncation_RespectsMaxLength()
    {
        CommandExecutedEvent? capturedEvent = null;
        var observer = new DelegateSandboxObserver(
            onCommandExecuted: e => capturedEvent = e);

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions 
            { 
                Enabled = true,
                MaxOutputLength = 10
            }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);
        
        sandbox.Execute("echo this is a very long output string");

        Assert.NotNull(capturedEvent);
        Assert.NotNull(capturedEvent.Output);
        Assert.Contains("(truncated)", capturedEvent.Output);
    }
}
