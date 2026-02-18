using AgentSandbox.Core;
using AgentSandbox.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace AgentSandbox.Extensions.Observability;

/// <summary>
/// ISandboxObserver implementation that logs events using ILogger.
/// Provides structured logging with semantic properties for easy querying.
/// </summary>
public class LoggingSandboxObserver : ISandboxObserver
{
    private readonly ILogger _logger;
    private readonly LoggingObserverOptions _options;

    /// <summary>
    /// Creates a new LoggingSandboxObserver.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">Optional configuration options.</param>
    public LoggingSandboxObserver(ILogger logger, LoggingObserverOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new LoggingObserverOptions();
    }

    /// <inheritdoc />
    public void OnCommandExecuted(CommandExecutedEvent e)
    {
        if (!_options.LogCommands) return;

        if (e.ExitCode == 0)
        {
            _logger.LogInformation(
                "Command executed: {CommandName} in {Duration:F1}ms [SandboxId={SandboxId}, ExitCode={ExitCode}, Cwd={WorkingDirectory}]",
                e.CommandName,
                e.Duration.TotalMilliseconds,
                e.SandboxId,
                e.ExitCode,
                e.WorkingDirectory ?? "/");
        }
        else
        {
            _logger.LogWarning(
                "Command failed: {CommandName} with exit code {ExitCode} in {Duration:F1}ms [SandboxId={SandboxId}, Error={Error}]",
                e.CommandName,
                e.ExitCode,
                e.Duration.TotalMilliseconds,
                e.SandboxId,
                TruncateString(e.Error, _options.MaxMessageLength));
        }
    }

    /// <inheritdoc />
    public void OnFileChanged(FileChangedEvent e)
    {
        if (!_options.LogFileChanges) return;

        _logger.LogDebug(
            "File {ChangeType}: {Path} [SandboxId={SandboxId}, IsDirectory={IsDirectory}, Bytes={Bytes}]",
            e.ChangeType,
            e.Path,
            e.SandboxId,
            e.IsDirectory,
            e.Bytes);
    }

    /// <inheritdoc />
    public void OnSkillInvoked(SkillInvokedEvent e)
    {
        if (!_options.LogSkills) return;

        if (e.Success == true)
        {
            _logger.LogInformation(
                "Skill invoked: {SkillName} [SandboxId={SandboxId}, ScriptPath={ScriptPath}, Duration={Duration:F1}ms]",
                e.SkillName,
                e.SandboxId,
                e.ScriptPath,
                e.Duration?.TotalMilliseconds);
        }
        else if (e.Success == false)
        {
            _logger.LogWarning(
                "Skill failed: {SkillName} [SandboxId={SandboxId}, ScriptPath={ScriptPath}]",
                e.SkillName,
                e.SandboxId,
                e.ScriptPath);
        }
        else
        {
            _logger.LogInformation(
                "Skill invoked: {SkillName} [SandboxId={SandboxId}]",
                e.SkillName,
                e.SandboxId);
        }
    }

    /// <inheritdoc />
    public void OnLifecycleEvent(SandboxLifecycleEvent e)
    {
        if (!_options.LogLifecycle) return;

        var level = e.LifecycleType switch
        {
            SandboxLifecycleType.Created => LogLevel.Information,
            SandboxLifecycleType.Executed => LogLevel.Debug,
            SandboxLifecycleType.Disposed => LogLevel.Information,
            SandboxLifecycleType.SnapshotCreated => LogLevel.Debug,
            SandboxLifecycleType.SnapshotRestored => LogLevel.Debug,
            _ => LogLevel.Debug
        };

        _logger.Log(
            level,
            "Sandbox {LifecycleType} [SandboxId={SandboxId}, Details={Details}, Correlation={Correlation}]",
            e.LifecycleType,
            e.SandboxId,
            e.Details,
            FormatCorrelationMetadata(e.HostCorrelationMetadata));
    }

    /// <inheritdoc />
    public void OnError(SandboxErrorEvent e)
    {
        _logger.LogError(
            "Sandbox error in {Category}: {Message} [SandboxId={SandboxId}, ExceptionType={ExceptionType}]",
            e.Category,
            e.Message,
            e.SandboxId,
            e.ExceptionType);
    }

    private static string? TruncateString(string? value, int maxLength)
    {
        if (value == null || value.Length <= maxLength)
            return value;
        return value[..maxLength] + "...";
    }

    private static string? FormatCorrelationMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return null;

        return string.Join(", ", metadata.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => $"{kvp.Key}={kvp.Value}"));
    }
}

/// <summary>
/// Configuration options for LoggingSandboxObserver.
/// </summary>
public class LoggingObserverOptions
{
    /// <summary>
    /// Log command execution events. Default: true.
    /// </summary>
    public bool LogCommands { get; set; } = true;

    /// <summary>
    /// Log file change events. Default: false (can be noisy at Debug level).
    /// </summary>
    public bool LogFileChanges { get; set; } = false;

    /// <summary>
    /// Log skill invocation events. Default: true.
    /// </summary>
    public bool LogSkills { get; set; } = true;

    /// <summary>
    /// Log lifecycle events (created, disposed). Default: true.
    /// </summary>
    public bool LogLifecycle { get; set; } = true;

    /// <summary>
    /// Maximum length of error/output messages in logs. Default: 500.
    /// </summary>
    public int MaxMessageLength { get; set; } = 500;
}
