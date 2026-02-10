using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Shell.Commands;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentSandbox.Core.Shell;

/// <summary>
/// A sandboxed shell that executes commands against a virtual filesystem.
/// Emulates common Unix commands without touching the real filesystem.
/// Supports extensibility via IShellCommand registration.
/// </summary>
public class SandboxShell : IShellContext
{
    #region Fields

    private readonly IFileSystem _fs;
    private string _currentDirectory = "/";
    private readonly Dictionary<string, string> _environment = new();
    private readonly Dictionary<string, IShellCommand> _builtinCommands = new();
    private readonly Dictionary<string, IShellCommand> _extensionCommands = new();
    private readonly Dictionary<object, object> _sessionCache = new();

    #endregion

    #region IShellContext Explicit Implementation

    IFileSystem IShellContext.FileSystem => _fs;
    
    string IShellContext.CurrentDirectory
    {
        get => _currentDirectory;
        set => _currentDirectory = FileSystemPath.Normalize(value);
    }
    
    IDictionary<string, string> IShellContext.Environment => _environment;
    
    string IShellContext.ResolvePath(string path) => ResolvePath(path);

    T IShellContext.GetOrCreate<T>(object key, Func<T> factory)
    {
        if (_sessionCache.TryGetValue(key, out var cached))
        {
            return (T)cached;
        }
        var value = factory();
        _sessionCache[key] = value!;
        return value;
    }

    #endregion

    #region Constructor

    public SandboxShell(IFileSystem fileSystem)
    {
        _fs = fileSystem;
        
        _environment["PWD"] = _currentDirectory;

        // Register built-in commands
        RegisterBuiltinCommand(new PwdCommand());
        RegisterBuiltinCommand(new CdCommand());
        RegisterBuiltinCommand(new LsCommand());
        RegisterBuiltinCommand(new CatCommand());
        RegisterBuiltinCommand(new EchoCommand());
        RegisterBuiltinCommand(new MkdirCommand());
        RegisterBuiltinCommand(new RmCommand());
        RegisterBuiltinCommand(new CpCommand());
        RegisterBuiltinCommand(new MvCommand());
        RegisterBuiltinCommand(new TouchCommand());
        RegisterBuiltinCommand(new HeadCommand());
        RegisterBuiltinCommand(new TailCommand());
        RegisterBuiltinCommand(new WcCommand());
        RegisterBuiltinCommand(new GrepCommand());
        RegisterBuiltinCommand(new FindCommand());
        RegisterBuiltinCommand(new EnvCommand());
        RegisterBuiltinCommand(new ExportCommand());
        RegisterBuiltinCommand(new ClearCommand());
        RegisterBuiltinCommand(new DateCommand());
    }

    #endregion

    #region Public Interfaces

    /// <summary>
    /// Executes a command string.
    /// </summary>
    public ShellResult Execute(string commandLine)
    {
        var sw = Stopwatch.StartNew();
        
        if (string.IsNullOrWhiteSpace(commandLine))
            return ShellResult.Ok();

        if (!ShellLexer.TryTokenize(commandLine, out List<ShellToken> tokens, out ShellResult tokenizeError))
            return tokenizeError;
        if (tokens.Count == 0)
            return ShellResult.Ok();

        string? redirectFile = null;
        bool appendMode = false;
        var parts = new List<string>();
        var wasQuoted = new List<bool>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind == ShellTokenKind.Operator)
            {
                switch (token.Value)
                {
                    case ">>":
                    case ">":
                        if (i == 0 || i + 1 >= tokens.Count || tokens[i + 1].Kind != ShellTokenKind.Word)
                        {
                            return ShellResult.Error("redirect: missing file operand");
                        }

                        appendMode = token.Value == ">>";
                        redirectFile = tokens[i + 1].Value;
                        i++;
                        break;
                }

                continue;
            }

