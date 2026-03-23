using System.Diagnostics;
using System.Reflection;
using AgentSandbox.Core;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Core.Shell;

namespace AgentSandbox.Tests;

public class TelemetryFacadeTests
{
    [Fact]
    public void TelemetryFacade_WithTelemetryDisabled_SkipsActivitiesAndEventEmission()
    {
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions
            {
                Enabled = false,
                TraceCommands = true,
                TraceFileSystem = true
            }
        }, emitter);

        Assert.Null(Invoke<Activity?>(facade, "StartCommandActivity", "echo hi"));
        Assert.Null(Invoke<Activity?>(facade, "StartReadFileActivity", "/file.txt"));
        Assert.Null(Invoke<Activity?>(facade, "StartWriteFileActivity", "/file.txt"));
        Assert.Null(Invoke<Activity?>(facade, "StartApplyPatchActivity", "/file.txt"));
        Assert.Null(Invoke<Activity?>(facade, "StartOperationActivity", "capability", "run", "sql"));

        Invoke(facade, "RecordSandboxCreated");
        Invoke(facade, "RecordSandboxDisposed");
        Invoke(facade, "RecordSandboxExecuted", "echo", 0, TimeSpan.FromMilliseconds(1));
        Invoke(facade, "RecordSnapshotRestored", "snap-1");
        Invoke(facade, "RecordCommandError", new InvalidOperationException("boom"));
        Invoke(facade, "RecordReadFileError", "/file.txt", new InvalidOperationException("boom"));
        Invoke(facade, "RecordWriteFileError", "/file.txt", new InvalidOperationException("boom"));
        Invoke(facade, "RecordApplyPatchError", "/file.txt", new InvalidOperationException("boom"));

        Assert.Empty(emitter.Events);
    }

    [Fact]
    public void TelemetryFacade_WithTracingEnabled_StartsActivitiesAndSetsOperationTags()
    {
        using var listener = CreateTelemetryListener();
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions
            {
                Enabled = true,
                TraceCommands = true,
                TraceFileSystem = true
            }
        }, emitter);

        using var commandActivity = Invoke<Activity?>(facade, "StartCommandActivity", "echo hello");
        Assert.NotNull(commandActivity);

        using var readActivity = Invoke<Activity?>(facade, "StartReadFileActivity", "/a.txt");
        Assert.NotNull(readActivity);

        using var writeActivity = Invoke<Activity?>(facade, "StartWriteFileActivity", "/a.txt");
        Assert.NotNull(writeActivity);

        using var patchActivity = Invoke<Activity?>(facade, "StartApplyPatchActivity", "/a.txt");
        Assert.NotNull(patchActivity);

        using var operationActivity = Invoke<Activity?>(facade, "StartOperationActivity", "capability", "run", "sql");
        Assert.NotNull(operationActivity);
        Assert.Contains(operationActivity.TagObjects, t => t.Key == "operation.type" && Equals(t.Value, "capability"));
        Assert.Contains(operationActivity.TagObjects, t => t.Key == "operation.name" && Equals(t.Value, "run"));
        Assert.Contains(operationActivity.TagObjects, t => t.Key == "capability.name" && Equals(t.Value, "sql"));

        using var noCapabilityActivity = Invoke<Activity?>(facade, "StartOperationActivity", "capability", "run", null);
        Assert.NotNull(noCapabilityActivity);
        Assert.DoesNotContain(noCapabilityActivity.TagObjects, t => t.Key == "capability.name");
    }

    [Fact]
    public void TelemetryFacade_StartOperationActivity_WithTracingDisabled_ReturnsNull()
    {
        using var listener = CreateTelemetryListener();
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions
            {
                Enabled = true,
                TraceCommands = false,
                TraceFileSystem = false
            }
        }, emitter);

        Assert.Null(Invoke<Activity?>(facade, "StartOperationActivity", "capability", "run", "sql"));
    }

    [Fact]
    public void TelemetryFacade_RecordOperationSuccess_WithCapabilityAndNoActivity_DoesNotThrow()
    {
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        }, emitter);

        var tags = new Dictionary<string, object?> { ["custom.key"] = "value" };

        Invoke(facade, "RecordOperationSuccess", "capability", "run", "sql", TimeSpan.FromMilliseconds(3), tags);
    }

    [Fact]
    public void TelemetryFacade_RecordReadFileSuccess_WithoutLinesReturned_DoesNotSetLinesReturnedTag()
    {
        using var listener = CreateTelemetryListener();
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        }, emitter);

        using var activity = SandboxTelemetryHelper.ActivitySource.StartActivity("facade.read.no-lines");
        Assert.NotNull(activity);
        var stopwatch = Stopwatch.StartNew();
        stopwatch.Stop();

        Invoke(facade, "RecordReadFileSuccess", "/a.txt", stopwatch, 42L, "partial", (int?)null, (int?)null, (int?)null);

        Assert.DoesNotContain(activity.TagObjects, t => t.Key == "file.lines_returned");
    }

    [Fact]
    public void TelemetryFacade_RecordReadWritePatchSuccess_SetsOperationAndOutcomeTags()
    {
        using var listener = CreateTelemetryListener();
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        }, emitter);

        using var activity = SandboxTelemetryHelper.ActivitySource.StartActivity("facade.success");
        Assert.NotNull(activity);
        var stopwatch = Stopwatch.StartNew();
        stopwatch.Stop();

        Invoke(facade, "RecordReadFileSuccess", "/a.txt", stopwatch, 42L, "partial", (int?)2, (int?)5, (int?)3);
        Assert.Contains(activity.TagObjects, t => t.Key == "operation.type" && Equals(t.Value, "file.read"));
        Assert.Contains(activity.TagObjects, t => t.Key == "operation.success" && Equals(t.Value, true));
        Assert.Contains(activity.TagObjects, t => t.Key == "file.lines_returned" && Equals(t.Value, 3));

        Invoke(facade, "RecordWriteFileSuccess", "/a.txt", stopwatch, 4L);
        Assert.Contains(activity.TagObjects, t => t.Key == "operation.name" && Equals(t.Value, "write_file"));

        Invoke(facade, "RecordApplyPatchSuccess", "/a.txt", stopwatch, 6L);
        Assert.Contains(activity.TagObjects, t => t.Key == "operation.name" && Equals(t.Value, "apply_patch"));
    }

    [Fact]
    public void TelemetryFacade_RecordOperationFailure_WithoutActivity_DoesNotThrow()
    {
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        }, emitter);

        var tags = new Dictionary<string, object?> { ["custom.key"] = "custom-value" };
        Invoke(facade, "RecordOperationFailure", "capability", "run", "sql", "failed", "E123", tags);
    }

    [Fact]
    public void TelemetryFacade_RecordCommandSuccess_WithoutCommandCwdTag_UsesRootWorkingDirectory()
    {
        using var listener = CreateTelemetryListener();
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        }, emitter);

        using var activity = SandboxTelemetryHelper.ActivitySource.StartActivity("facade.command-success");
        Assert.NotNull(activity);
        var result = ShellResult.Ok("output");

        Invoke(facade, "RecordCommandSuccess", "echo output", result, TimeSpan.FromMilliseconds(2));

        var commandEvent = Assert.Single(emitter.Events.OfType<CommandExecutedEvent>());
        Assert.Equal("/", commandEvent.WorkingDirectory);
        Assert.Equal("echo", commandEvent.CommandName);
    }

    [Fact]
    public void TelemetryFacade_RecordOperationFailure_SetsErrorAndCapabilityTags()
    {
        using var listener = CreateTelemetryListener();
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        }, emitter);

        using var activity = SandboxTelemetryHelper.ActivitySource.StartActivity("facade.failure");
        Assert.NotNull(activity);

        var tags = new Dictionary<string, object?> { ["custom.key"] = "custom-value" };
        Invoke(facade, "RecordOperationFailure", "capability", "run", "sql", "failed", "E123", tags);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("failed", activity.StatusDescription);
        Assert.Contains(activity.TagObjects, t => t.Key == "operation.type" && Equals(t.Value, "capability"));
        Assert.Contains(activity.TagObjects, t => t.Key == "operation.name" && Equals(t.Value, "run"));
        Assert.Contains(activity.TagObjects, t => t.Key == "operation.success" && Equals(t.Value, false));
        Assert.Contains(activity.TagObjects, t => t.Key == "capability.name" && Equals(t.Value, "sql"));
        Assert.Contains(activity.TagObjects, t => t.Key == "error.code" && Equals(t.Value, "E123"));
    }

    [Fact]
    public void TelemetryFacade_RecordErrorMethods_EmitExpectedErrorEvents()
    {
        using var listener = CreateTelemetryListener();
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions { Enabled = true }
        }, emitter);

        using var activity = SandboxTelemetryHelper.ActivitySource.StartActivity("facade.errors");
        Assert.NotNull(activity);

        Invoke(facade, "RecordCommandError", new InvalidOperationException("command boom"));
        Invoke(facade, "RecordReadFileError", "/r.txt", new InvalidOperationException("read boom"));
        Invoke(facade, "RecordWriteFileError", "/w.txt", new InvalidOperationException("write boom"));
        Invoke(facade, "RecordApplyPatchError", "/p.txt", new InvalidOperationException("patch boom"));

        var errors = emitter.Events.OfType<SandboxErrorEvent>().ToList();
        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, e => e.Category == "Command" && e.Message.Contains("command boom", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Category == "FileIO" && e.Message.StartsWith("ReadFile failed:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Category == "FileIO" && e.Message.StartsWith("WriteFile failed:", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Category == "FileIO" && e.Message.StartsWith("ApplyPatch failed:", StringComparison.Ordinal));
        Assert.All(errors, e => Assert.Equal(activity.TraceId.ToString(), e.TraceId));
    }

    [Fact]
    public void TelemetryFacade_RecordLifecycleEvents_EmitsExpectedDetailsAndMetadata()
    {
        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = "tenant-1",
            ["requestId"] = "req-2"
        };
        var emitter = new CapturingEventEmitter();
        var facade = CreateFacade(new SandboxOptions
        {
            Telemetry = new SandboxTelemetryOptions
            {
                Enabled = true,
                HostCorrelationMetadata = metadata
            }
        }, emitter);

        Invoke(facade, "RecordSandboxCreated");
        Invoke(facade, "RecordSandboxExecuted", "echo", 0, TimeSpan.FromMilliseconds(2));
        Invoke(facade, "RecordSnapshotRestored", new object?[] { null });
        Invoke(facade, "RecordSnapshotRestored", "snap-1");
        Invoke(facade, "RecordSandboxDisposed");

        var lifecycle = emitter.Events.OfType<SandboxLifecycleEvent>().ToList();
        Assert.Equal(5, lifecycle.Count);
        Assert.Contains(lifecycle, e => e.LifecycleType == SandboxLifecycleType.Created);
        Assert.Contains(lifecycle, e => e.LifecycleType == SandboxLifecycleType.Executed && e.Details!.Contains("command=echo", StringComparison.Ordinal));
        Assert.Contains(lifecycle, e => e.LifecycleType == SandboxLifecycleType.SnapshotRestored && e.Details is null);
        Assert.Contains(lifecycle, e => e.LifecycleType == SandboxLifecycleType.SnapshotRestored && e.Details is not null && e.Details.Contains("snap-1", StringComparison.Ordinal));
        Assert.Contains(lifecycle, e => e.LifecycleType == SandboxLifecycleType.Disposed);
        Assert.All(lifecycle, e => Assert.Equal("tenant-1", e.HostCorrelationMetadata!["tenantId"]));
        Assert.All(lifecycle, e => Assert.Equal("req-2", e.HostCorrelationMetadata!["requestId"]));
    }

    private static object CreateFacade(SandboxOptions options, ISandboxEventEmitter emitter)
    {
        var facadeType = typeof(Sandbox).Assembly.GetType("AgentSandbox.Core.Telemetry.SandboxTelemetryFacade", throwOnError: true)!;
        return Activator.CreateInstance(facadeType, options, "facade-test", emitter)!;
    }

    private static T? Invoke<T>(object instance, string methodName, params object?[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        return (T?)method.Invoke(instance, args);
    }

    private static void Invoke(object instance, string methodName, params object?[] args)
    {
        _ = Invoke<object?>(instance, methodName, args);
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

    private sealed class CapturingEventEmitter : ISandboxEventEmitter
    {
        public List<SandboxEvent> Events { get; } = [];

        public void Emit(SandboxEvent sandboxEvent)
        {
            Events.Add(sandboxEvent);
        }
    }
}
