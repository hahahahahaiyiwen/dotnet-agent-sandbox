using AgentSandbox.Core;
using AgentSandbox.Core.Security;
using AgentSandbox.Core.Shell.Extensions;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Core.Validation;
using System.Net;
using System.Diagnostics;

namespace AgentSandbox.Tests;

public class TelemetryTests
{
    [Fact]
    public void Sandbox_CommandValidationFailure_EmitsCommandErrorTelemetryEvent()
    {
        var errors = new List<SandboxErrorEvent>();
        var observer = new DelegateSandboxObserver(onError: e => errors.Add(e));

        var options = new SandboxOptions
        {
            MaxCommandLength = 8,
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);

        Assert.Throws<CoreValidationException>(() => sandbox.Execute("echo this command is too long"));

        var errorEvent = Assert.Single(errors);
        Assert.Equal("Command", errorEvent.Category);
        Assert.Contains("exceeds max bytes", errorEvent.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(nameof(CoreValidationException), errorEvent.ExceptionType);
    }

    [Fact]
    public void Sandbox_PathValidationFailureOnRead_EmitsFileIoErrorTelemetryEvent()
    {
        var errors = new List<SandboxErrorEvent>();
        var observer = new DelegateSandboxObserver(onError: e => errors.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);

        Assert.Throws<CoreValidationException>(() => sandbox.ReadFileLines("/safe/../secret.txt").ToList());

        var errorEvent = Assert.Single(errors);
        Assert.Equal("FileIO", errorEvent.Category);
        Assert.Contains("ReadFile failed", errorEvent.Message, StringComparison.Ordinal);
        Assert.Equal(nameof(CoreValidationException), errorEvent.ExceptionType);
    }

    [Fact]
    public void Sandbox_PathValidationFailureOnWrite_EmitsFileIoErrorTelemetryEvent()
    {
        var errors = new List<SandboxErrorEvent>();
        var observer = new DelegateSandboxObserver(onError: e => errors.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);

        Assert.Throws<CoreValidationException>(() => sandbox.WriteFile("/safe/../secret.txt", "data"));

        var errorEvent = Assert.Single(errors);
        Assert.Equal("FileIO", errorEvent.Category);
        Assert.Contains("WriteFile failed", errorEvent.Message, StringComparison.Ordinal);
        Assert.Equal(nameof(CoreValidationException), errorEvent.ExceptionType);
    }

    [Fact]
    public void Sandbox_PathValidationFailureOnApplyPatch_EmitsFileIoErrorTelemetryEvent()
    {
        var errors = new List<SandboxErrorEvent>();
        var observer = new DelegateSandboxObserver(onError: e => errors.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);

        Assert.Throws<CoreValidationException>(() => sandbox.ApplyPatch("/safe/../secret.txt", "@@ -1,1 +1,1 @@\n-a\n+b"));

        var errorEvent = Assert.Single(errors);
        Assert.Equal("FileIO", errorEvent.Category);
        Assert.Contains("ApplyPatch failed", errorEvent.Message, StringComparison.Ordinal);
        Assert.Equal(nameof(CoreValidationException), errorEvent.ExceptionType);
    }

    [Fact]
    public void Sandbox_DisableCommandTracing_StillEmitsCommandAndLifecycleEvents()
    {
        using var listener = CreateTelemetryListener();
        var startedActivities = new List<string>();
        listener.ActivityStarted = activity =>
        {
            lock (startedActivities)
            {
                startedActivities.Add(activity.OperationName);
            }
        };

        var commandEvents = new List<CommandExecutedEvent>();
        var lifecycleEvents = new List<SandboxLifecycleEvent>();
        var observer = new DelegateSandboxObserver(
            onCommandExecuted: e => commandEvents.Add(e),
            onLifecycleEvent: e => lifecycleEvents.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions
            {
                Enabled = true,
                TraceCommands = false,
                TraceFileSystem = false
            }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);

        var result = sandbox.Execute("echo hello");
        Assert.True(result.Success);

        var commandEvent = Assert.Single(commandEvents);
        Assert.Equal("echo", commandEvent.CommandName);
        Assert.Contains(lifecycleEvents, e => e.LifecycleType == SandboxLifecycleType.Executed);
        Assert.Contains(startedActivities, name => string.Equals(name, "sandbox.lifecycle", StringComparison.Ordinal));
        Assert.DoesNotContain(startedActivities, name => name.StartsWith("sandbox.command.", StringComparison.Ordinal));
    }

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
    public void Sandbox_LifecycleEvents_IncludeExecutionRestoreAndCorrelationMetadata()
    {
        var events = new List<SandboxLifecycleEvent>();
        var observer = new DelegateSandboxObserver(onLifecycleEvent: e => events.Add(e));

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions
            {
                Enabled = true,
                HostCorrelationMetadata = new Dictionary<string, string>
                {
                    ["tenantId"] = "tenant-123",
                    ["requestId"] = "req-456"
                }
            }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);

        sandbox.Execute("echo hello");
        var snapshot = sandbox.CreateSnapshot();
        sandbox.RestoreSnapshot(snapshot);
        sandbox.Dispose();

        var executedEvent = Assert.Single(events.Where(e => e.LifecycleType == SandboxLifecycleType.Executed));
        Assert.Contains("command=echo", executedEvent.Details);
        Assert.Equal("tenant-123", executedEvent.HostCorrelationMetadata!["tenantId"]);
        Assert.Equal("req-456", executedEvent.HostCorrelationMetadata["requestId"]);

        var createdSnapshotEvent = Assert.Single(events.Where(e => e.LifecycleType == SandboxLifecycleType.SnapshotCreated));
        Assert.Contains(snapshot.Id, createdSnapshotEvent.Details);
        Assert.Equal("tenant-123", createdSnapshotEvent.HostCorrelationMetadata!["tenantId"]);
        Assert.Equal("req-456", createdSnapshotEvent.HostCorrelationMetadata["requestId"]);

        var restoreEvent = Assert.Single(events.Where(e => e.LifecycleType == SandboxLifecycleType.SnapshotRestored));
        Assert.Contains(snapshot.Id, restoreEvent.Details);
        Assert.Equal("tenant-123", restoreEvent.HostCorrelationMetadata!["tenantId"]);
        Assert.Equal("req-456", restoreEvent.HostCorrelationMetadata["requestId"]);

        var disposeEvent = Assert.Single(events.Where(e => e.LifecycleType == SandboxLifecycleType.Disposed));
        Assert.Equal("tenant-123", disposeEvent.HostCorrelationMetadata!["tenantId"]);
        Assert.Equal("req-456", disposeEvent.HostCorrelationMetadata["requestId"]);
    }

