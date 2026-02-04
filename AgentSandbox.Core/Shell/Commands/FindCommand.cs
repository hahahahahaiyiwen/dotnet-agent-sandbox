using System.Text;
using System.Text.RegularExpressions;
using AgentSandbox.Core.FileSystem;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Find files by name command.
/// </summary>
public class FindCommand : IShellCommand
{
    public string Name => "find";
    public string Description => "Find files by name";
    public string Usage => """
        find [path] [-name pattern]

        Options:
          -name <pattern>    Filter by filename pattern (supports * and ?)
        """;

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var startPath = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : ".";
        var namePattern = "*";
        
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-name")
            {
                namePattern = args[i + 1];
            }
        }

        var basePath = context.ResolvePath(startPath);
        var output = new StringBuilder();
        
        // Create cached regex once, outside the recursive loop
        var regexPattern = "^" + Regex.Escape(namePattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        var regex = context.GetOrCreate(
            regexPattern,
            () => new Regex(regexPattern, RegexOptions.Compiled));
        
        FindRecursive(context, basePath, namePattern, regex, output);

        return ShellResult.Ok(output.ToString().TrimEnd());
    }

    private static void FindRecursive(IShellContext context, string path, string pattern, Regex regex, StringBuilder output)
    {
        var name = FileSystemPath.GetName(path);
        
        if (regex.IsMatch(name) || pattern == "*")
        {
            output.AppendLine(path);
        }

        if (context.FileSystem.IsDirectory(path))
        {
            foreach (var child in context.FileSystem.ListDirectory(path))
            {
                var childPath = path == "/" ? "/" + child : path + "/" + child;
                FindRecursive(context, childPath, pattern, regex, output);
            }
        }
    }
}
