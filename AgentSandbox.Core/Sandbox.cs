using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Importing;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Skills;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Core.Validation;
using System.Diagnostics;
using System.Text;

namespace AgentSandbox.Core;

/// <summary>
/// Represents a sandboxed execution environment with virtual filesystem and shell.
/// </summary>
public class Sandbox : IDisposable, IObservableSandbox
{
    private readonly FileSystem.FileSystem _fileSystem;
    private readonly SandboxShell _shell;
    private readonly SandboxOptions _options;
    private readonly SandboxTelemetryFacade _telemetry;
    private readonly ObserverManager _observerManager = new();
    private readonly FileImportManager _fileImportManager;
    private readonly SkillManager _skillManager;
    private readonly List<ShellResult> _commandHistory = new();
    private readonly Action<string>? _onDisposed;
    private readonly Activity? _sandboxActivity;
    private bool _disposed;
    private bool TelemetryEnabled => _options.Telemetry?.Enabled == true;

    public Sandbox(string? id = null, SandboxOptions? options = null)
        : this(id, options, null)
    {
    }

    internal Sandbox(string? id, SandboxOptions? options, Action<string>? onDisposed)
    {
        Id = id ?? Guid.NewGuid().ToString("N")[..12];
        _options = options ?? new SandboxOptions();
        _onDisposed = onDisposed;
        
        // Create filesystem with size limits from options
        var fsOptions = new FileSystemOptions
        {
            MaxTotalSize = _options.MaxTotalSize,
            MaxFileSize = _options.MaxFileSize,
            MaxNodeCount = _options.MaxNodeCount
        };

        _fileSystem = new FileSystem.FileSystem(fsOptions);
        
        // Initialize shell and module managers
        _shell = new SandboxShell(_fileSystem, _options.SecretBroker);
        _fileImportManager = new FileImportManager(_fileSystem);
        _skillManager = new SkillManager(_fileSystem);

        CreatedAt = DateTime.UtcNow;
        LastActivityAt = CreatedAt;

        // Initialize telemetry facade
        _telemetry = new SandboxTelemetryFacade(_options, Id, _observerManager.NotifyObservers);
        
        // Start sandbox-level activity for tracing
        if (TelemetryEnabled)
        {
            _sandboxActivity = SandboxTelemetry.StartSandboxActivity(Id);
            _telemetry.RecordSandboxCreated();
        }

        // Apply initial environment
        foreach (var kvp in _options.Environment)
        {
            _shell.Execute($"export {kvp.Key}={kvp.Value}");
        }

        // Set initial working directory
        if (_options.WorkingDirectory != "/")
        {
            ValidatePathInput(_options.WorkingDirectory, nameof(_options.WorkingDirectory));
            _fileSystem.CreateDirectory(_options.WorkingDirectory);
            _shell.Execute($"cd {_options.WorkingDirectory}");
        }

        // Register shell extensions
        foreach (var cmd in _options.ShellExtensions)
        {
            _shell.RegisterCommand(cmd);
        }

        // Import all files (including skills) from configured sources
        foreach (var import in _options.Imports)
        {
            _fileImportManager.Import(import.Path, import.Source);
        }

        // Discover and load skills from the skill base path
        _skillManager.LoadSkills(_options.AgentSkills.BasePath);
    }

    #region  Properties

    public string Id { get; }

    public DateTime CreatedAt { get; }

    public DateTime LastActivityAt { get; private set; }
    
    /// <summary>
    /// Gets the current working directory of the sandbox shell.
    /// </summary>
    public string CurrentDirectory => _shell.CurrentDirectory;

    #endregion

    #region Command Execution

