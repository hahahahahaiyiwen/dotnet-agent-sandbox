using AgentSandbox.Core.Importing;
using AgentSandbox.Core.Capabilities;
using AgentSandbox.Core.Security;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Skills;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Core.Metadata;

namespace AgentSandbox.Core;

/// <summary>
/// Configuration options for a sandbox instance.
/// </summary>
public class SandboxOptions
{
    private int _maxCommandLength = 8 * 1024;
    private int _maxWritePayloadBytes = 100 * 1024;

    /// <summary>Maximum total size of all files in bytes (default: 5MB).</summary>
    public long MaxTotalSize { get; set; } = 5 * 1024 * 1024;
    
    /// <summary>Maximum size of a single file in bytes (default: 100KB).</summary>
    public long MaxFileSize { get; set; } = 100 * 1024;
    
    /// <summary>Maximum number of files/directories (default: 1000).</summary>
    public int MaxNodeCount { get; set; } = 1000;
    
    /// <summary>Command execution timeout (default: 30 seconds).</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum UTF-8 payload size for Execute command input in bytes (default: 8KB).</summary>
    public int MaxCommandLength
    {
        get => _maxCommandLength;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxCommandLength), value, "MaxCommandLength must be a positive value.");
            }

            _maxCommandLength = value;
        }
    }

    /// <summary>Maximum UTF-8 payload size for WriteFile content input in bytes (default: 100KB).</summary>
    public int MaxWritePayloadBytes
    {
        get => _maxWritePayloadBytes;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxWritePayloadBytes), value, "MaxWritePayloadBytes must be a positive value.");
            }

            _maxWritePayloadBytes = value;
        }
    }
    
    /// <summary>Initial environment variables.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
    
    /// <summary>Initial working directory.</summary>
    public string WorkingDirectory { get; set; } = "/";

    /// <summary>Shell command extensions to register.</summary>
    public IEnumerable<IShellCommand> ShellExtensions { get; set; } = Array.Empty<IShellCommand>();

    /// <summary>Sandbox capabilities to initialize during sandbox construction (after ShellExtensions registration, before file imports).</summary>
    public IReadOnlyList<ISandboxCapability> Capabilities { get; set; } = [];

    /// <summary>
    /// Optional host-provided services for capability initialization and runtime integration.
    /// </summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// Files to import into the sandbox filesystem at initialization.
    /// Each import specifies a destination path and file source.
    /// </summary>
    public IReadOnlyList<FileImportOptions> Imports { get; set; } = [];

    /// <summary>
    /// Agent skills configuration. Skills are discovered from the BasePath after file imports.
    /// Skills should be imported to subdirectories under BasePath via FileImportOptions.
    /// </summary>
    public AgentSkillOptions AgentSkills { get; set; } = new();

    /// <summary>
    /// Telemetry configuration. Default: disabled (opt-in).
    /// </summary>
    public SandboxTelemetryOptions? Telemetry { get; set; }

    /// <summary>
    /// Host-managed secret broker for resolving secret references at execution time.
    /// </summary>
    /// <remarks>
    /// Secrets are resolved just-in-time by shell commands and should never be logged or persisted by broker implementations.
    /// Keep resolved secret lifetime short and scope broker access to the minimum needed for the sandbox session.
    /// </remarks>
    public ISecretBroker? SecretBroker { get; set; }

    /// <summary>
    /// Optional secret policy model used to constrain command-level secret usage.
    /// </summary>
    public SecretResolutionPolicy? SecretPolicy { get; set; }

    /// <summary>
    /// Metadata journal retention options.
    /// </summary>
    public SandboxOperationJournalOptions Journal { get; set; } = new();

    /// <summary>
    /// Creates a shallow copy of this options instance.
    /// Capability instances are reused across clones; they should be stateless or re-initialization safe.
    /// </summary>
    public SandboxOptions Clone() => new()
    {
        MaxTotalSize = MaxTotalSize,
        MaxFileSize = MaxFileSize,
        MaxNodeCount = MaxNodeCount,
        CommandTimeout = CommandTimeout,
        MaxCommandLength = MaxCommandLength,
        MaxWritePayloadBytes = MaxWritePayloadBytes,
        Environment = new Dictionary<string, string>(Environment),
        WorkingDirectory = WorkingDirectory,
        ShellExtensions = ShellExtensions.ToArray(),
        Capabilities = Capabilities.ToArray(),
        Imports = Imports.ToArray(),
        AgentSkills = new AgentSkillOptions
        {
            BasePath = AgentSkills.BasePath
        },
        Telemetry = Telemetry is null
            ? null
            : new SandboxTelemetryOptions
            {
                Enabled = Telemetry.Enabled,
                InstanceId = Telemetry.InstanceId,
                TraceCommands = Telemetry.TraceCommands,
                TraceFileSystem = Telemetry.TraceFileSystem,
                CollectMetrics = Telemetry.CollectMetrics,
                EnableLogging = Telemetry.EnableLogging,
                MinTraceDuration = Telemetry.MinTraceDuration,
                MaxOutputLength = Telemetry.MaxOutputLength,
                RedactFileContents = Telemetry.RedactFileContents,
                HostCorrelationMetadata = new Dictionary<string, string>(Telemetry.HostCorrelationMetadata, StringComparer.Ordinal)
            },
        SecretBroker = SecretBroker,
        SecretPolicy = SecretPolicy is null
            ? null
            : new SecretResolutionPolicy
            {
                AllowedRefs = SecretPolicy.AllowedRefs is null
                    ? null
                    : new HashSet<string>(SecretPolicy.AllowedRefs, StringComparer.Ordinal),
                MaxSecretAge = SecretPolicy.MaxSecretAge,
                EgressHostAllowlistHook = SecretPolicy.EgressHostAllowlistHook
            },
        Journal = new SandboxOperationJournalOptions
        {
            MaxEntries = Journal.MaxEntries,
            TruncationStrategy = Journal.TruncationStrategy
        },
        Services = Services
    };
}
