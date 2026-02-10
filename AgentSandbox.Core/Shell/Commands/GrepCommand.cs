using System.Text;
using System.Text.RegularExpressions;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Search for pattern in files command.
/// </summary>
public class GrepCommand : IShellCommand
{
    public string Name => "grep";
    public string Description => "Search for pattern in files";
    public string Usage => """
        grep [-cilnovrw] [-A N] [-B N] [-C N] [-m N] <pattern> <file|dir>...

        Options:
          -i        Case insensitive search
          -n        Show line numbers
          -r        Search directories recursively
          -l        Print only filenames with matches
          -c        Print only count of matching lines per file
          -v        Invert match (select non-matching lines)
          -w        Match whole words only
          -o        Print only the matched parts
          -m N      Stop after N matches per file
          -A N      Print N lines after match
          -B N      Print N lines before match
          -C N      Print N lines before and after match
        """;

    public ShellResult Execute(string[] args, IShellContext context)
    {
        // Parse options
        var options = new GrepOptions();
        var nonFlagArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            
            if (arg == "-i") options.IgnoreCase = true;
            else if (arg == "-n") options.ShowLineNumbers = true;
            else if (arg == "-r" || arg == "-R") options.Recursive = true;
            else if (arg == "-l") options.FilesOnly = true;
            else if (arg == "-c") options.CountOnly = true;
            else if (arg == "-v") options.InvertMatch = true;
            else if (arg == "-w") options.WordMatch = true;
            else if (arg == "-o") options.OnlyMatching = true;
            else if (arg == "-A" && i + 1 < args.Length && int.TryParse(args[i + 1], out var afterCtx))
            {
                options.AfterContext = afterCtx;
                i++;
            }
            else if (arg == "-B" && i + 1 < args.Length && int.TryParse(args[i + 1], out var beforeCtx))
            {
                options.BeforeContext = beforeCtx;
                i++;
            }
            else if (arg == "-C" && i + 1 < args.Length && int.TryParse(args[i + 1], out var ctx))
            {
                options.BeforeContext = ctx;
                options.AfterContext = ctx;
                i++;
            }
            else if (arg == "-m" && i + 1 < args.Length && int.TryParse(args[i + 1], out var maxCount))
            {
                options.MaxCount = maxCount;
                i++;
            }
            else if (arg.StartsWith("-") && !arg.StartsWith("--"))
            {
                // Handle combined flags like -rn, -inv, etc.
                foreach (var c in arg.Skip(1))
                {
                    switch (c)
                    {
                        case 'i': options.IgnoreCase = true; break;
                        case 'n': options.ShowLineNumbers = true; break;
                        case 'r': case 'R': options.Recursive = true; break;
                        case 'l': options.FilesOnly = true; break;
                        case 'c': options.CountOnly = true; break;
                        case 'v': options.InvertMatch = true; break;
                        case 'w': options.WordMatch = true; break;
                        case 'o': options.OnlyMatching = true; break;
                    }
                }
            }
            else
            {
                nonFlagArgs.Add(arg);
            }
        }

        if (nonFlagArgs.Count < 2)
            return ShellResult.Error("grep: missing pattern or file");

        var pattern = nonFlagArgs[0];
        var inputPaths = nonFlagArgs.Skip(1).ToList();

        // Wrap pattern for word matching
        if (options.WordMatch)
            pattern = $@"\b{pattern}\b";

        // Collect files
        var filePaths = new List<(string DisplayPath, string FullPath)>();
        foreach (var p in inputPaths)
        {
            var path = context.ResolvePath(p);
            if (context.FileSystem.IsDirectory(path))
            {
                if (options.Recursive)
                {
                    CollectFilesRecursive(context, path, p, filePaths);
                }
                else
                {
                    return ShellResult.Error($"grep: {p}: Is a directory");
                }
            }
            else
            {
                filePaths.Add((p, path));
            }
        }

        var output = new StringBuilder();
        var regexOptions = options.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
        var regex = context.GetOrCreate(
            (pattern, regexOptions),
            () => new Regex(pattern, regexOptions | RegexOptions.Compiled));
        var showPrefix = filePaths.Count > 1;
        var hasContext = options.BeforeContext > 0 || options.AfterContext > 0;

