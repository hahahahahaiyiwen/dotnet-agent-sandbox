using AgentSandbox.Core.Shell;

namespace AgentSandbox.Core;

/// <summary>
/// Minimal shell host abstraction exposed to capabilities.
/// </summary>
public interface ISandboxShellHost
{
    /// <summary>
    /// Registers a shell command so it can be invoked from sandbox bash execution.
    /// </summary>
    void RegisterCommand(IShellCommand command);

    /// <summary>
    /// Returns currently available shell command names, including capability-registered commands.
    /// </summary>
    IEnumerable<string> GetAvailableCommands();
}
