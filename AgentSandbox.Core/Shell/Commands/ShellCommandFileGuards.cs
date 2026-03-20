namespace AgentSandbox.Core.Shell.Commands;

internal static class ShellCommandFileGuards
{
    public static bool TryResolveReadableFilePath(
        IShellContext context,
        string commandName,
        string inputPath,
        out string resolvedPath,
        out string errorMessage)
    {
        resolvedPath = context.ResolvePath(inputPath);

        if (!context.FileSystem.Exists(resolvedPath))
        {
            errorMessage = $"{commandName}: {inputPath}: No such file or directory";
            return false;
        }

        if (context.FileSystem.IsDirectory(resolvedPath))
        {
            errorMessage = $"{commandName}: {inputPath}: Is a directory";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
