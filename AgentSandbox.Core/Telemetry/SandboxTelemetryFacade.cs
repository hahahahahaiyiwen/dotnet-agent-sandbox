using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using AgentSandbox.Core.Capabilities;
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
    private readonly ISandboxEventEmitter _eventEmitter;

    public SandboxTelemetryFacade(SandboxOptions options, string sandboxId,
        ISandboxEventEmitter eventEmitter)
    {
        _options = options;
        _sandboxId = sandboxId;
        _eventEmitter = eventEmitter;
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
        SandboxTelemetryHelper.SandboxesCreated.Add(1,
            new KeyValuePair<string, object?>("sandbox.id", _sandboxId),
            new KeyValuePair<string, object?>("service.instance.id", instanceId));
        SandboxTelemetryHelper.ActiveSandboxes.Add(1,
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
        SandboxTelemetryHelper.SandboxesDisposed.Add(1,
            new KeyValuePair<string, object?>("sandbox.id", _sandboxId),
            new KeyValuePair<string, object?>("service.instance.id", instanceId));
        SandboxTelemetryHelper.ActiveSandboxes.Add(-1,
            new KeyValuePair<string, object?>("service.instance.id", instanceId));
    }

    /// <summary>
    /// Records sandbox execution lifecycle audit event.
    /// </summary>
    public void RecordSandboxExecuted(string commandName, int exitCode, TimeSpan duration)
    {
        if (!TelemetryEnabled)
            return;

        EmitLifecycleEvent(
            SandboxLifecycleType.Executed,
            $"command={commandName}; exitCode={exitCode}; durationMs={duration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Records sandbox snapshot restore lifecycle audit event.
    /// </summary>
    public void RecordSnapshotRestored(string? snapshotId = null)
    {
        if (!TelemetryEnabled)
            return;

        EmitLifecycleEvent(
            SandboxLifecycleType.SnapshotRestored,
            snapshotId is null ? null : $"snapshotId={snapshotId}");
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
            return SandboxTelemetryHelper.StartCommandActivity(command, _sandboxId);
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

        var commandName = SandboxTelemetryHelper.GetCommandName(command);
        RecordOperationSuccess("shell", commandName, null, duration);

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

        RecordOperationFailure("shell", "execute", null, ex.Message);
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
            return SandboxTelemetryHelper.StartCommandActivity($"read_file {path}", _sandboxId);
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

        var tags = new Dictionary<string, object?>
        {
            ["file.path"] = path,
            ["file.size_bytes"] = fileSizeBytes,
            ["file.read_mode"] = readMode
        };
        if (startLine.HasValue) tags["file.start_line"] = startLine.Value;
        if (endLine.HasValue) tags["file.end_line"] = endLine.Value;
        if (linesReturned.HasValue) tags["file.lines_returned"] = linesReturned.Value;

        RecordOperationSuccess("file.read", "read_file", null, stopwatch.Elapsed, tags);

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

        RecordOperationFailure("file.read", "read_file", null, ex.Message, tags: new Dictionary<string, object?> { ["file.path"] = path });
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
            return SandboxTelemetryHelper.StartCommandActivity($"write_file {path}", _sandboxId);
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
        var tags = new Dictionary<string, object?>
        {
            ["file.path"] = path,
            ["file.size_bytes"] = fileSizeBytes
        };
        RecordOperationSuccess("file.write", "write_file", null, stopwatch.Elapsed, tags);

        // No outcome details to add to activity (size is already in metrics as dimension)
    }

    /// <summary>
    /// Records file write operation error and emits error event.
    /// </summary>
    public void RecordWriteFileError(string path, Exception ex)
    {
        if (!TelemetryEnabled)
            return;

        RecordOperationFailure("file.write", "write_file", null, ex.Message, tags: new Dictionary<string, object?> { ["file.path"] = path });
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
            return SandboxTelemetryHelper.StartCommandActivity($"apply_patch {path}", _sandboxId);
        }
        return null;
    }

    /// <summary>
    /// Starts a distributed tracing activity for a generic operation.
    /// </summary>
    public Activity? StartOperationActivity(string operationType, string operationName, string? capabilityName = null)
    {
        if (!TelemetryEnabled)
            return null;

        var telemetryOptions = _options.Telemetry!;
        if (!telemetryOptions.TraceCommands && !telemetryOptions.TraceFileSystem)
            return null;

        var activity = SandboxTelemetryHelper.StartCommandActivity($"{operationType}:{operationName}", _sandboxId);
        activity?.SetTag("operation.type", operationType);
        activity?.SetTag("operation.name", operationName);
        if (!string.IsNullOrWhiteSpace(capabilityName))
        {
            activity?.SetTag("capability.name", capabilityName);
        }

        return activity;
    }

    /// <summary>
    /// Records successful patch apply operation metrics.
    /// </summary>
    public void RecordApplyPatchSuccess(string path, Stopwatch stopwatch, long fileSizeBytes)
    {
        if (!TelemetryEnabled)
            return;
        var tags = new Dictionary<string, object?>
        {
            ["file.path"] = path,
            ["file.size_bytes"] = fileSizeBytes
        };
        RecordOperationSuccess("file.patch", "apply_patch", null, stopwatch.Elapsed, tags);

        // No outcome details to add to activity (size is already in metrics as dimension)
    }

    /// <summary>
    /// Records patch apply operation error and emits error event.
    /// </summary>
    public void RecordApplyPatchError(string path, Exception ex)
    {
        if (!TelemetryEnabled)
            return;

        RecordOperationFailure("file.patch", "apply_patch", null, ex.Message, tags: new Dictionary<string, object?> { ["file.path"] = path });
        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
        EmitErrorEvent("FileIO", $"ApplyPatch failed: {ex.Message}", ex);
    }

    #endregion

    #region Internal Helpers

    /// <summary>
    /// Records successful generic operation metrics.
    /// </summary>
    public void RecordOperationSuccess(
        string operationType,
        string operationName,
        string? capabilityName,
        TimeSpan duration,
        IReadOnlyDictionary<string, object?>? tags = null)
    {
        if (!TelemetryEnabled)
            return;

        var telemetryTags = BuildOperationTags(operationType, operationName, capabilityName, tags);
        SandboxTelemetryHelper.CommandsExecuted.Add(1, telemetryTags);
        SandboxTelemetryHelper.CommandDuration.Record(duration.TotalMilliseconds, telemetryTags);

        Activity.Current?.SetTag("operation.type", operationType);
        Activity.Current?.SetTag("operation.name", operationName);
        Activity.Current?.SetTag("operation.duration_ms", duration.TotalMilliseconds);
        Activity.Current?.SetTag("operation.success", true);
        if (!string.IsNullOrWhiteSpace(capabilityName))
        {
            Activity.Current?.SetTag("capability.name", capabilityName);
        }

    }

    /// <summary>
    /// Records failed generic operation metrics and error context.
    /// </summary>
    public void RecordOperationFailure(
        string operationType,
        string operationName,
        string? capabilityName,
        string errorMessage,
        string? errorCode = null,
        IReadOnlyDictionary<string, object?>? tags = null)
    {
        if (!TelemetryEnabled)
            return;

        var telemetryTags = BuildOperationTags(operationType, operationName, capabilityName, tags);
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            telemetryTags.Add("error.code", errorCode);
        }

        SandboxTelemetryHelper.CommandsExecuted.Add(1, telemetryTags);
        SandboxTelemetryHelper.CommandsFailed.Add(1, telemetryTags);

        Activity.Current?.SetStatus(ActivityStatusCode.Error, errorMessage);
        Activity.Current?.SetTag("operation.type", operationType);
        Activity.Current?.SetTag("operation.name", operationName);
        Activity.Current?.SetTag("operation.success", false);
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            Activity.Current?.SetTag("error.code", errorCode);
        }
        if (!string.IsNullOrWhiteSpace(capabilityName))
        {
            Activity.Current?.SetTag("capability.name", capabilityName);
        }
    }

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

    private TagList BuildOperationTags(
        string operationType,
        string operationName,
        string? capabilityName,
        IReadOnlyDictionary<string, object?>? tags)
    {
        var telemetryTags = BuildBaseTags(operationName);
        telemetryTags.Add("operation.type", operationType);
        if (!string.IsNullOrWhiteSpace(capabilityName))
        {
            telemetryTags.Add("capability.name", capabilityName);
        }

        if (tags != null)
        {
            foreach (var tag in tags)
            {
                telemetryTags.Add(tag.Key, tag.Value);
            }
        }

        return telemetryTags;
    }

    #endregion

    #region Event Emission

    /// <summary>
    /// Emits command executed event to observers.
    /// </summary>
    private void EmitCommandExecutedEvent(string command, ShellResult result, TimeSpan duration)
    {
        var maxOutput = _options.Telemetry?.MaxOutputLength ?? 1024;
        var workingDirectory = Activity.Current?.Tags.FirstOrDefault(t => t.Key == "command.cwd").Value?.ToString() ?? "/";
        _eventEmitter.Emit(SandboxTelemetryHelper.CreateCommandExecutedEvent(
            _sandboxId,
            command,
            result,
            duration,
            workingDirectory,
            maxOutput,
            Activity.Current));
    }

    /// <summary>
    /// Emits lifecycle event to observers.
    /// </summary>
    private void EmitLifecycleEvent(SandboxLifecycleType lifecycleType, string? details = null)
    {
        _eventEmitter.Emit(SandboxTelemetryHelper.CreateLifecycleEvent(
            _sandboxId,
            lifecycleType,
            details,
            _options.Telemetry?.HostCorrelationMetadata,
            Activity.Current));
    }

    /// <summary>
    /// Emits error event to observers.
    /// </summary>
    private void EmitErrorEvent(string category, string message, Exception? ex = null)
    {
        _eventEmitter.Emit(SandboxTelemetryHelper.CreateErrorEvent(
            _sandboxId,
            category,
            message,
            ex,
            Activity.Current));
    }

    #endregion
}