    [Fact]
    public void Sandbox_SnapshotCreatedObserver_CanQuerySandboxState()
    {
        var callbackRan = false;
        Sandbox? sandbox = null;
        var observer = new DelegateSandboxObserver(onLifecycleEvent: e =>
        {
            if (e.LifecycleType != SandboxLifecycleType.SnapshotCreated)
            {
                return;
            }

            _ = sandbox!.GetStats();
            callbackRan = true;
        });

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var createdSandbox = new Sandbox(options: options);
        sandbox = createdSandbox;
        sandbox.Subscribe(observer);

        var snapshot = sandbox.CreateSnapshot();

        Assert.NotNull(snapshot);
        Assert.True(callbackRan);
    }

    [Fact]
    public void Sandbox_ExecutedObserver_CanQuerySandboxState()
    {
        var callbackRan = false;
        Sandbox? sandbox = null;
        var observer = new DelegateSandboxObserver(onLifecycleEvent: e =>
        {
            if (e.LifecycleType != SandboxLifecycleType.Executed)
            {
                return;
            }

            _ = sandbox!.GetStats();
            callbackRan = true;
        });

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var createdSandbox = new Sandbox(options: options);
        sandbox = createdSandbox;
        sandbox.Subscribe(observer);

        var result = sandbox.Execute("echo hello");

        Assert.Equal(0, result.ExitCode);
        Assert.True(callbackRan);
    }

    [Fact]
    public void Sandbox_SnapshotRestoredObserver_CanQuerySandboxState()
    {
        var callbackRan = false;
        Sandbox? sandbox = null;
        var observer = new DelegateSandboxObserver(onLifecycleEvent: e =>
        {
            if (e.LifecycleType != SandboxLifecycleType.SnapshotRestored)
            {
                return;
            }

            _ = sandbox!.GetStats();
            callbackRan = true;
        });

        var options = new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        };

        using var createdSandbox = new Sandbox(options: options);
        sandbox = createdSandbox;
        sandbox.Subscribe(observer);
        var snapshot = sandbox.CreateSnapshot();

        sandbox.RestoreSnapshot(snapshot);

        Assert.True(callbackRan);
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

    [Fact]
    public void Sandbox_ResolvedSecrets_AreRedactedFromHistoryAndTelemetry()
    {
        CommandExecutedEvent? capturedEvent = null;
        var observer = new DelegateSandboxObserver(onCommandExecuted: e => capturedEvent = e);
        var httpClient = new HttpClient(new SecretEchoHandler("super-secret-token"));

        var options = new SandboxOptions
        {
            SecretBroker = new TestSecretBroker(new Dictionary<string, string>
            {
                ["api-token"] = "super-secret-token"
            }),
            Telemetry = new SandboxTelemetryOptions { Enabled = true },
            ShellExtensions = new[] { new CurlCommand(httpClient) }
        };

        using var sandbox = new Sandbox(options: options);
        sandbox.Subscribe(observer);

        var result = sandbox.Execute("curl -H \"Authorization: Bearer secretRef:api-token\" http://example.com/api");
        var history = sandbox.GetHistory();

        Assert.NotNull(capturedEvent);
        Assert.Equal("curl", capturedEvent.CommandName);
        Assert.DoesNotContain("super-secret-token", capturedEvent.Output ?? string.Empty);
        Assert.Contains("***REDACTED***", capturedEvent.Output ?? string.Empty);
        Assert.DoesNotContain("super-secret-token", result.Stdout);
        Assert.Contains("***REDACTED***", result.Stdout);
        Assert.Single(history);
        Assert.DoesNotContain("super-secret-token", history[0].Stdout);
    }

    private sealed class TestSecretBroker : ISecretBroker
    {
        private readonly IReadOnlyDictionary<string, string> _secrets;

        public TestSecretBroker(IReadOnlyDictionary<string, string> secrets)
        {
            _secrets = secrets;
        }

        public bool TryResolve(string secretRef, out string secretValue)
        {
            return _secrets.TryGetValue(secretRef, out secretValue!);
        }
    }

    private sealed class SecretEchoHandler : HttpMessageHandler
    {
        private readonly string _secret;

        public SecretEchoHandler(string secret)
        {
            _secret = secret;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"echo:{_secret}")
            });
        }
    }

    private static ActivityListener CreateTelemetryListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SandboxTelemetryHelper.ServiceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
