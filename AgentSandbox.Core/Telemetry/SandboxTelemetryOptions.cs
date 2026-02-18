namespace AgentSandbox.Core.Telemetry;

/// <summary>
/// Configuration options for sandbox telemetry.
/// </summary>
public class SandboxTelemetryOptions
{
    /// <summary>
    /// Enable telemetry collection. Default: false (opt-in).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Service instance identifier for distributed systems. 
    /// Default: machine name. Used to distinguish metrics from different instances.
    /// </summary>
    public string InstanceId { get; set; } = Environment.MachineName;

    /// <summary>
    /// Enable command execution tracing.
    /// </summary>
    public bool TraceCommands { get; set; } = true;

    /// <summary>
    /// Enable filesystem operation tracing.
    /// </summary>
    public bool TraceFileSystem { get; set; } = true;

    /// <summary>
    /// Enable metrics collection.
    /// </summary>
    public bool CollectMetrics { get; set; } = true;

    /// <summary>
    /// Enable structured logging.
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Minimum command duration to trace (filters noise). Default: Zero (trace all).
    /// </summary>
    public TimeSpan MinTraceDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Maximum length of command output to include in traces. Default: 1024.
    /// </summary>
    public int MaxOutputLength { get; set; } = 1024;

    /// <summary>
    /// Redact file contents from traces and logs. Default: true.
    /// </summary>
    public bool RedactFileContents { get; set; } = true;

    /// <summary>
    /// Optional host-provided correlation metadata attached to emitted lifecycle audit events.
    /// Defaults to an empty dictionary (never null). Use stable identifiers (e.g., tenantId, sessionId, requestId)
    /// and avoid sensitive values.
    /// </summary>
    public Dictionary<string, string> HostCorrelationMetadata { get; set; } = new(StringComparer.Ordinal);
}
