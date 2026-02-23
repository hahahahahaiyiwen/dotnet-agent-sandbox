using System.Text;

namespace AgentSandbox.Core.Shell;

internal enum ShellTokenKind
{
    Word,
    Operator
}

internal readonly record struct ShellToken(string Value, ShellTokenKind Kind, bool WasQuoted);

internal static class ShellLexer
{
    public static bool TryTokenize(string commandLine, out List<ShellToken> tokens, out ShellResult error)
    {
        tokens = new List<ShellToken>();
        error = ShellResult.Ok();
        var current = new StringBuilder();
        var inQuote = false;
        var quoteChar = '\0';
        var currentWasQuoted = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (inQuote)
            {
                if (c == '\\' && i + 1 < commandLine.Length)
                {
                    var next = commandLine[i + 1];
                    var escaped = SandboxShell.GetEscapedChar(next);
                    if (escaped.HasValue)
                    {
                        current.Append(escaped.Value);
                        i++;
                        continue;
                    }
                }

                if (c == quoteChar)
                {
                    inQuote = false;
                }
                else
                {
                    current.Append(c);
                }
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
                currentWasQuoted = true;
                continue;
            }

            if (c == '\\' && i + 1 < commandLine.Length)
            {
                var next = commandLine[i + 1];
                var escaped = SandboxShell.GetEscapedChar(next);
                if (escaped.HasValue)
                {
                    current.Append(escaped.Value);
                    i++;
                    continue;
                }
                current.Append(c);
                continue;
            }

            if (c == '\n')
            {
                error = ShellResult.Error(
                    "Multi-line scripts are not supported. Workarounds:\n" +
                    "  - Execute commands separately\n" +
                    "  - Save commands in a .sh file and run: sh <script.sh>");
                return false;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushCurrent(tokens, current, ref currentWasQuoted);
                continue;
            }

            if (TryReadOperator(commandLine, ref i, out var op))
            {
                switch (op)
                {
                    case "|":
                        error = ShellResult.Error(
                            "Pipelines are not supported. Workarounds:\n" +
                            "  - Use file arguments: 'grep pattern file.txt' instead of 'cat file.txt | grep pattern'\n" +
                            "  - Execute commands separately and process output programmatically\n" +
                            "  - Use shell scripts (.sh) to sequence commands");
                        return false;
                    case "&&":
                        FlushCurrent(tokens, current, ref currentWasQuoted);
                        tokens.Add(new ShellToken(op, ShellTokenKind.Operator, false));
                        continue;
                    case "||":
                        error = ShellResult.Error(
                            "Command chaining (||) is not supported. Workarounds:\n" +
                            "  - Execute commands separately and check results\n" +
                            "  - Use shell scripts (.sh) to sequence commands");
                        return false;
                    case ";":
                        FlushCurrent(tokens, current, ref currentWasQuoted);
                        tokens.Add(new ShellToken(op, ShellTokenKind.Operator, false));
                        continue;
                    case "&":
                        error = ShellResult.Error(
                            "Background jobs (&) are not supported. Workarounds:\n" +
                            "  - Execute commands sequentially\n" +
                            "  - Use shell scripts (.sh) to sequence commands");
                        return false;
                    case "$(":
                    case "`":
                        error = ShellResult.Error(
                            "Command substitution is not supported. Workarounds:\n" +
                            "  - Execute commands separately and pass outputs explicitly\n" +
                            "  - Use shell scripts (.sh) to sequence commands");
                        return false;
                    case "<<":
                        error = ShellResult.Error(
                            "Heredoc (<<) is not supported. Workarounds:\n" +
                            "  - Write content to a file first, then use file as argument\n" +
                            "  - Use 'echo \"content\" > file.txt' to create input files");
                        return false;
                    case "<":
                        error = ShellResult.Error(
                            "Input redirection (<) is not supported. Workarounds:\n" +
                            "  - Use file arguments directly: 'cat file.txt' instead of 'cat < file.txt'\n" +
                            "  - Most commands accept file paths as arguments");
                        return false;
                    case ">>":
                    case ">":
                        FlushCurrent(tokens, current, ref currentWasQuoted);
                        tokens.Add(new ShellToken(op, ShellTokenKind.Operator, false));
                        continue;
                }
            }

            current.Append(c);
        }

        FlushCurrent(tokens, current, ref currentWasQuoted);
        return true;
    }

    private static void FlushCurrent(List<ShellToken> tokens, StringBuilder current, ref bool currentWasQuoted)
    {
        if (current.Length == 0)
        {
            currentWasQuoted = false;
            return;
        }

        tokens.Add(new ShellToken(current.ToString(), ShellTokenKind.Word, currentWasQuoted));
        current.Clear();
        currentWasQuoted = false;
    }

    private static bool TryReadOperator(string commandLine, ref int index, out string op)
    {
        op = string.Empty;
        var c = commandLine[index];

        if (c == '$' && index + 1 < commandLine.Length && commandLine[index + 1] == '(')
        {
            op = "$(";
            index++;
            return true;
        }

        if (c == '`')
        {
            op = "`";
            return true;
        }

        if (c == '&')
        {
            if (index + 1 < commandLine.Length && commandLine[index + 1] == '&')
            {
                op = "&&";
                index++;
                return true;
            }
            op = "&";
            return true;
        }

        if (c == '|')
        {
            if (index + 1 < commandLine.Length && commandLine[index + 1] == '|')
            {
                op = "||";
                index++;
                return true;
            }
            op = "|";
            return true;
        }

        if (c == ';')
        {
            op = ";";
            return true;
        }

        if (c == '<')
        {
            if (index + 1 < commandLine.Length && commandLine[index + 1] == '<')
            {
                op = "<<";
                index++;
                return true;
            }
            op = "<";
            return true;
        }

        if (c == '>')
        {
            if (index + 1 < commandLine.Length && commandLine[index + 1] == '>')
            {
                op = ">>";
                index++;
                return true;
            }
            op = ">";
            return true;
        }

        return false;
    }

}
