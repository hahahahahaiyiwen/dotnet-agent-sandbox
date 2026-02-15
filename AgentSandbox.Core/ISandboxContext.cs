using AgentSandbox.Core.FileSystem;

namespace AgentSandbox.Core;

/// <summary>
/// Stable runtime context provided to sandbox capabilities during initialization.
/// </summary>
public interface ISandboxContext
{
    SandboxOptions Options { get; }
    IFileSystem FileSystem { get; }
    ISandboxShellHost Shell { get; }
    ISandboxTelemetry Telemetry { get; }
    IServiceProvider? Services { get; }

    TCapability GetCapability<TCapability>() where TCapability : class;
    bool TryGetCapability<TCapability>(out TCapability? capability) where TCapability : class;
}
