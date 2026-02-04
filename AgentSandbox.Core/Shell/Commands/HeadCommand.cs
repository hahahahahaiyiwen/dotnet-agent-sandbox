using System.Text;
using AgentSandbox.Core.Shell;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Show first lines of file command.
/// </summary>
public class HeadCommand : IShellCommand
{
    public string Name => "head";
    public string Description => "Show first lines of file";
    public string Usage => """
        head [-n N] <file>...

        Options:
          -n N    Show first N lines (default: 10)
        """;

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var maxLines = 10;
        var paths = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-n" && i + 1 < args.Length)
            {
                int.TryParse(args[++i], out maxLines);
            }
            else if (!args[i].StartsWith('-'))
            {
                paths.Add(args[i]);
            }
        }

        if (paths.Count == 0)
            return ShellResult.Error("head: missing file operand");

        var output = new StringBuilder();
        foreach (var p in paths)
        {
            var path = context.ResolvePath(p);
            var content = context.FileSystem.ReadFile(path, Encoding.UTF8);
            
            var count = 0;
            foreach (var (_, line) in content.EnumerateLines())
            {
                if (count >= maxLines) break;
                if (count > 0) output.AppendLine();
                output.Append(line);
                count++;
            }
            output.AppendLine();
        }

        return ShellResult.Ok(output.ToString().TrimEnd());
    }
}
