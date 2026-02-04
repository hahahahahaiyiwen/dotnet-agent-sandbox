using System.Text;
using AgentSandbox.Core.Shell;

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
                int.TryParse(args[++i], out maxLines);
            }
            else if (!args[i].StartsWith('-'))
            {
                paths.Add(args[i]);
            }
        }

        if (paths.Count == 0)
            return ShellResult.Error("tail: missing file operand");

        var output = new StringBuilder();
        foreach (var p in paths)
        {
            var path = context.ResolvePath(p);
            var content = context.FileSystem.ReadFile(path, Encoding.UTF8);
            
            // Use ring buffer to keep last N lines - avoids full string[] allocation
            var buffer = new (int Start, int Length)[maxLines];
            var bufferIndex = 0;
            var totalLines = 0;
            
            var span = content.AsSpan();
            var start = 0;
            
            for (int i = 0; i <= span.Length; i++)
            {
                if (i == span.Length || span[i] == '\n')
                {
                    var lineEnd = i;
                    // Handle \r\n
                    if (lineEnd > start && lineEnd > 0 && span[lineEnd - 1] == '\r')
                        lineEnd--;
                    
                    buffer[bufferIndex] = (start, lineEnd - start);
                    bufferIndex = (bufferIndex + 1) % maxLines;
                    totalLines++;
                    start = i + 1;
                }
            }
            
            // Output the lines in order
            var linesToOutput = Math.Min(totalLines, maxLines);
            var startIndex = totalLines <= maxLines ? 0 : bufferIndex;
            
            for (int i = 0; i < linesToOutput; i++)
            {
                var idx = (startIndex + i) % maxLines;
                var (lineStart, length) = buffer[idx];
                if (i > 0) output.AppendLine();
                output.Append(content.AsSpan(lineStart, length));
            }
            output.AppendLine();
        }

        return ShellResult.Ok(output.ToString().TrimEnd());
    }
}
