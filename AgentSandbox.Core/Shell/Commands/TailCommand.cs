using System.Text;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Show last lines of file command.
/// </summary>
public class TailCommand : IShellCommand
{
    public string Name => "tail";
    public string Description => "Show last lines of file";
    public string Usage => """
        tail [-n N] <file>...

        Options:
          -n N    Show last N lines (default: 10)
        """;

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var maxLines = 10;
        var paths = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-n" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out maxLines))
                {
                    return ShellResult.Error("tail: invalid number of lines");
                }
            }
            else if (!args[i].StartsWith('-'))
            {
                paths.Add(args[i]);
            }
        }

        if (paths.Count == 0)
            return ShellResult.Error("tail: missing file operand");

        if (maxLines < 0)
            return ShellResult.Error("tail: invalid number of lines");

        if (maxLines == 0)
            return ShellResult.Ok(string.Empty);

        var output = new StringBuilder();
        foreach (var p in paths)
        {
            if (!ShellCommandFileGuards.TryResolveReadableFilePath(context, Name, p, out var path, out var errorMessage))
                return ShellResult.Error(errorMessage);
            
            // Use ring buffer to keep last N lines - avoids full string[] allocation
            var buffer = new string[maxLines];
            var bufferIndex = 0;
            var totalLines = 0;
            
            // Stream lines directly - no UTF-8 string materialization
            foreach (var line in context.FileSystem.ReadFileLines(path))
            {
                buffer[bufferIndex] = line;
                bufferIndex = (bufferIndex + 1) % maxLines;
                totalLines++;
            }
            
            // Output the lines in order
            var linesToOutput = Math.Min(totalLines, maxLines);
            var startIndex = totalLines <= maxLines ? 0 : bufferIndex;
            
            for (int i = 0; i < linesToOutput; i++)
            {
                var idx = (startIndex + i) % maxLines;
                if (i > 0) output.AppendLine();
                output.Append(buffer[idx]);
            }
            output.AppendLine();
        }

        return ShellResult.Ok(output.ToString().TrimEnd());
    }
}
