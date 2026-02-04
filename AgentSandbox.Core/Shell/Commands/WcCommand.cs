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
            var content = context.FileSystem.ReadFile(path, Encoding.UTF8);
            var byteCount = Encoding.UTF8.GetByteCount(content);
            
            // Count lines and words without allocating arrays
            var lines = 0;
            var words = 0;
            var inWord = false;
            
            foreach (var c in content.AsSpan())
            {
                if (c == '\n') lines++;
                
                var isWhitespace = c == ' ' || c == '\n' || c == '\t' || c == '\r';
                if (isWhitespace)
                {
                    inWord = false;
                }
                else if (!inWord)
                {
                    inWord = true;
                    words++;
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