            parts.Add(ExpandVariables(token.Value));
            wasQuoted.Add(token.WasQuoted);
        }

        if (parts.Count == 0)
            return ShellResult.Ok();

        var cmd = parts[0];
        var cmdLower = cmd.ToLowerInvariant();
        
        // Expand globs in arguments (skip command name, skip quoted args)
        var args = ExpandGlobs(parts.Skip(1).ToArray(), wasQuoted.Skip(1).ToArray());

        ShellResult result;
        
        // Check if it's a direct script execution (./script.sh or /path/to/script.sh)
        if ((cmd.StartsWith("./") || cmd.StartsWith("/")) && cmd.EndsWith(".sh"))
        {
            try
            {
                result = ExecuteShCommand(new[] { cmd }.Concat(args).ToArray());
            }
            catch (Exception ex)
            {
                result = ShellResult.Error($"{cmd}: {ex.Message}");
            }
        }
        // Handle 'sh' command specially (needs to call Execute recursively)
        else if (cmdLower == "sh")
        {
            try
            {
                if (args.Length > 0 && args[0] == "-h")
                {
                    result = ShellResult.Ok("sh - Execute shell script\n\nUsage: sh <script.sh> [args...]");
                }
                else
                {
                    result = ExecuteShCommand(args);
                }
            }
            catch (Exception ex)
            {
                result = ShellResult.Error($"sh: {ex.Message}");
            }
        }
        // Handle 'help' command specially (needs access to extension commands)
        else if (cmdLower == "help")
        {
            if (args.Length > 0 && args[0] == "-h")
            {
                result = ShellResult.Ok("help - Show available commands\n\nUsage: help");
            }
            else
            {
                result = ExecuteHelpCommand();
            }
        }
        // Handle 'which' command specially (needs access to command registries)
        else if (cmdLower == "which")
        {
            if (args.Length > 0 && args[0] == "-h")
            {
                result = ShellResult.Ok("which - Locate a command\n\nUsage: which <command>");
            }
            else
            {
                result = ExecuteWhichCommand(args);
            }
        }
        // Check built-in commands first
        else if (_builtinCommands.TryGetValue(cmdLower, out var command))
        {
            try
            {
                if (args.Length > 0 && args[0] == "-h")
                {
                    result = ShellResult.Ok($"{command.Name} - {command.Description}\n\nUsage: {command.Usage}");
                }
                else
                {
                    result = command.Execute(args, this);
                }
            }
            catch (Exception ex)
            {
                result = ShellResult.Error($"{cmdLower}: {ex.Message}");
            }
        }
        // Then check extension commands
        else if (_extensionCommands.TryGetValue(cmdLower, out var extCommand))
        {
            try
            {
                result = extCommand.Execute(args, this);
            }
            catch (Exception ex)
            {
                result = ShellResult.Error($"{cmdLower}: {ex.Message}");
            }
        }
        else
        {
            result = ShellResult.Error(
                $"{cmdLower}: command not found.\n" +
                "Use 'help' to list available commands.",
                127);
        }

        // Handle output redirection
        if (redirectFile != null && result.Success && !string.IsNullOrEmpty(result.Stdout))
        {
            try
            {
                var path = ResolvePath(redirectFile);
                if (appendMode)
                {
                    // Append mode: read existing content and append new content
                    var existingBytes = _fs.Exists(path) ? _fs.ReadFileBytes(path) : null;
                    var existing = existingBytes != null ? Encoding.UTF8.GetString(existingBytes) : string.Empty;
                    _fs.WriteFile(path, existing + result.Stdout);
                }
                else
                {
                    _fs.WriteFile(path, result.Stdout);
                }
                result = ShellResult.Ok();
            }
            catch (Exception ex)
            {
                result = ShellResult.Error($"redirect: {ex.Message}");
            }
        }

        result.Command = commandLine;
        result.Duration = sw.Elapsed;
        return result;
    }

    /// <summary>
    /// Registers a shell command extension.
    /// </summary>
    public void RegisterCommand(IShellCommand command)
    {
        _extensionCommands[command.Name.ToLowerInvariant()] = command;
        foreach (var alias in command.Aliases)
        {
            _extensionCommands[alias.ToLowerInvariant()] = command;
        }
    }

    /// <summary>
    /// Gets all available command names (built-in and extensions).
    /// </summary>
    public IEnumerable<string> GetAvailableCommands()
    {
        return _builtinCommands.Values.Select(c => c.Name).Distinct()
            .Concat(_extensionCommands.Values.Select(c => c.Name).Distinct())
            .Concat(new[] { "sh", "help" })
            .OrderBy(c => c);
    }

    #endregion

    #region Internal Members (for Sandbox access)

    /// <summary>
    /// Current working directory.
    /// </summary>
    internal string CurrentDirectory
    {
        get => _currentDirectory;
        set => _currentDirectory = FileSystemPath.Normalize(value);
    }
    
    /// <summary>
    /// Environment variables (read-only).
    /// </summary>
    internal IReadOnlyDictionary<string, string> Environment => _environment;

    /// <summary>
    /// Resolves a path relative to the current directory.
    /// </summary>
    internal string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return _currentDirectory;
        if (path.StartsWith('/')) return FileSystemPath.Normalize(path);
        
        var combined = _currentDirectory == "/" 
            ? "/" + path 
            : _currentDirectory + "/" + path;
        
        return FileSystemPath.Normalize(combined);
    }

    #endregion

    #region Private Methods - Command Parsing

    private (string[] Parts, bool[] WasQuoted) ParseCommandLineWithQuoteInfo(string commandLine)
    {
        var parts = new List<string>();
        var wasQuoted = new List<bool>();
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
                    var escaped = GetEscapedChar(next);
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
            }
            else if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
                currentWasQuoted = true;
            }
            else if (c == '\\' && i + 1 < commandLine.Length)
            {
                var next = commandLine[i + 1];
                var escaped = GetEscapedChar(next);
                if (escaped.HasValue)
                {
                    current.Append(escaped.Value);
                    i++;
                    continue;
                }
                current.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    parts.Add(ExpandVariables(current.ToString()));
                    wasQuoted.Add(currentWasQuoted);
                    current.Clear();
                    currentWasQuoted = false;
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            parts.Add(ExpandVariables(current.ToString()));
            wasQuoted.Add(currentWasQuoted);
        }

        return (parts.ToArray(), wasQuoted.ToArray());
    }

    internal static char? GetEscapedChar(char c)
    {
        return c switch
        {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            '\\' => '\\',
            '"' => '"',
            '\'' => '\'',
            ' ' => ' ',
            _ => null
        };
    }

    private int FindUnquotedOperator(string commandLine, string op)
    {
        var inQuote = false;
        var quoteChar = '\0';

        for (int i = 0; i <= commandLine.Length - op.Length; i++)
        {
            var c = commandLine[i];

            if (inQuote)
            {
                if (c == quoteChar) inQuote = false;
            }
            else if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
            }
            else if (commandLine.Substring(i, op.Length) == op)
            {
                if (op == ">" && i + 1 < commandLine.Length && commandLine[i + 1] == '>')
                    continue;
                return i;
            }
        }

        return -1;
    }

    private static int FindUnquotedNewline(string commandLine)
    {
        var inQuote = false;
        var quoteChar = '\0';
        
        for (int i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (inQuote)
            {
                if (c == quoteChar) inQuote = false;
                if (c == '\\' && i + 1 < commandLine.Length) i++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
                continue;
            }

            if (c == '\\' && i + 1 < commandLine.Length)
            {
                i++;
                continue;
            }

            if (c == '\n')
            {
                return i;
            }
        }

        return -1;
    }
    
    private static bool ContainsUnquotedSubstitution(string commandLine)
    {
        var inQuote = false;
        var quoteChar = '\0';
        
        for (int i = 0; i < commandLine.Length - 1; i++)
        {
            var c = commandLine[i];
            
            if (inQuote)
            {
                if (c == quoteChar) inQuote = false;
                if (c == '\\' && i + 1 < commandLine.Length) i++;
                continue;
            }
            
            if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
                continue;
            }
            
            if (c == '\\' && i + 1 < commandLine.Length)
            {
                i++;
                continue;
            }
            
            if (c == '$' && commandLine[i + 1] == '(')
            {
                return true;
            }
        }
        
        return false;
    }
    
    private static bool ContainsUnquotedBackticks(string commandLine)
    {
        var inQuote = false;
        var quoteChar = '\0';
        
        for (int i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            
            if (inQuote)
            {
                if (c == quoteChar) inQuote = false;
                if (c == '\\' && i + 1 < commandLine.Length) i++;
                continue;
            }
            
            if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
                continue;
            }
            
            if (c == '\\' && i + 1 < commandLine.Length)
            {
                i++;
                continue;
            }
            
            if (c == '`')
            {
                return true;
            }
        }
        
        return false;
    }

    private string ExpandVariables(string text)
    {
        text = Regex.Replace(text, @"\$([0-9@#*])", m =>
        {
            var varName = m.Groups[1].Value;
            return _environment.TryGetValue(varName, out var value) ? value : "";
        });
        
        return Regex.Replace(text, @"\$(\w+)", m =>
        {
            var varName = m.Groups[1].Value;
            return _environment.TryGetValue(varName, out var value) ? value : "";
        });
    }

    #endregion

    #region Private Methods - Glob Expansion

    private string[] ExpandGlobs(string[] args, bool[] wasQuoted)
    {
        var result = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var quoted = i < wasQuoted.Length && wasQuoted[i];

            if (quoted || arg.StartsWith("-"))
            {
                result.Add(arg);
                continue;
            }

            if (!ContainsGlobChars(arg))
            {
                result.Add(arg);
                continue;
            }

            var matches = ExpandGlobPattern(arg);
            if (matches.Count > 0)
            {
                result.AddRange(matches);
            }
            else
            {
                result.Add(arg);
            }
        }

        return result.ToArray();
    }

    private static bool ContainsGlobChars(string s)
    {
        return s.Contains('*') || s.Contains('?') || s.Contains('[');
    }

    private List<string> ExpandGlobPattern(string pattern)
    {
        var results = new List<string>();
        string basePath;
        string globPattern;

        if (pattern.StartsWith("/"))
        {
            var lastSlashBeforeGlob = FindLastSlashBeforeGlob(pattern);
            if (lastSlashBeforeGlob == 0)
            {
                basePath = "/";
                globPattern = pattern[1..];
            }
            else
            {
                basePath = pattern[..lastSlashBeforeGlob];
                globPattern = pattern[(lastSlashBeforeGlob + 1)..];
            }
        }
        else
        {
            var slashIndex = FindLastSlashBeforeGlob(pattern);
            if (slashIndex < 0)
            {
                basePath = _currentDirectory;
                globPattern = pattern;
            }
            else
            {
                var relativePart = pattern[..slashIndex];
                basePath = ResolvePath(relativePart);
                globPattern = pattern[(slashIndex + 1)..];
            }
        }

        if (globPattern.Contains('/'))
        {
            ExpandGlobRecursive(basePath, globPattern.Split('/'), 0, results, pattern.StartsWith("/"));
        }
        else
        {
            ExpandGlobSingleLevel(basePath, globPattern, results, pattern.StartsWith("/"));
        }

        results.Sort();
        return results;
    }

    private static int FindLastSlashBeforeGlob(string pattern)
    {
        int lastSlash = -1;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '/') lastSlash = i;
            if (pattern[i] == '*' || pattern[i] == '?' || pattern[i] == '[') break;
        }
        return lastSlash;
    }

    private void ExpandGlobSingleLevel(string basePath, string pattern, List<string> results, bool absolute)
    {
        if (!_fs.IsDirectory(basePath)) return;

        var regex = GlobToRegex(pattern);

        foreach (var name in _fs.ListDirectory(basePath))
        {
            if (regex.IsMatch(name))
            {
                var fullPath = basePath == "/" ? "/" + name : basePath + "/" + name;
                results.Add(absolute ? fullPath : GetRelativePath(fullPath));
            }
        }
    }

    private void ExpandGlobRecursive(string basePath, string[] patternParts, int partIndex, List<string> results, bool absolute)
    {
        if (partIndex >= patternParts.Length) return;

        var pattern = patternParts[partIndex];
        var isLast = partIndex == patternParts.Length - 1;

        if (!_fs.IsDirectory(basePath)) return;

        var regex = GlobToRegex(pattern);

        foreach (var name in _fs.ListDirectory(basePath))
        {
            if (regex.IsMatch(name))
            {
                var fullPath = basePath == "/" ? "/" + name : basePath + "/" + name;

                if (isLast)
                {
                    results.Add(absolute ? fullPath : GetRelativePath(fullPath));
                }
                else if (_fs.IsDirectory(fullPath))
                {
                    ExpandGlobRecursive(fullPath, patternParts, partIndex + 1, results, absolute);
                }
            }
        }
    }

    private string GetRelativePath(string absolutePath)
    {
        if (_currentDirectory == "/")
            return absolutePath.TrimStart('/');

        if (absolutePath.StartsWith(_currentDirectory + "/"))
            return absolutePath[(_currentDirectory.Length + 1)..];

        return absolutePath;
    }

    private Regex GlobToRegex(string pattern)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".")
            .Replace("\\[", "[")
            .Replace("\\]", "]") + "$";
        return ((IShellContext)this).GetOrCreate(
            regexPattern,
            () => new Regex(regexPattern, RegexOptions.Compiled));
    }

    #endregion

    #region Private Methods - Script Execution and Help

    private void RegisterBuiltinCommand(IShellCommand command)
    {
        _builtinCommands[command.Name.ToLowerInvariant()] = command;
        foreach (var alias in command.Aliases)
        {
            _builtinCommands[alias.ToLowerInvariant()] = command;
        }
    }

    private ShellResult ExecuteHelpCommand()
    {
        var output = new StringBuilder();
        output.AppendLine("Available commands:");
        output.AppendLine("  pwd              Print working directory");
        output.AppendLine("  cd [dir]         Change directory");
        output.AppendLine("  ls [-la] [path]  List directory contents");
        output.AppendLine("  cat <file>       Display file contents");
        output.AppendLine("  echo [text]      Print text");
        output.AppendLine("  mkdir [-p] <dir> Create directory");
        output.AppendLine("  rm [-rf] <path>  Remove file or directory");
        output.AppendLine("  cp <src> <dest>  Copy file or directory");
        output.AppendLine("  mv <src> <dest>  Move/rename file or directory");
        output.AppendLine("  touch <file>     Create empty file or update timestamp");
        output.AppendLine("  head [-n N] <f>  Show first N lines");
        output.AppendLine("  tail [-n N] <f>  Show last N lines");
        output.AppendLine("  wc <file>        Count lines, words, bytes");
        output.AppendLine("  grep <pat> <f>   Search for pattern in files");
        output.AppendLine("  find [path]      Find files");
        output.AppendLine("  env              Show environment variables");
        output.AppendLine("  export VAR=val   Set environment variable");
        output.AppendLine("  date [+FMT]      Display current date/time");
        output.AppendLine("  sh <script>      Execute shell script");
        output.AppendLine("  which <cmd>      Locate a command");
        output.AppendLine("  help             Show this help");
        output.AppendLine();
        output.AppendLine("Use '<command> -h' for detailed help on a specific command.");

        // Add extension commands if any
        var extensions = _extensionCommands.Values.Distinct().OrderBy(c => c.Name).ToList();
        if (extensions.Count > 0)
        {
            output.AppendLine();
            output.AppendLine("Extension commands:");
            foreach (var cmd in extensions)
            {
                output.AppendLine($"  {cmd.Name,-16} {cmd.Description}");
            }
        }

        return ShellResult.Ok(output.ToString().TrimEnd());
    }

    private ShellResult ExecuteWhichCommand(string[] args)
    {
        if (args.Length == 0)
            return ShellResult.Error("which: missing argument\nUsage: which <command>");

        var cmdName = args[0].ToLowerInvariant();
        
        // Check built-in commands (including special ones: help, which, sh)
        var specialCommands = new[] { "help", "which", "sh" };
        if (specialCommands.Contains(cmdName) || _builtinCommands.ContainsKey(cmdName))
        {
            return ShellResult.Ok($"/bin/{cmdName}");
        }

        // Check extension commands
        if (_extensionCommands.ContainsKey(cmdName))
        {
            return ShellResult.Ok($"/usr/bin/{cmdName}");
        }

        return ShellResult.Error($"which: {args[0]}: command not found", exitCode: 1);
    }

    private ShellResult ExecuteShCommand(string[] args)
    {
        if (args.Length == 0)
            return ShellResult.Error("sh: missing script path\nUsage: sh <script.sh> [args...]");

        var scriptPath = ResolvePath(args[0]);

        if (!_fs.Exists(scriptPath) || !_fs.IsFile(scriptPath))
            return ShellResult.Error($"sh: {args[0]}: No such file");

        var scriptBytes = _fs.ReadFileBytes(scriptPath);
        var scriptContent = Encoding.UTF8.GetString(scriptBytes);
        var scriptArgs = args.Skip(1).ToArray();

        return ExecuteScript(scriptContent, scriptArgs);
    }

    private ShellResult ExecuteScript(string script, string[] args)
    {
        var savedParams = new Dictionary<string, string?>();
        var paramNames = new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "@", "#", "*" };
        foreach (var name in paramNames)
        {
            savedParams[name] = _environment.TryGetValue(name, out var val) ? val : null;
        }

        try
        {
            for (int i = 0; i < args.Length && i < 9; i++)
            {
                _environment[$"{i + 1}"] = args[i];
            }
            for (int i = args.Length; i < 9; i++)
            {
                _environment.Remove($"{i + 1}");
            }
            
            _environment["@"] = string.Join(" ", args);
            _environment["*"] = string.Join(" ", args);
            _environment["#"] = args.Length.ToString();

            var lines = script.Split('\n');
            var output = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                var result = Execute(trimmed);

                if (!string.IsNullOrEmpty(result.Stdout))
                {
                    if (output.Length > 0) output.AppendLine();
                    output.Append(result.Stdout);
                }

                if (result.ExitCode != 0)
                {
                    return new ShellResult
                    {
                        ExitCode = result.ExitCode,
                        Stdout = output.ToString(),
                        Stderr = result.Stderr
                    };
                }
            }

            return ShellResult.Ok(output.ToString());
        }
        finally
        {
            foreach (var (name, value) in savedParams)
            {
                if (value != null)
                    _environment[name] = value;
                else
                    _environment.Remove(name);
            }
        }
    }

    #endregion
}
