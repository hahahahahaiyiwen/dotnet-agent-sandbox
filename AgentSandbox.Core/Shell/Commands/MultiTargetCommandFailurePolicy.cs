namespace AgentSandbox.Core.Shell.Commands;

internal static class MultiTargetCommandFailurePolicy
{
    public static ShellResult FailFast(
        string errorMessage,
        int targetCount,
        Func<string>? partialStdoutFactory = null)
    {
        if (targetCount > 1 && partialStdoutFactory is not null)
        {
            return new ShellResult
            {
                ExitCode = 1,
                Stderr = errorMessage,
                Stdout = partialStdoutFactory()
            };
        }

        return ShellResult.Error(errorMessage);
    }
}
