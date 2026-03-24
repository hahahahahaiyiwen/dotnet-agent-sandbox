using System.Text.RegularExpressions;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Set environment variable command.
/// </summary>
public class ExportCommand : IShellCommand
{
    private static readonly Regex VariableNameRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public string Name => "export";
    public string Description => "Set environment variable";
    public string Usage => "export VAR=value";

    public ShellResult Execute(string[] args, IShellContext context)
    {
        if (args.Length == 0)
        {
            return ShellResult.Error("export: missing assignment");
        }

        foreach (var arg in args)
        {
            if (!arg.Contains('='))
            {
                return ShellResult.Error($"export: invalid assignment '{arg}'");
            }

            var parts = arg.Split('=', 2);
            if (parts.Length != 2 || !VariableNameRegex.IsMatch(parts[0]))
            {
                return ShellResult.Error($"export: invalid assignment '{arg}'");
            }
        }

        foreach (var arg in args)
        {
            var parts = arg.Split('=', 2);
            if (parts.Length == 2)
            {
                context.Environment[parts[0]] = parts[1];
            }
        }
        return ShellResult.Ok();
    }
}
