using System.Text;

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

        var sb = new StringBuilder();
        foreach (var file in args)
        {
            var path = context.ResolvePath(file);
            if (!context.FileSystem.Exists(path))
                return ShellResult.Error($"cat: {file}: No such file or directory");
            if (context.FileSystem.IsDirectory(path))
                return ShellResult.Error($"cat: {file}: Is a directory");

            var bytes = context.FileSystem.ReadFileBytes(path);
            var content = Encoding.UTF8.GetString(bytes);
            sb.Append(content);
        }

        return ShellResult.Ok(sb.ToString());
    }
}
