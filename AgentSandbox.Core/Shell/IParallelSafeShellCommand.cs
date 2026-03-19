namespace AgentSandbox.Core.Shell;

/// <summary>
/// Marker interface for shell extensions that are safe to execute in isolated parallel command mode.
/// </summary>
public interface IParallelSafeShellCommand : IShellCommand
{
}
