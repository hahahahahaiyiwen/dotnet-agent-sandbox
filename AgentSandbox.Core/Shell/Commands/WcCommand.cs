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
            var path = context.ResolvePath(p);
            var fileBytes = context.FileSystem.ReadFileBytes(path);
            var lines = 0;
            var words = 0;
            var byteCount = fileBytes.Length;
            var inWord = false;
            
            foreach (var line in context.FileSystem.ReadFileLines(path))
            {
                lines++;

                foreach (var c in line.AsSpan())
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
