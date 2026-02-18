using AgentSandbox.Core;
using AgentSandbox.Core.Telemetry;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace AgentSandbox.Extensions.Observability;

/// <summary>
/// ISandboxObserver implementation that sends telemetry to Application Insights.
/// </summary>
public class ApplicationInsightsObserver : ISandboxObserver
{
    private readonly TelemetryClient _telemetryClient;
    private readonly ApplicationInsightsObserverOptions _options;
    private const int MaxCorrelationProperties = 20;
    private const int MaxCorrelationKeyLength = 100;
    private const int MaxCorrelationValueLength = 500;

    /// <summary>
    /// Creates a new ApplicationInsightsObserver.
    /// </summary>
    /// <param name="telemetryClient">The Application Insights TelemetryClient.</param>
    /// <param name="options">Optional configuration options.</param>
    public ApplicationInsightsObserver(
        TelemetryClient telemetryClient, 
        ApplicationInsightsObserverOptions? options = null)
    {
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        _options = options ?? new ApplicationInsightsObserverOptions();
    }

    /// <inheritdoc />
    public void OnCommandExecuted(CommandExecutedEvent e)
    {
        if (!_options.TrackCommands) return;

        var telemetry = new EventTelemetry("SandboxCommandExecuted")
        {
            Timestamp = e.Timestamp
        };

        telemetry.Properties["SandboxId"] = e.SandboxId;
        telemetry.Properties["Command"] = _options.RedactCommands ? e.CommandName : e.Command;
        telemetry.Properties["CommandName"] = e.CommandName;
        telemetry.Properties["ExitCode"] = e.ExitCode.ToString();
        telemetry.Properties["WorkingDirectory"] = e.WorkingDirectory ?? "/";
        
        if (!string.IsNullOrEmpty(e.TraceId))
            telemetry.Properties["TraceId"] = e.TraceId;

        telemetry.Metrics["DurationMs"] = e.Duration.TotalMilliseconds;

        if (_options.IncludeOutput && !string.IsNullOrEmpty(e.Output))
        {
            telemetry.Properties["Output"] = TruncateString(e.Output, _options.MaxOutputLength);
        }

        if (!string.IsNullOrEmpty(e.Error))
        {
            telemetry.Properties["Error"] = TruncateString(e.Error, _options.MaxOutputLength);
        }

        _telemetryClient.TrackEvent(telemetry);

        // Also track as dependency for duration visualization
        if (_options.TrackAsDependency)
        {
            var dependency = new DependencyTelemetry
            {
                Name = $"sandbox.{e.CommandName}",
                Type = "Sandbox",
                Target = e.SandboxId,
                Data = _options.RedactCommands ? e.CommandName : e.Command,
                Duration = e.Duration,
                Success = e.ExitCode == 0,
                Timestamp = e.Timestamp
            };

            if (!string.IsNullOrEmpty(e.TraceId))
                dependency.Context.Operation.Id = e.TraceId;

            _telemetryClient.TrackDependency(dependency);
        }
    }

    /// <inheritdoc />
    public void OnFileChanged(FileChangedEvent e)
    {
        if (!_options.TrackFileChanges) return;

        var telemetry = new EventTelemetry("SandboxFileChanged")
        {
            Timestamp = e.Timestamp
        };

        telemetry.Properties["SandboxId"] = e.SandboxId;
        telemetry.Properties["Path"] = e.Path;
        telemetry.Properties["ChangeType"] = e.ChangeType.ToString();
        telemetry.Properties["IsDirectory"] = e.IsDirectory.ToString();

        if (!string.IsNullOrEmpty(e.OldPath))
            telemetry.Properties["OldPath"] = e.OldPath;

        if (e.Bytes.HasValue)
            telemetry.Metrics["Bytes"] = e.Bytes.Value;

        if (!string.IsNullOrEmpty(e.TraceId))
            telemetry.Properties["TraceId"] = e.TraceId;

        _telemetryClient.TrackEvent(telemetry);
    }

