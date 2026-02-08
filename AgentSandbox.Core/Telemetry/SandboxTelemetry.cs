using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentSandbox.Core.Telemetry;

/// <summary>
/// Central telemetry instrumentation for AgentSandbox.
/// Provides ActivitySource for tracing and Meter for metrics.
/// </summary>
public static class SandboxTelemetry
{
    /// <summary>
    /// Service name used for telemetry identification.
    /// </summary>
    public const string ServiceName = "AgentSandbox";

    /// <summary>
    /// Version of the telemetry instrumentation.
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// ActivitySource for distributed tracing.
    /// Use this to create spans for sandbox operations.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName, Version);

    /// <summary>
    /// Meter for metrics collection.
    /// </summary>
    public static readonly Meter Meter = new(ServiceName, Version);

    #region Sandbox Metrics

    /// <summary>
    /// Number of active sandbox instances.
    /// </summary>
    public static readonly UpDownCounter<long> ActiveSandboxes = 
        Meter.CreateUpDownCounter<long>(
            "sandbox.active",
            unit: "{sandbox}",
            description: "Number of active sandbox instances");

    /// <summary>
    /// Total sandboxes created.
    /// </summary>
    public static readonly Counter<long> SandboxesCreated = 
        Meter.CreateCounter<long>(
            "sandbox.created.total",
            unit: "{sandbox}",
            description: "Total number of sandboxes created");

    /// <summary>
    /// Total sandboxes disposed.
    /// </summary>
    public static readonly Counter<long> SandboxesDisposed = 
        Meter.CreateCounter<long>(
            "sandbox.disposed.total",
            unit: "{sandbox}",
            description: "Total number of sandboxes disposed");

    #endregion

    #region Command Metrics

    /// <summary>
    /// Number of commands executed.
    /// </summary>
    public static readonly Counter<long> CommandsExecuted = 
        Meter.CreateCounter<long>(
            "sandbox.commands.executed",
            unit: "{command}",
            description: "Number of commands executed");

    /// <summary>
    /// Number of failed commands.
    /// </summary>
    public static readonly Counter<long> CommandsFailed = 
        Meter.CreateCounter<long>(
            "sandbox.commands.failed",
            unit: "{command}",
            description: "Number of commands with non-zero exit code");

    /// <summary>
    /// Command execution duration histogram.
    /// </summary>
    public static readonly Histogram<double> CommandDuration = 
        Meter.CreateHistogram<double>(
            "sandbox.commands.duration",
            unit: "ms",
            description: "Command execution duration in milliseconds");

    #endregion

    #region FileSystem Metrics

    /// <summary>
    /// Number of files created.
    /// Reserved for future use when file creation telemetry is integrated.
    /// </summary>
    public static readonly Counter<long> FilesCreated = 
        Meter.CreateCounter<long>(
            "sandbox.files.created",
            unit: "{file}",
            description: "Number of files created");

    /// <summary>
    /// Number of files deleted.
    /// Reserved for future use when file deletion telemetry is integrated.
    /// </summary>
    public static readonly Counter<long> FilesDeleted = 
        Meter.CreateCounter<long>(
            "sandbox.files.deleted",
            unit: "{file}",
            description: "Number of files deleted");

    /// <summary>
    /// Number of files modified.
    /// Reserved for future use when file modification telemetry is integrated.
    /// </summary>
    public static readonly Counter<long> FilesModified = 
        Meter.CreateCounter<long>(
            "sandbox.files.modified",
            unit: "{file}",
            description: "Number of files modified");

    /// <summary>
    /// Number of directories created.
    /// Reserved for future use when directory creation telemetry is integrated.
    /// </summary>
    public static readonly Counter<long> DirectoriesCreated = 
        Meter.CreateCounter<long>(
            "sandbox.directories.created",
            unit: "{directory}",
            description: "Number of directories created");

    /// <summary>
    /// Total bytes written to filesystem.
    /// Reserved for future use when detailed I/O telemetry is implemented.
    /// </summary>
    public static readonly Counter<long> BytesWritten = 
        Meter.CreateCounter<long>(
            "sandbox.bytes.written",
            unit: "By",
            description: "Total bytes written to filesystem");

    /// <summary>
    /// Total bytes read from filesystem.
    /// Reserved for future use when detailed I/O telemetry is implemented.
    /// </summary>
    public static readonly Counter<long> BytesRead = 
        Meter.CreateCounter<long>(
            "sandbox.bytes.read",
            unit: "By",
            description: "Total bytes read from filesystem");

    #endregion

    #region Skill Metrics

    /// <summary>
    /// Number of skills invoked.
    /// Reserved for future use when skill invocation telemetry is integrated.
    /// </summary>
    public static readonly Counter<long> SkillsInvoked = 
        Meter.CreateCounter<long>(
            "sandbox.skills.invoked",
            unit: "{invocation}",
            description: "Number of skill invocations");

    /// <summary>
    /// Number of shell scripts executed.
    /// Reserved for future use when script execution telemetry is integrated.
    /// </summary>
    public static readonly Counter<long> ScriptsExecuted = 
        Meter.CreateCounter<long>(
            "sandbox.scripts.executed",
            unit: "{script}",
            description: "Number of shell scripts executed");

    /// <summary>
    /// Number of failed script executions.
    /// Reserved for future use when script failure telemetry is integrated.
    /// </summary>
    public static readonly Counter<long> ScriptsFailed = 
        Meter.CreateCounter<long>(
            "sandbox.scripts.failed",
            unit: "{script}",
            description: "Number of failed shell script executions");

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a new activity/span for a sandbox lifecycle.
    /// Returns null if no listeners are registered.
    /// </summary>
    public static Activity? StartSandboxActivity(string sandboxId)
    {
        if (!ActivitySource.HasListeners())
            return null;

        var activity = ActivitySource.StartActivity("sandbox.lifecycle", ActivityKind.Internal);
        activity?.SetTag("sandbox.id", sandboxId);
        return activity;
    }

    /// <summary>
    /// Creates a new activity/span for a command execution (child of current activity).
    /// Returns null if no listeners are registered.
    /// </summary>
    public static Activity? StartCommandActivity(string command, string sandboxId)
    {
        if (!ActivitySource.HasListeners())
            return null;

        var commandName = GetCommandName(command);
        var activity = ActivitySource.StartActivity($"sandbox.command.{commandName}");
        
        if (activity != null)
        {
            activity.SetTag("sandbox.id", sandboxId);
            activity.SetTag("command.full", command);
            activity.SetTag("command.name", commandName);
        }

        return activity;
    }

    /// <summary>
    /// Extracts the command name (first word) from a command string.
    /// </summary>
    public static string GetCommandName(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "unknown";

        var trimmed = command.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        return spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;
    }

    #endregion
}
