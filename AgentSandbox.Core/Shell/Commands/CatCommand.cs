namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Display file contents command.
/// </summary>
public class CatCommand : IShellCommand
{
    public string Name => "cat";
    public string Description => "Display file contents";
    public string Usage => "cat <file>...";

    public ShellResult Execute(string[] args, IShellContext context)
    {
        if (args.Length == 0)
            return ShellResult.Error("cat: missing operand");

        var parts = new List<string>();
        foreach (var file in args)
        {
            if (!ShellCommandFileGuards.TryResolveReadableFilePath(context, Name, file, out var path, out var errorMessage))
                return MultiTargetCommandFailurePolicy.FailFast(
                    errorMessage,
                    args.Length,
                    () => string.Join("\n", parts));

            // Use ReadFile() - IFileSystem handles UTF-8 decoding, no manual conversion needed
            parts.Add(context.FileSystem.ReadFile(path));
        }

        return ShellResult.Ok(string.Join("\n", parts));
    }
}