    /// <summary>
    /// Executes a bash shell command in the sandbox.
    /// </summary>
    public ShellResult Execute(string command)
    {
        ThrowIfDisposed();
        LastActivityAt = DateTime.UtcNow;
        ValidateCommandInput(command);

        var stopwatch = Stopwatch.StartNew();
        var activity = _telemetry.StartCommandActivity(command);
        activity?.SetTag("command.cwd", _shell.CurrentDirectory);

        try
        {
            var executionTask = Task.Run(() => _shell.Execute(command));
            if (!executionTask.Wait(_options.CommandTimeout))
            {
                stopwatch.Stop();
                var timeoutResult = ShellResult.Error($"Command execution timed out after {_options.CommandTimeout.TotalMilliseconds} ms.");
                timeoutResult.Command = command;
                timeoutResult.Duration = stopwatch.Elapsed;
                _commandHistory.Add(timeoutResult);
                _telemetry.RecordCommandError(new TimeoutException(timeoutResult.Stderr));
                return timeoutResult;
            }

            var result = executionTask.GetAwaiter().GetResult();
            result = _shell.RedactSecrets(result);
            _commandHistory.Add(result);
            stopwatch.Stop();

            _telemetry.RecordCommandSuccess(command, result, stopwatch.Elapsed);

            return result;
        }
        catch (Exception ex)
        {
            _telemetry.RecordCommandError(ex);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    /// <summary>
    /// Gets a description of the bash shell tool.
    /// Dynamically includes all available commands and extensions.
    /// </summary>
    public string GetBashToolDescription()
    {
        var sb = new StringBuilder();
        
        sb.Append("Run shell commands. ");
        sb.Append($"Available commands: {string.Join(", ", _shell.GetAvailableCommands())}. ");
        sb.Append("Run 'help' to list commands or '<command> -h' for detailed help on a specific command. ");
        sb.Append("Commands use short-style arguments (e.g., -l, -a, -n). ");

        return sb.ToString();
    }

    #endregion


    #region File System I/O

    /// <summary>
    /// Reads file lines within a range as a lazy-evaluated stream.
    /// Useful for reading specific line ranges from large files without materializing the entire file.
    /// </summary>
    /// <param name="path">Path to the file to read.</param>
    /// <param name="startLine">Starting line number (1-indexed), inclusive. If null, defaults to 1.</param>
    /// <param name="endLine">Ending line number (1-indexed), exclusive. If null, reads to end of file.</param>
    /// <returns>Enumerable of lines within the specified range. Line endings are normalized to LF.</returns>
    /// <remarks>
    /// - Line numbers are 1-indexed (first line = 1)
    /// - endLine is exclusive (startLine=1, endLine=4 returns lines 1, 2, 3)
    /// - Lines are yielded lazily as they're encountered during scanning
    /// - Enumeration stops as soon as endLine is reached (early termination)
    /// </remarks>
    /// <exception cref="FileNotFoundException">File does not exist.</exception>
    /// <exception cref="InvalidOperationException">Path is a directory.</exception>
    public IEnumerable<string> ReadFileLines(string path, int? startLine = null, int? endLine = null)
    {
        ThrowIfDisposed();
        LastActivityAt = DateTime.UtcNow;
        ValidatePathInput(path);
        
        var stopwatch = Stopwatch.StartNew();
        var activity = _telemetry.StartReadFileActivity(path);

        try
        {
            // Delegate to FileSystem - it handles line scanning and normalization
            var lines = _fileSystem.ReadFileLines(path, startLine, endLine);
            
            // Wrap in a custom enumerable to ensure telemetry and cleanup happen correctly
            // We need to materialize to capture the metrics properly
            var linesList = lines.ToList();
            
            stopwatch.Stop();
            
            // Record telemetry
            int actualStartLine = startLine ?? 1;
            _telemetry.RecordReadFileSuccess(path, stopwatch, 0, readMode: "partial",
                startLine: actualStartLine, endLine: endLine ?? 0, linesReturned: linesList.Count);

            return linesList;
        }
        catch (Exception ex)
        {
            _telemetry.RecordReadFileError(path, ex);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    /// <summary>
    /// Writes or overwrites a file with the given content as UTF8‐encoded text.
    /// Creates parent directories if they do not exist.
    /// </summary>
    /// <param name="path">Path to the file to write.</param>
    /// <param name="content">File contents to write.</param>
    /// <exception cref="InvalidOperationException">Path is a directory.</exception>
    public void WriteFile(string path, string content)
    {
        ThrowIfDisposed();
        LastActivityAt = DateTime.UtcNow;
        ValidatePathInput(path);
        ValidateWritePayloadInput(content);

        var stopwatch = Stopwatch.StartNew();
        var activity = _telemetry.StartWriteFileActivity(path);

        try
        {
            _fileSystem.WriteFile(path, content);
            stopwatch.Stop();

            _telemetry.RecordWriteFileSuccess(path, stopwatch, content.Length);
        }
        catch (Exception ex)
        {
            _telemetry.RecordWriteFileError(path, ex);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    /// <summary>
    /// Applies a unified diff patch to a file.
    /// Supports basic unified diff format with line-based changes.
    /// </summary>
    /// <param name="path">Path to the file to patch.</param>
    /// <param name="patch">Unified diff patch string (unified diff format).</param>
    /// <exception cref="ArgumentException">Patch format is invalid.</exception>
    /// <exception cref="FileNotFoundException">File does not exist.</exception>
    /// <exception cref="InvalidOperationException">Patch could not be applied (hunk mismatch).</exception>
    public void ApplyPatch(string path, string patch)
    {
        ThrowIfDisposed();
        LastActivityAt = DateTime.UtcNow;
        ValidatePathInput(path);

        var stopwatch = Stopwatch.StartNew();
        var activity = _telemetry.StartApplyPatchActivity(path);

        try
        {
            // Read the current file content, normalizing line endings
            var bytes = _fileSystem.ReadFileBytes(path);
            var text = Encoding.UTF8.GetString(bytes);
            // Normalize to LF only for processing
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = text.Split('\n').ToList();
            // Remove empty last line if present
            if (lines.Count > 0 && string.IsNullOrEmpty(lines[lines.Count - 1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }

            // Parse and apply the unified diff
            var patched = ApplyUnifiedDiff(lines, patch);

            // Write the patched content (using system line endings)
            _fileSystem.WriteFile(path, patched);
            stopwatch.Stop();

            _telemetry.RecordApplyPatchSuccess(path, stopwatch, patched.Length);
        }
        catch (Exception ex)
        {
            _telemetry.RecordApplyPatchError(path, ex);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }


    /// <summary>
    /// Applies a unified diff patch to a list of lines.
    /// Supports basic unified diff format: lines starting with '-' are removed, '+' are added, ' ' are context.
    /// </summary>
    private static string ApplyUnifiedDiff(List<string> lines, string patch)
    {
        var patchLines = patch.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int fileLineIndex = 0;
        var result = new List<string>(lines);

        int patchLineIndex = 0;

        // Skip header lines (--- and +++)
        while (patchLineIndex < patchLines.Length)
        {
            var line = patchLines[patchLineIndex];
            if (line.StartsWith("---") || line.StartsWith("+++"))
            {
                patchLineIndex++;
                continue;
            }

            if (line.StartsWith("@@"))
            {
                // Parse hunk header: @@ -start,count +start,count @@
                var hunkMatch = System.Text.RegularExpressions.Regex.Match(line, @"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
                if (!hunkMatch.Success)
                    throw new ArgumentException("Invalid hunk header in patch");

                int newStart = int.Parse(hunkMatch.Groups[3].Value) - 1; // Convert to 0-based

                fileLineIndex = newStart;
                patchLineIndex++;

                // Process hunk lines
                while (patchLineIndex < patchLines.Length)
                {
                    var hunkLine = patchLines[patchLineIndex];

                    if (hunkLine.StartsWith("@@"))
                        break; // Next hunk

                    if (string.IsNullOrEmpty(hunkLine) && patchLineIndex == patchLines.Length - 1)
                        break; // End of file

                    if (hunkLine.StartsWith("-"))
                    {
                        // Remove line
                        if (fileLineIndex >= result.Count)
                            throw new InvalidOperationException("Patch context does not match file content (line to remove not found)");
                        result.RemoveAt(fileLineIndex);
                    }
                    else if (hunkLine.StartsWith("+"))
                    {
                        // Add line
                        result.Insert(fileLineIndex, hunkLine.Substring(1));
                        fileLineIndex++;
                    }
                    else if (hunkLine.StartsWith(" "))
                    {
                        // Context line (unchanged)
                        var contextLine = hunkLine.Substring(1);
                        if (fileLineIndex >= result.Count || result[fileLineIndex] != contextLine)
                            throw new InvalidOperationException($"Patch context mismatch at line {fileLineIndex + 1}: expected '{contextLine}' but got '{(fileLineIndex < result.Count ? result[fileLineIndex] : "EOF")}'");
                        fileLineIndex++;
                    }
                    else if (hunkLine.StartsWith("\\"))
                    {
                        // "\ No newline at end of file" marker, skip
                    }
                    else if (!string.IsNullOrEmpty(hunkLine))
                    {
                        // Unknown line type, treat as context
                        fileLineIndex++;
                    }

                    patchLineIndex++;
                }
            }
            else
            {
                patchLineIndex++;
            }
        }

        return string.Join(Environment.NewLine, result);
    }
    #endregion

    #region Skill Management

    private void ValidateCommandInput(string command)
    {
        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        var byteCount = Encoding.UTF8.GetByteCount(command);
        if (byteCount > _options.MaxCommandLength)
        {
            throw CoreValidationException.CommandTooLong(byteCount, _options.MaxCommandLength);
        }
    }

    private void ValidateWritePayloadInput(string content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        var byteCount = Encoding.UTF8.GetByteCount(content);
        if (byteCount > _options.MaxWritePayloadBytes)
        {
            throw CoreValidationException.WritePayloadTooLarge(byteCount, _options.MaxWritePayloadBytes);
        }
    }

    private static void ValidatePathInput(string path, string paramName = "path")
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(paramName);
        }

        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment == ".."))
        {
            throw CoreValidationException.PathTraversalDetected(paramName);
        }
    }

    /// <summary>
    /// Gets information about all loaded skills.
    /// </summary>
    public IReadOnlyList<SkillInfo> GetSkills() => _skillManager.GetSkills();

    /// <summary>
    /// Gets a description of loaded skills for use in AI function descriptions.
    /// Uses the XML format recommended by agentskills.io specification.
    /// </summary>
    public string GetSkillsDescription() => _skillManager.GetSkillsDescription();
    
    #endregion

    #region Snapshots

    /// <summary>
    /// Creates a snapshot of the entire sandbox state.
    /// </summary>
    public SandboxSnapshot CreateSnapshot()
    {
        ThrowIfDisposed();
        return new SandboxSnapshot
        {
            Id = Id,
            FileSystemData = _fileSystem.CreateSnapshot(),
            CurrentDirectory = _shell.CurrentDirectory,
            Environment = new Dictionary<string, string>(_shell.Environment),
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Restores sandbox state from a snapshot.
    /// </summary>
    public void RestoreSnapshot(SandboxSnapshot snapshot)
    {
        ThrowIfDisposed();
        _fileSystem.RestoreSnapshot(snapshot.FileSystemData);
        _shell.Execute($"cd {snapshot.CurrentDirectory}");
        
        foreach (var kvp in snapshot.Environment)
        {
            _shell.Execute($"export {kvp.Key}={kvp.Value}");
        }
        
        LastActivityAt = DateTime.UtcNow;
    }

    #endregion

    #region  Metadata

    /// <summary>
    /// Gets command execution history.
    /// </summary>
    public IReadOnlyList<ShellResult> GetHistory() => _commandHistory.AsReadOnly();

    /// <summary>
    /// Gets sandbox statistics.
    /// </summary>
    public SandboxStats GetStats() => new()
    {
        Id = Id,
        FileCount = _fileSystem.NodeCount,
        TotalSize = _fileSystem.TotalSize, // in bytes
        CommandCount = _commandHistory.Count,
        CurrentDirectory = _shell.CurrentDirectory,
        CreatedAt = CreatedAt,
        LastActivityAt = LastActivityAt
    };

    #endregion

    #region Observability

    /// <summary>
    /// Subscribes an observer to receive sandbox events.
    /// </summary>
    public IDisposable Subscribe(ISandboxObserver observer)
    {
        return _observerManager.Subscribe(observer);
    }

    #endregion

    #region  Disposal

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Sandbox));
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;

        // Record sandbox disposal telemetry
        if (TelemetryEnabled)
        {
            _telemetry.RecordSandboxDisposed();
            
            // End sandbox-level activity
            _sandboxActivity?.SetTag("sandbox.command_count", _commandHistory.Count);
            _sandboxActivity?.Dispose();
        }

        _commandHistory.Clear();
        _observerManager.Clear();
        
        // Notify manager to remove reference
        _onDisposed?.Invoke(Id);
        
        GC.SuppressFinalize(this);
    }

    #endregion
}
