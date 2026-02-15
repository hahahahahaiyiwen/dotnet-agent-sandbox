using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Telemetry;

namespace AgentSandbox.Core.Capabilities;

/// <summary>
/// Stable runtime context provided to sandbox capabilities during initialization.
/// </summary>
public interface ISandboxContext
{
    string SandboxId { get; }
    SandboxOptions Options { get; }
    IFileSystem FileSystem { get; }
    ISandboxShellHost Shell { get; }
    ISandboxEventEmitter EventEmitter { get; }
    IServiceProvider? Services { get; }

    TCapability GetCapability<TCapability>() where TCapability : class;
    bool TryGetCapability<TCapability>(out TCapability? capability) where TCapability : class;
}
