using AgentSandbox.Core;
using AgentSandbox.Core.Capabilities;
using AgentSandbox.Core.Shell;

namespace AgentSandbox.Extensions;

/// <summary>
/// Capability that registers one or more shell commands through SandboxOptions.
/// </summary>
public sealed class ShellCommandsCapability : ISandboxCapability
{
    private readonly IShellCommand[] _commands;
    public string Name => "shell-commands";

    public ShellCommandsCapability(IEnumerable<IShellCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        _commands = commands.ToArray();
    }

    public void Initialize(ISandboxContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var command in _commands)
        {
            context.Shell.RegisterCommand(command);
        }
    }
}
