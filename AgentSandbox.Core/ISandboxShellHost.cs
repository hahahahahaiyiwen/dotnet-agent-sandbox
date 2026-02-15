using AgentSandbox.Core.Shell;

namespace AgentSandbox.Core;

/// <summary>
/// Minimal shell host abstraction exposed to capabilities.
/// </summary>
public interface ISandboxShellHost
{
    void RegisterCommand(IShellCommand command);
    IEnumerable<string> GetAvailableCommands();
}
