using System.Text;
using AgentSandbox.Core.FileSystem;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// List directory contents command.
/// </summary>
public class LsCommand : IShellCommand
{
    public string Name => "ls";
    public string Description => "List directory contents";
    public string Usage => """
        ls [-laR] [path...]

        Options:
          -a    Show hidden files (starting with .)
          -l    Long format with details
          -R    List subdirectories recursively
        """;

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var showAll = args.Contains("-a") || args.Contains("-la") || args.Contains("-al") || 
                      args.Contains("-laR") || args.Contains("-lRa") || args.Contains("-aR") || 
                      args.Contains("-Ra") || args.Contains("-alR") || args.Contains("-Rla");
        var longFormat = args.Contains("-l") || args.Contains("-la") || args.Contains("-al") ||
                         args.Contains("-laR") || args.Contains("-lRa") || args.Contains("-lR") ||
                         args.Contains("-Rl") || args.Contains("-alR") || args.Contains("-Rla");
        var recursive = args.Contains("-R") || args.Contains("-laR") || args.Contains("-lRa") ||
                        args.Contains("-aR") || args.Contains("-Ra") || args.Contains("-lR") ||
                        args.Contains("-Rl") || args.Contains("-alR") || args.Contains("-Rla");
        var paths = args.Where(a => !a.StartsWith('-')).ToList();
        
        if (paths.Count == 0) paths.Add(".");

        var output = new StringBuilder();
        
        foreach (var p in paths)
        {
            var path = context.ResolvePath(p);
            
            if (!context.FileSystem.Exists(path))
            {
                return MultiTargetCommandFailurePolicy.FailFast(
                    $"ls: cannot access '{p}': No such file or directory",
                    paths.Count,
                    () => output.ToString().TrimEnd());
            }

            if (recursive)
            {
                LsRecursive(context, path, p, showAll, longFormat, output, paths.Count > 1);
            }
            else if (context.FileSystem.IsDirectory(path))
            {
                if (paths.Count > 1)
                {
                    output.AppendLine($"{p}:");
                }
                LsDirectory(context, path, showAll, longFormat, output);
            }
            else
            {
                output.AppendLine(FileSystemPath.GetName(path));
            }
        }

        return ShellResult.Ok(output.ToString().TrimEnd());
    }

    private static void LsDirectory(IShellContext context, string path, bool showAll, bool longFormat, StringBuilder output)
    {
        var entries = context.FileSystem.ListDirectory(path).ToList();
        
        if (!showAll)
        {
            entries = entries.Where(e => !e.StartsWith('.')).ToList();
        }

        if (longFormat)
        {
            foreach (var entry in entries)
            {
                var fullPath = path == "/" ? "/" + entry : path + "/" + entry;
                var node = context.FileSystem.GetEntry(fullPath);
                if (node != null)
                {
                    var type = node.IsDirectory ? "d" : "-";
                    var size = node.IsDirectory ? 0 : node.Content.Length;
                    output.AppendLine($"{type}rw-r--r--  {size,8}  {node.ModifiedAt:MMM dd HH:mm}  {entry}");
                }
            }
        }
        else
        {
            output.AppendLine(string.Join("  ", entries));
        }
    }

    private static void LsRecursive(IShellContext context, string path, string displayPath, bool showAll, bool longFormat, StringBuilder output, bool showHeader)
    {
        if (!context.FileSystem.IsDirectory(path))
        {
            output.AppendLine(displayPath);
            return;
        }

        output.AppendLine($"{displayPath}:");
        LsDirectory(context, path, showAll, longFormat, output);

        var entries = context.FileSystem.ListDirectory(path).ToList();
        if (!showAll)
        {
            entries = entries.Where(e => !e.StartsWith('.')).ToList();
        }

        foreach (var entry in entries)
        {
            var fullPath = path == "/" ? "/" + entry : path + "/" + entry;
            if (context.FileSystem.IsDirectory(fullPath))
            {
                output.AppendLine();
                var childDisplayPath = displayPath == "." ? entry : $"{displayPath}/{entry}";
                LsRecursive(context, fullPath, childDisplayPath, showAll, longFormat, output, true);
            }
        }
    }
}