        foreach (var (displayPath, fullPath) in filePaths)
        {
            try
            {
                var bytes = context.FileSystem.ReadFileBytes(fullPath);
                var content = Encoding.UTF8.GetString(bytes);
                
                if (options.FilesOnly)
                {
                    // -l: just check if any line matches
                    foreach (var (_, line) in content.EnumerateLines())
                    {
                        var matches = regex.IsMatch(line);
                        if (options.InvertMatch) matches = !matches;
                        if (matches)
                        {
                            output.AppendLine(displayPath);
                            break;
                        }
                    }
                }
                else if (options.CountOnly)
                {
                    // -c: count matching lines
                    var count = 0;
                    foreach (var (_, line) in content.EnumerateLines())
                    {
                        var matches = regex.IsMatch(line);
                        if (options.InvertMatch) matches = !matches;
                        if (matches) count++;
                        if (options.MaxCount > 0 && count >= options.MaxCount) break;
                    }
                    if (showPrefix)
                        output.AppendLine($"{displayPath}:{count}");
                    else
                        output.AppendLine(count.ToString());
                }
                else if (hasContext)
                {
                    // Context mode: need to buffer lines
                    SearchWithContext(content, regex, options, displayPath, showPrefix, output);
                }
                else
                {
                    // Normal search
                    var matchCount = 0;
                    foreach (var (lineNum, line) in content.EnumerateLines())
                    {
                        if (options.MaxCount > 0 && matchCount >= options.MaxCount) break;
                        
                        var matches = regex.IsMatch(line);
                        if (options.InvertMatch) matches = !matches;
                        
                        if (matches)
                        {
                            matchCount++;
                            var prefix = showPrefix ? $"{displayPath}:" : "";
                            var lineNumStr = options.ShowLineNumbers ? $"{lineNum}:" : "";
                            
                            if (options.OnlyMatching && !options.InvertMatch)
                            {
                                // -o: print only matched parts
                                foreach (Match m in regex.Matches(line.ToString()))
                                {
                                    output.Append(prefix);
                                    output.Append(lineNumStr);
                                    output.AppendLine(m.Value);
                                }
                            }
                            else
                            {
                                output.Append(prefix);
                                output.Append(lineNumStr);
                                output.Append(line);
                                output.AppendLine();
                            }
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {
                return ShellResult.Error($"grep: {displayPath}: No such file or directory");
            }
        }

        return ShellResult.Ok(output.ToString().TrimEnd());
    }

    private static void SearchWithContext(
        string content,
        Regex regex,
        GrepOptions options,
        string displayPath,
        bool showPrefix,
        StringBuilder output)
    {
        // Collect all lines into array for context access
        var lines = new List<string>();
        foreach (var (_, line) in content.EnumerateLines())
        {
            lines.Add(line.ToString());
        }

        var printedLines = new HashSet<int>();
        var matchCount = 0;
        var lastPrintedLine = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            if (options.MaxCount > 0 && matchCount >= options.MaxCount) break;

            var matches = regex.IsMatch(lines[i]);
            if (options.InvertMatch) matches = !matches;

            if (matches)
            {
                matchCount++;
                
                // Calculate context range
                var startLine = Math.Max(0, i - options.BeforeContext);
                var endLine = Math.Min(lines.Count - 1, i + options.AfterContext);

                // Print separator if there's a gap
                if (lastPrintedLine >= 0 && startLine > lastPrintedLine + 1)
                {
                    output.AppendLine("--");
                }

                // Print context and match
                for (int j = startLine; j <= endLine; j++)
                {
                    if (printedLines.Contains(j)) continue;
                    printedLines.Add(j);
                    
                    var prefix = showPrefix ? $"{displayPath}:" : "";
                    var lineNumStr = options.ShowLineNumbers ? $"{j + 1}:" : "";
                    var separator = (j == i) ? ":" : "-"; // : for match, - for context
                    
                    if (options.ShowLineNumbers && showPrefix)
                    {
                        output.Append($"{displayPath}{separator}{j + 1}{separator}{lines[j]}");
                    }
                    else if (options.ShowLineNumbers)
                    {
                        output.Append($"{j + 1}{separator}{lines[j]}");
                    }
                    else if (showPrefix)
                    {
                        output.Append($"{displayPath}{separator}{lines[j]}");
                    }
                    else
                    {
                        output.Append(lines[j]);
                    }
                    output.AppendLine();
                    
                    lastPrintedLine = j;
                }
            }
        }
    }

    private static void CollectFilesRecursive(IShellContext context, string path, string displayPath, List<(string DisplayPath, string FullPath)> files)
    {
        if (!context.FileSystem.IsDirectory(path))
        {
            files.Add((displayPath, path));
            return;
        }

        foreach (var entry in context.FileSystem.ListDirectory(path))
        {
            var fullPath = path == "/" ? "/" + entry : path + "/" + entry;
            var childDisplayPath = displayPath == "." ? entry : $"{displayPath}/{entry}";

            if (context.FileSystem.IsDirectory(fullPath))
            {
                CollectFilesRecursive(context, fullPath, childDisplayPath, files);
            }
            else
            {
                files.Add((childDisplayPath, fullPath));
            }
        }
    }

    private class GrepOptions
    {
        public bool IgnoreCase { get; set; }
        public bool ShowLineNumbers { get; set; }
        public bool Recursive { get; set; }
        public bool FilesOnly { get; set; }
        public bool CountOnly { get; set; }
        public bool InvertMatch { get; set; }
        public bool WordMatch { get; set; }
        public bool OnlyMatching { get; set; }
        public int MaxCount { get; set; }
        public int BeforeContext { get; set; }
        public int AfterContext { get; set; }
    }
}
