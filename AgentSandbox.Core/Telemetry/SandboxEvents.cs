namespace AgentSandbox.Core.Telemetry;

/// <summary>
/// Base class for all sandbox events.
/// </summary>
public abstract record SandboxEvent
{
    /// <summary>
    /// Timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The sandbox ID where the event occurred.
    /// </summary>
    public required string SandboxId { get; init; }

    /// <summary>
    /// Optional trace ID for correlation with distributed tracing.
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Optional span ID for correlation with distributed tracing.
    /// </summary>
    public string? SpanId { get; init; }
}

/// <summary>
/// Event raised when a command is executed.
/// </summary>
public record CommandExecutedEvent : SandboxEvent
{
    /// <summary>
    /// The full command string that was executed.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// The command name (first word of the command).
    /// </summary>
    public required string CommandName { get; init; }

    /// <summary>
    /// Exit code of the command.
    /// </summary>
    public required int ExitCode { get; init; }

    /// <summary>
    /// Duration of command execution.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Standard output (may be truncated).
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Standard error output.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Current working directory when command was executed.
    /// </summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// Type of filesystem change.
/// </summary>
public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}

/// <summary>
/// Event raised when a filesystem change occurs.
/// </summary>
public record FileChangedEvent : SandboxEvent
{
    /// <summary>
    /// Path of the affected file or directory.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Type of change that occurred.
    /// </summary>
    public required FileChangeType ChangeType { get; init; }

    /// <summary>
    /// Whether the path is a directory.
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// Original path (for rename operations).
    /// </summary>
    public string? OldPath { get; init; }

    /// <summary>
    /// Size in bytes (for created/modified files).
    /// </summary>
    public long? Bytes { get; init; }
}

/// <summary>
/// Event raised when a skill is invoked.
/// </summary>
public record SkillInvokedEvent : SandboxEvent
{
    /// <summary>
    /// Name of the skill that was invoked.
    /// </summary>
    public required string SkillName { get; init; }

    /// <summary>
    /// Path to the script being executed (if applicable).
    /// </summary>
    public string? ScriptPath { get; init; }

    /// <summary>
    /// Duration of skill execution.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Whether the skill execution succeeded.
    /// </summary>
    public bool? Success { get; init; }
}

/// <summary>
/// Event raised when a sandbox lifecycle event occurs.
/// </summary>
public record SandboxLifecycleEvent : SandboxEvent
{
    /// <summary>
    /// Type of lifecycle event.
    /// </summary>
    public required SandboxLifecycleType LifecycleType { get; init; }

    /// <summary>
    /// Additional details about the lifecycle event.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Optional host-provided correlation metadata for audit and compliance workflows.
    /// </summary>
    public IReadOnlyDictionary<string, string>? HostCorrelationMetadata { get; init; }
}

/// <summary>
/// Type of sandbox lifecycle event.
/// </summary>
public enum SandboxLifecycleType
{
    Created,
    Executed,
    Disposed,
    SnapshotCreated,
    SnapshotRestored
}

/// <summary>
/// Event raised when an error occurs in the sandbox.
/// </summary>
public record SandboxErrorEvent : SandboxEvent
{
    /// <summary>
    /// Category of the error (Command, FileSystem, Skill, etc.).
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Exception type name if applicable.
    /// </summary>
    public string? ExceptionType { get; init; }

    /// <summary>
    /// Stack trace if applicable and enabled.
    /// </summary>
    public string? StackTrace { get; init; }
}
