using AgentSandbox.Core.FileSystem;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Copy files or directories command.
/// </summary>
public class CpCommand : IShellCommand
{
    public string Name => "cp";
    public string Description => "Copy files or directories";
    public string Usage => """
        cp [-r] <source>... <dest>

        Options:
          -r, -R    Copy directories recursively
        """;

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var recursive = args.Contains("-r") || args.Contains("-R");
        var paths = args.Where(a => !a.StartsWith('-')).ToList();
        
        if (paths.Count < 2)
            return ShellResult.Error("cp: missing destination file operand");

        var dest = context.ResolvePath(paths[^1]);
        var sources = paths.Take(paths.Count - 1).ToList();

        foreach (var src in sources)
        {
            var srcPath = context.ResolvePath(src);
            
            if (!context.FileSystem.Exists(srcPath))
                return MultiTargetCommandFailurePolicy.FailFast(
                    $"cp: cannot stat '{src}': No such file or directory",
                    sources.Count);

            if (context.FileSystem.IsDirectory(srcPath) && !recursive)
                return MultiTargetCommandFailurePolicy.FailFast(
                    $"cp: -r not specified; omitting directory '{src}'",
                    sources.Count);

            var targetPath = context.FileSystem.IsDirectory(dest) 
                ? dest + "/" + FileSystemPath.GetName(srcPath) 
                : dest;

            context.FileSystem.Copy(srcPath, targetPath);
        }

        return ShellResult.Ok();
    }
}
