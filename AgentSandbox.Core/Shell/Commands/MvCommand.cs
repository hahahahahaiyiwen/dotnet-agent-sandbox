using AgentSandbox.Core.FileSystem;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Move/rename files or directories command.
/// </summary>
public class MvCommand : IShellCommand
{
    public string Name => "mv";
    public string Description => "Move/rename files or directories";
    public string Usage => "mv <source>... <dest>";

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var paths = args.Where(a => !a.StartsWith('-')).ToList();
        
        if (paths.Count < 2)
            return ShellResult.Error("mv: missing destination file operand");

        var dest = context.ResolvePath(paths[^1]);
        var sources = paths.Take(paths.Count - 1).ToList();

        foreach (var src in sources)
        {
            var srcPath = context.ResolvePath(src);
            
            if (!context.FileSystem.Exists(srcPath))
                return MultiTargetCommandFailurePolicy.FailFast(
                    $"mv: cannot stat '{src}': No such file or directory",
                    sources.Count);

            var targetPath = context.FileSystem.IsDirectory(dest) 
                ? dest + "/" + FileSystemPath.GetName(srcPath) 
                : dest;

            context.FileSystem.Move(srcPath, targetPath);
        }

        return ShellResult.Ok();
    }
}
