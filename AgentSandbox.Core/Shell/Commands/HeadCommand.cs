using System.Text;

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
                if (!int.TryParse(args[++i], out maxLines) || maxLines < 0)
                {
                    return ShellResult.Error("head: invalid number of lines");
                }
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
            if (!ShellCommandFileGuards.TryResolveReadableFilePath(context, Name, p, out var path, out var errorMessage))
                return ShellResult.Error(errorMessage);
            int? endLine = maxLines == int.MaxValue ? null : maxLines + 1;

            var count = 0;
            foreach (var line in context.FileSystem.ReadFileLines(path, endLine: endLine))
            {
                if (count > 0) output.AppendLine();
                output.Append(line);
                count++;
            }
            output.AppendLine();
        }

        return ShellResult.Ok(output.ToString().TrimEnd());
    }
}
