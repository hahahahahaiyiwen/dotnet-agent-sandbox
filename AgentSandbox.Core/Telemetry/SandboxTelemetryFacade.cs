using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgentSandbox.Core.Shell;

namespace AgentSandbox.Core.Telemetry;

/// <summary>
/// Centralizes all telemetry emit operations for a Sandbox instance.
/// Encapsulates metrics recording, activity management, and event emission.
/// This facade eliminates scattered telemetry calls throughout Sandbox.cs.
/// </summary>
internal sealed class SandboxTelemetryFacade
{
    private readonly SandboxOptions _options;
    private readonly string _sandboxId;
    private readonly Action<Action<ISandboxObserver>> _notifyObservers;

    public SandboxTelemetryFacade(SandboxOptions options, string sandboxId,
        Action<Action<ISandboxObserver>> notifyObservers)
    {
        _options = options;
        _sandboxId = sandboxId;
        _notifyObservers = notifyObservers;
    }

    private bool TelemetryEnabled => _options.Telemetry?.Enabled == true;

    #region Lifecycle Management

    /// <summary>
    /// Records sandbox creation metrics and lifecycle event.
    /// </summary>
    public void RecordSandboxCreated()
    {
        if (!TelemetryEnabled)
            return;

        var instanceId = _options.Telemetry!.InstanceId;
        SandboxTelemetry.SandboxesCreated.Add(1,
            new KeyValuePair<string, object?>("sandbox.id", _sandboxId),
            new KeyValuePair<string, object?>("service.instance.id", instanceId));
        SandboxTelemetry.ActiveSandboxes.Add(1,
            new KeyValuePair<string, object?>("service.instance.id", instanceId));

        EmitLifecycleEvent(SandboxLifecycleType.Created);
    }

    /// <summary>
    /// Records sandbox disposal metrics and lifecycle event.
    /// </summary>
    public void RecordSandboxDisposed()
    {
        if (!TelemetryEnabled)
            return;

        EmitLifecycleEvent(SandboxLifecycleType.Disposed);

        var instanceId = _options.Telemetry!.InstanceId;
        SandboxTelemetry.SandboxesDisposed.Add(1,
            new KeyValuePair<string, object?>("sandbox.id", _sandboxId),
            new KeyValuePair<string, object?>("service.instance.id", instanceId));
        SandboxTelemetry.ActiveSandboxes.Add(-1,
            new KeyValuePair<string, object?>("service.instance.id", instanceId));
    }

    #endregion

    #region Command Execution Telemetry

    /// <summary>
    /// Starts a distributed tracing activity for command execution.
    /// Returns null if telemetry tracing is not enabled.
    /// </summary>
    public Activity? StartCommandActivity(string command)
    {
        if (TelemetryEnabled && _options.Telemetry!.TraceCommands)
        {
            return SandboxTelemetry.StartCommandActivity(command, _sandboxId);
        }
        return null;
    }

    /// <summary>
    /// Records successful command execution metrics and emits event.
    /// </summary>
    public void RecordCommandSuccess(string command, ShellResult result, TimeSpan duration)
    {
        if (!TelemetryEnabled)
            return;

        var commandName = SandboxTelemetry.GetCommandName(command);
        var tags = BuildBaseTags(commandName);

        SandboxTelemetry.CommandsExecuted.Add(1, tags);
        SandboxTelemetry.CommandDuration.Record(duration.TotalMilliseconds, tags);

        if (!result.Success)
        {
            SandboxTelemetry.CommandsFailed.Add(1, tags);
        }

        Activity.Current?.SetTag("command.exit_code", result.ExitCode);
        Activity.Current?.SetTag("command.duration_ms", duration.TotalMilliseconds);

        EmitCommandExecutedEvent(command, result, duration);
    }

    /// <summary>
    /// Records command execution error and emits error event.
    /// </summary>
    public void RecordCommandError(Exception ex)
    {
        if (!TelemetryEnabled)
            return;

        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
        EmitErrorEvent("Command", ex.Message, ex);
    }

    #endregion

    #region File I/O Telemetry - Read Operations

    /// <summary>
    /// Starts a distributed tracing activity for file read operation.
    /// </summary>
    public Activity? StartReadFileActivity(string path)
    {
        if (TelemetryEnabled && _options.Telemetry!.TraceFileSystem)
        {
            return SandboxTelemetry.StartCommandActivity($"read_file {path}", _sandboxId);
        }
        return null;
    }

    /// <summary>
    /// Records successful file read operation metrics.
    /// Metrics tags provide dimensions for aggregation; activity tags capture outcome details.
    /// </summary>
    public void RecordReadFileSuccess(string path, Stopwatch stopwatch, long fileSizeBytes,
        string readMode, int? startLine = null, int? endLine = null, int? linesReturned = null)
    {
        if (!TelemetryEnabled)
            return;

        var tags = BuildBaseTags("read_file");
        tags.Add("file.size_bytes", fileSizeBytes);
        tags.Add("file.read_mode", readMode);

        if (startLine.HasValue)
            tags.Add("file.start_line", startLine.Value);

        if (endLine.HasValue)
            tags.Add("file.end_line", endLine.Value);

        if (linesReturned.HasValue)
            tags.Add("file.lines_returned", linesReturned.Value);

        SandboxTelemetry.CommandsExecuted.Add(1, tags);
        SandboxTelemetry.CommandDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

        // Set outcome details on activity for trace context (dimension tags already in metrics)
        if (linesReturned.HasValue)
            Activity.Current?.SetTag("file.lines_returned", linesReturned.Value);
    }

