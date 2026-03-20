using System.Text;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Count lines, words, and bytes command.
/// </summary>
public class WcCommand : IShellCommand
{
    public string Name => "wc";
    public string Description => "Count lines, words, and bytes";
    public string Usage => "wc <file>...";

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var paths = args.Where(a => !a.StartsWith('-')).ToList();
        
        if (paths.Count == 0)
            return ShellResult.Error("wc: missing file operand");

        var output = new StringBuilder();
        long totalLines = 0, totalWords = 0, totalBytes = 0;

        foreach (var p in paths)
        {
            if (!ShellCommandFileGuards.TryResolveReadableFilePath(context, Name, p, out var path, out var errorMessage))
                return ShellResult.Error(errorMessage);

            var fileBytes = context.FileSystem.ReadFileBytes(path);
            var fileContent = Encoding.UTF8.GetString(fileBytes);
            var normalized = fileContent.Replace("\r\n", "\n").Replace("\r", "\n");
            var linesSpan = normalized.AsSpan();
            var lines = 0;
            var words = 0;
            var byteCount = fileBytes.Length;
            var inWord = false;

            var lineStart = 0;
            for (var i = 0; i < linesSpan.Length; i++)
            {
                if (linesSpan[i] != '\n')
                    continue;

                lines++;
                var line = linesSpan[lineStart..i];

                foreach (var c in line)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        inWord = false;
                    }
                    else if (!inWord)
                    {
                        inWord = true;
                        words++;
                    }
                }

                // Newline between lines is a word boundary.
                inWord = false;
                lineStart = i + 1;
            }

            if (lineStart < linesSpan.Length)
            {
                lines++;
                var line = linesSpan[lineStart..];
                foreach (var c in line)
                {
                    if (char.IsWhiteSpace(c))
                    {
                        inWord = false;
                    }
                    else if (!inWord)
                    {
                        inWord = true;
                        words++;
                    }
                }
            }
            
            output.AppendLine($"  {lines,6}  {words,6}  {byteCount,6} {p}");
            totalLines += lines;
            totalWords += words;
            totalBytes += byteCount;
        }

        if (paths.Count > 1)
        {
            output.AppendLine($"  {totalLines,6}  {totalWords,6}  {totalBytes,6} total");
        }

        return ShellResult.Ok(output.ToString().TrimEnd());
    }
}
