namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Remove files or directories command.
/// </summary>
public class RmCommand : IShellCommand
{
    public string Name => "rm";
    public string Description => "Remove files or directories";
    public string Usage => """
        rm [-rf] <path>...

        Options:
          -r, -R    Remove directories recursively
          -f        Force, ignore nonexistent files
        """;

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var recursive = args.Contains("-r") || args.Contains("-rf") || args.Contains("-R");
        var force = args.Contains("-f") || args.Contains("-rf");
        var paths = args.Where(a => !a.StartsWith('-')).ToList();

        if (paths.Count == 0)
            return ShellResult.Error("rm: missing operand");

        foreach (var p in paths)
        {
            var path = context.ResolvePath(p);
            
            if (!context.FileSystem.Exists(path))
            {
                if (!force)
                    return MultiTargetCommandFailurePolicy.FailFast(
                        $"rm: cannot remove '{p}': No such file or directory",
                        paths.Count);
                continue;
            }

            try
            {
                context.FileSystem.Delete(path, recursive);
            }
            catch (InvalidOperationException ex)
            {
                return MultiTargetCommandFailurePolicy.FailFast(
                    $"rm: {ex.Message}",
                    paths.Count);
            }
        }

        return ShellResult.Ok();
    }
}
