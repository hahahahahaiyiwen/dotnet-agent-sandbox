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
            var path = context.ResolvePath(file);
            if (!context.FileSystem.Exists(path))
                return new ShellResult
                {
                    ExitCode = 1,
                    Stdout = string.Join("\n", parts),
                    Stderr = $"cat: {file}: No such file or directory"
                };
            if (context.FileSystem.IsDirectory(path))
                return new ShellResult
                {
                    ExitCode = 1,
                    Stdout = string.Join("\n", parts),
                    Stderr = $"cat: {file}: Is a directory"
                };

            // Use ReadFile() - IFileSystem handles UTF-8 decoding, no manual conversion needed
            parts.Add(context.FileSystem.ReadFile(path));
        }

        return ShellResult.Ok(string.Join("\n", parts));
    }
}