    /// <summary>
    /// Records file read operation error and emits error event.
    /// </summary>
    public void RecordReadFileError(string path, Exception ex)
    {
        if (!TelemetryEnabled)
            return;

        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
        EmitErrorEvent("FileIO", $"ReadFile failed: {ex.Message}", ex);
    }

    #endregion

    #region File I/O Telemetry - Write Operations

    /// <summary>
    /// Starts a distributed tracing activity for file write operation.
    /// </summary>
    public Activity? StartWriteFileActivity(string path)
    {
        if (TelemetryEnabled && _options.Telemetry!.TraceFileSystem)
        {
            return SandboxTelemetry.StartCommandActivity($"write_file {path}", _sandboxId);
        }
        return null;
    }

    /// <summary>
    /// Records successful file write operation metrics.
    /// </summary>
    public void RecordWriteFileSuccess(string path, Stopwatch stopwatch, long fileSizeBytes)
    {
        if (!TelemetryEnabled)
            return;

        var tags = BuildBaseTags("write_file");
        tags.Add("file.size_bytes", fileSizeBytes);

        SandboxTelemetry.CommandsExecuted.Add(1, tags);
        SandboxTelemetry.CommandDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

        // No outcome details to add to activity (size is already in metrics as dimension)
    }

    /// <summary>
    /// Records file write operation error and emits error event.
    /// </summary>
    public void RecordWriteFileError(string path, Exception ex)
    {
        if (!TelemetryEnabled)
            return;

        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
        EmitErrorEvent("FileIO", $"WriteFile failed: {ex.Message}", ex);
    }

    #endregion

    #region File I/O Telemetry - Patch Operations

    /// <summary>
    /// Starts a distributed tracing activity for patch apply operation.
    /// </summary>
    public Activity? StartApplyPatchActivity(string path)
    {
        if (TelemetryEnabled && _options.Telemetry!.TraceFileSystem)
        {
            return SandboxTelemetry.StartCommandActivity($"apply_patch {path}", _sandboxId);
        }
        return null;
    }

    /// <summary>
    /// Records successful patch apply operation metrics.
    /// </summary>
    public void RecordApplyPatchSuccess(string path, Stopwatch stopwatch, long fileSizeBytes)
    {
        if (!TelemetryEnabled)
            return;

        var tags = BuildBaseTags("apply_patch");
        tags.Add("file.size_bytes", fileSizeBytes);

        SandboxTelemetry.CommandsExecuted.Add(1, tags);
        SandboxTelemetry.CommandDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);

        // No outcome details to add to activity (size is already in metrics as dimension)
    }

    /// <summary>
    /// Records patch apply operation error and emits error event.
    /// </summary>
    public void RecordApplyPatchError(string path, Exception ex)
    {
        if (!TelemetryEnabled)
            return;

        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
        EmitErrorEvent("FileIO", $"ApplyPatch failed: {ex.Message}", ex);
    }

    #endregion

    #region Internal Helpers

    /// <summary>
    /// Builds base TagList with common sandbox tags.
    /// </summary>
    private TagList BuildBaseTags(string commandName)
    {
        return new TagList
        {
            { "sandbox.id", _sandboxId },
            { "command.name", commandName },
            { "service.instance.id", _options.Telemetry!.InstanceId }
        };
    }

    #endregion

    #region Event Emission

    /// <summary>
    /// Emits command executed event to observers.
    /// </summary>
    private void EmitCommandExecutedEvent(string command, ShellResult result, TimeSpan duration)
    {
        var maxOutput = _options.Telemetry?.MaxOutputLength ?? 1024;
        var evt = new CommandExecutedEvent
        {
            SandboxId = _sandboxId,
            Command = command,
            CommandName = SandboxTelemetry.GetCommandName(command),
            ExitCode = result.ExitCode,
            Duration = duration,
            Output = TruncateOutput(result.Stdout, maxOutput),
            Error = result.Stderr,
            WorkingDirectory = Activity.Current?.Tags.FirstOrDefault(t => t.Key == "command.cwd").Value?.ToString() ?? "/",
            TraceId = Activity.Current?.TraceId.ToString(),
            SpanId = Activity.Current?.SpanId.ToString()
        };

        _notifyObservers(o => o.OnCommandExecuted(evt));
    }

    /// <summary>
    /// Emits lifecycle event to observers.
    /// </summary>
    private void EmitLifecycleEvent(SandboxLifecycleType lifecycleType, string? details = null)
    {
        var evt = new SandboxLifecycleEvent
        {
            SandboxId = _sandboxId,
            LifecycleType = lifecycleType,
            Details = details,
            TraceId = Activity.Current?.TraceId.ToString(),
            SpanId = Activity.Current?.SpanId.ToString()
        };

        _notifyObservers(o => o.OnLifecycleEvent(evt));
    }

    /// <summary>
    /// Emits error event to observers.
    /// </summary>
    private void EmitErrorEvent(string category, string message, Exception? ex = null)
    {
        var evt = new SandboxErrorEvent
        {
            SandboxId = _sandboxId,
            Category = category,
            Message = message,
            ExceptionType = ex?.GetType().Name,
            StackTrace = ex?.StackTrace,
            TraceId = Activity.Current?.TraceId.ToString(),
            SpanId = Activity.Current?.SpanId.ToString()
        };

        _notifyObservers(o => o.OnError(evt));
    }

    /// <summary>
    /// Truncates output string to max length.
    /// </summary>
    private static string? TruncateOutput(string? output, int maxLength)
    {
        if (output == null || output.Length <= maxLength)
            return output;
        return output[..maxLength] + "... (truncated)";
    }

    #endregion
}