    /// <inheritdoc />
    public void OnSkillInvoked(SkillInvokedEvent e)
    {
        if (!_options.TrackSkills) return;

        var telemetry = new EventTelemetry("SandboxSkillInvoked")
        {
            Timestamp = e.Timestamp
        };

        telemetry.Properties["SandboxId"] = e.SandboxId;
        telemetry.Properties["SkillName"] = e.SkillName;

        if (!string.IsNullOrEmpty(e.ScriptPath))
            telemetry.Properties["ScriptPath"] = e.ScriptPath;

        if (e.Duration.HasValue)
            telemetry.Metrics["DurationMs"] = e.Duration.Value.TotalMilliseconds;

        if (e.Success.HasValue)
            telemetry.Properties["Success"] = e.Success.Value.ToString();

        if (!string.IsNullOrEmpty(e.TraceId))
            telemetry.Properties["TraceId"] = e.TraceId;

        _telemetryClient.TrackEvent(telemetry);
    }

    /// <inheritdoc />
    public void OnLifecycleEvent(SandboxLifecycleEvent e)
    {
        if (!_options.TrackLifecycle) return;

        var telemetry = new EventTelemetry($"Sandbox{e.LifecycleType}")
        {
            Timestamp = e.Timestamp
        };

        telemetry.Properties["SandboxId"] = e.SandboxId;
        telemetry.Properties["LifecycleType"] = e.LifecycleType.ToString();

        if (!string.IsNullOrEmpty(e.Details))
            telemetry.Properties["Details"] = e.Details;

        AddCorrelationMetadata(telemetry, e.HostCorrelationMetadata);

        if (!string.IsNullOrEmpty(e.TraceId))
            telemetry.Properties["TraceId"] = e.TraceId;

        _telemetryClient.TrackEvent(telemetry);
    }

    /// <inheritdoc />
    public void OnError(SandboxErrorEvent e)
    {
        var telemetry = new ExceptionTelemetry
        {
            Message = e.Message,
            Timestamp = e.Timestamp,
            SeverityLevel = SeverityLevel.Error
        };

        telemetry.Properties["SandboxId"] = e.SandboxId;
        telemetry.Properties["Category"] = e.Category;

        if (!string.IsNullOrEmpty(e.ExceptionType))
            telemetry.Properties["ExceptionType"] = e.ExceptionType;

        if (!string.IsNullOrEmpty(e.TraceId))
            telemetry.Context.Operation.Id = e.TraceId;

        _telemetryClient.TrackException(telemetry);
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..maxLength] + "...";
    }

    private static void AddCorrelationMetadata(EventTelemetry telemetry, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return;
        }

        foreach (var pair in metadata.Take(MaxCorrelationProperties))
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var key = TruncateString(pair.Key, MaxCorrelationKeyLength);
            var value = TruncateString(pair.Value ?? string.Empty, MaxCorrelationValueLength);
            telemetry.Properties[$"Correlation.{key}"] = value;
        }

        if (metadata.Count > MaxCorrelationProperties)
        {
            telemetry.Properties["Correlation.TruncatedCount"] = (metadata.Count - MaxCorrelationProperties).ToString();
        }
    }
}

/// <summary>
/// Configuration options for ApplicationInsightsObserver.
/// </summary>
public class ApplicationInsightsObserverOptions
{
    /// <summary>
    /// Track command execution events. Default: true.
    /// </summary>
    public bool TrackCommands { get; set; } = true;

    /// <summary>
    /// Track file change events. Default: false (can be noisy).
    /// </summary>
    public bool TrackFileChanges { get; set; } = false;

    /// <summary>
    /// Track skill invocation events. Default: true.
    /// </summary>
    public bool TrackSkills { get; set; } = true;

    /// <summary>
    /// Track sandbox lifecycle events (created/disposed). Default: true.
    /// </summary>
    public bool TrackLifecycle { get; set; } = true;

    /// <summary>
    /// Also track commands as dependencies for duration visualization. Default: true.
    /// </summary>
    public bool TrackAsDependency { get; set; } = true;

    /// <summary>
    /// Include command output in telemetry. Default: false.
    /// </summary>
    public bool IncludeOutput { get; set; } = false;

    /// <summary>
    /// Redact full command strings, only include command name. Default: false.
    /// </summary>
    public bool RedactCommands { get; set; } = false;

    /// <summary>
    /// Maximum length of output to include. Default: 1024.
    /// </summary>
    public int MaxOutputLength { get; set; } = 1024;
}
