using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Capabilities;
using AgentSandbox.Core.Importing;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Skills;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Core.Validation;
using AgentSandbox.Core.Metadata;
using System.Diagnostics;
using System.Text;
using System.Threading;

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
    private readonly Dictionary<Type, object> _capabilities = new();
    private readonly ISandboxEventEmitter _eventEmitter;
    private readonly SandboxOperationJournal _operationJournal;
    private readonly ReaderWriterLockSlim _fileOperationLock = new(LockRecursionPolicy.NoRecursion);
    private readonly object _journalSync = new();
    private readonly Action<string>? _onDisposed;
    private readonly Activity? _sandboxActivity;
    private int _disposeRequested;
    private int _cleanupCompleted;
    private int _cleanupScheduled;
    private int _disposed;
    private int _operationInProgress;
    private Task<ShellResult>? _timedOutCommandTask;
    private const string ConcurrentOperationError =
        "Another sandbox operation is already in progress. This operation conflicts with the sandbox concurrency rules in Phase 2.";
    private const string TimedOutCommandInProgressError =
        "A timed-out command is still completing in the background. Wait for it to finish before issuing another operation.";
    private bool TelemetryEnabled => _options.Telemetry?.Enabled == true;

    public Sandbox(string? id = null, SandboxOptions? options = null)
        : this(id, options, null)
    {
    }

    internal Sandbox(string? id, SandboxOptions? options, Action<string>? onDisposed)
    {
        Id = id ?? Guid.NewGuid().ToString("N")[..12];
        _options = (options ?? new SandboxOptions()).Clone();
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
        _shell = new SandboxShell(_fileSystem, _options.SecretBroker, _options.SecretPolicy);
        _fileImportManager = new FileImportManager(_fileSystem);
        _skillManager = new SkillManager(_fileSystem);
        _operationJournal = new SandboxOperationJournal(_options.Journal);

        CreatedAt = DateTime.UtcNow;
        LastActivityAt = CreatedAt;

        // Initialize telemetry facade
        _eventEmitter = new SandboxEventEmitter(_observerManager.NotifyObservers, HandleSandboxEvent);
        _telemetry = new SandboxTelemetryFacade(_options, Id, _eventEmitter);
        
        // Start sandbox-level activity for tracing
        if (TelemetryEnabled)
        {
            _sandboxActivity = SandboxTelemetryHelper.StartSandboxActivity(Id);
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

        InitializeCapabilities();

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

    #region Capabilities

    /// <summary>
    /// Gets a registered capability implementation by interface type.
    /// </summary>
    public TCapability GetCapability<TCapability>() where TCapability : class
    {
        EnterOperationGate();
        try
        {
            if (TryGetCapabilityUnsafe<TCapability>(out var capability))
            {
                return capability!;
            }

            throw new InvalidOperationException($"Capability '{typeof(TCapability).Name}' is not registered.");
        }
        finally
        {
            ExitOperationGate();
        }
    }

    /// <summary>
    /// Tries to get a registered capability implementation by interface type.
    /// </summary>
    public bool TryGetCapability<TCapability>(out TCapability? capability) where TCapability : class
    {
        EnterOperationGate();
        try
        {
            return TryGetCapabilityUnsafe(out capability);
        }
        finally
        {
            ExitOperationGate();
        }
    }

    private void InitializeCapabilities()
    {
        if (_options.Capabilities.Count == 0)
        {
            return;
        }

        foreach (var capability in _options.Capabilities)
        {
            RegisterCapabilityInterfaces(capability);
        }

        var context = new SandboxContext(Id, _options, _fileSystem, _shell, _eventEmitter, _capabilities);
        foreach (var capability in _options.Capabilities)
        {
            capability.Initialize(context);
        }
    }

    private void RegisterCapabilityInterfaces(ISandboxCapability capability)
    {
        var capabilityType = capability.GetType();
        _capabilities[capabilityType] = capability;

        foreach (var iface in capabilityType.GetInterfaces())
        {
            if (iface == typeof(ISandboxCapability) ||
                iface == typeof(IDisposable) ||
                iface == typeof(IAsyncDisposable))
            {
                continue;
            }

            if (_capabilities.TryGetValue(iface, out var existing) && !ReferenceEquals(existing, capability))
            {
                throw new InvalidOperationException(
                    $"Capability interface '{iface.Name}' is already registered by '{existing.GetType().Name}'.");
            }

            _capabilities[iface] = capability;
        }
    }

    #endregion

    #region Command Execution

    /// <summary>
    /// Executes a bash shell command in the sandbox.
    /// </summary>
    public ShellResult Execute(string command)
    {
        EnterOperationGate();
        EnterFileWriteLane();
        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;
        var releaseOperationGate = true;

        try
        {
            LastActivityAt = DateTime.UtcNow;
            ValidateCommandInput(command);

            activity = _telemetry.StartCommandActivity(command);
            activity?.SetTag("command.cwd", _shell.CurrentDirectory);

            var executionTask = Task.Run(() => _shell.Execute(command));
            if (!executionTask.Wait(_options.CommandTimeout))
            {
                stopwatch.Stop();
                var timeoutResult = ShellResult.Error($"Command execution timed out after {_options.CommandTimeout.TotalMilliseconds} ms.");
                timeoutResult.Command = command;
                timeoutResult.Duration = stopwatch.Elapsed;
                AppendShellOperation(timeoutResult);
                _telemetry.RecordCommandError(new TimeoutException(timeoutResult.Stderr));
                _telemetry.RecordSandboxExecuted(SandboxTelemetryHelper.GetCommandName(command), timeoutResult.ExitCode, timeoutResult.Duration);
                TrackTimedOutCommand(executionTask);
                releaseOperationGate = false;
                return timeoutResult;
            }

            var result = executionTask.GetAwaiter().GetResult();
            result = _shell.RedactSecrets(result);
            AppendShellOperation(result);
            stopwatch.Stop();

            _telemetry.RecordCommandSuccess(command, result, stopwatch.Elapsed);
            _telemetry.RecordSandboxExecuted(SandboxTelemetryHelper.GetCommandName(command), result.ExitCode, stopwatch.Elapsed);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _telemetry.RecordCommandError(ex);
            _telemetry.RecordSandboxExecuted(SandboxTelemetryHelper.GetCommandName(command), -1, stopwatch.Elapsed);
            throw;
        }
        finally
        {
            ExitFileWriteLane();
            if (releaseOperationGate)
            {
                ExitOperationGate();
            }
            activity?.Dispose();
        }
    }

    /// <summary>
    /// Gets a description of the bash shell tool.
    /// Dynamically includes all available commands and extensions.
    /// </summary>
    public string GetBashToolDescription()
    {
        EnterOperationGate();
        try
        {
            var sb = new StringBuilder();
            
            sb.Append("Run shell commands. ");
            sb.Append($"Available commands: {string.Join(", ", _shell.GetAvailableCommands())}. ");
            sb.Append("Run 'help' to list commands or '<command> -h' for detailed help on a specific command. ");
            sb.Append("Commands use short-style arguments (e.g., -l, -a, -n). ");

            return sb.ToString();
        }
        finally
        {
            ExitOperationGate();
        }
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
        EnterFileReadLane();
        
        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;

        try
        {
            LastActivityAt = DateTime.UtcNow;
            ValidatePathInput(path);
            activity = _telemetry.StartReadFileActivity(path);

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
            ExitFileReadLane();
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
        EnterFileWriteLane();

        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;

        try
        {
            LastActivityAt = DateTime.UtcNow;
            ValidatePathInput(path);
            ValidateWritePayloadInput(content);
            activity = _telemetry.StartWriteFileActivity(path);

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
            ExitFileWriteLane();
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
        EnterFileWriteLane();

        var stopwatch = Stopwatch.StartNew();
        Activity? activity = null;

        try
        {
            LastActivityAt = DateTime.UtcNow;
            ValidatePathInput(path);
            activity = _telemetry.StartApplyPatchActivity(path);

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
            ExitFileWriteLane();
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
        EnterOperationGate();
        EnterFileReadLane();
        try
        {
            var fileSystemData = _fileSystem.CreateSnapshot();
            var createdAt = DateTime.UtcNow;
            var stats = BuildStatsUnsafe();
            return new SandboxSnapshot
            {
                Id = Id,
                FileSystemData = fileSystemData,
                CurrentDirectory = _shell.CurrentDirectory,
                Environment = new Dictionary<string, string>(_shell.Environment),
                CreatedAt = createdAt,
                Metadata = new SnapshotMetadata
                {
                    SchemaVersion = 1,
                    SnapshotSizeBytes = fileSystemData.LongLength,
                    FileCount = stats.FileCount,
                    CreatedAt = createdAt,
                    SourceSandboxId = Id,
                    SourceSessionId = Id
                }
            };
        }
        finally
        {
            ExitFileReadLane();
            ExitOperationGate();
        }
    }

    /// <summary>
    /// Restores sandbox state from a snapshot.
    /// </summary>
    public void RestoreSnapshot(SandboxSnapshot snapshot)
    {
        EnterOperationGate();
        EnterFileWriteLane();
        try
        {
            _fileSystem.RestoreSnapshot(snapshot.FileSystemData);
            _shell.Execute($"cd {snapshot.CurrentDirectory}");
            
            foreach (var kvp in snapshot.Environment)
            {
                _shell.Execute($"export {kvp.Key}={kvp.Value}");
            }
            
            LastActivityAt = DateTime.UtcNow;
            _telemetry.RecordSnapshotRestored(snapshot.Id);
        }
        finally
        {
            ExitFileWriteLane();
            ExitOperationGate();
        }
    }

    #endregion

    #region  Metadata

    /// <summary>
    /// Gets command execution history.
    /// </summary>
    public IReadOnlyList<ShellResult> GetHistory()
    {
        ThrowIfDisposed();
        ThrowIfTimedOutCommandInProgress();
        lock (_journalSync)
        {
            return _operationJournal.GetCommandHistoryProjection();
        }
    }

    /// <summary>
    /// Gets sandbox statistics.
    /// </summary>
    public SandboxStats GetStats()
    {
        EnterFileReadLane();
        try
        {
            return BuildStatsUnsafe();
        }
        finally
        {
            ExitFileReadLane();
        }
    }

    #endregion

    #region Observability

    /// <summary>
    /// Subscribes an observer to receive sandbox events.
    /// </summary>
    public IDisposable Subscribe(ISandboxObserver observer)
    {
        EnterOperationGate();
        try
        {
            return _observerManager.Subscribe(observer);
        }
        finally
        {
            ExitOperationGate();
        }
    }

    #endregion

    #region  Disposal

    private void EnterOperationGate()
    {
        ThrowIfDisposed();

        if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
        {
            var timedOutTask = Volatile.Read(ref _timedOutCommandTask);
            if (timedOutTask is not null && !timedOutTask.IsCompleted)
            {
                throw new InvalidOperationException(TimedOutCommandInProgressError);
            }

            throw new InvalidOperationException(ConcurrentOperationError);
        }

        // Re-check after acquisition to close races with Dispose().
        if (Volatile.Read(ref _disposed) == 1 || Volatile.Read(ref _disposeRequested) == 1)
        {
            ExitOperationGate();
            throw new ObjectDisposedException(nameof(Sandbox));
        }
    }

    private void ExitOperationGate()
    {
        Interlocked.Exchange(ref _operationInProgress, 0);
    }

    private void EnterFileReadLane()
    {
        ThrowIfDisposed();
        ThrowIfTimedOutCommandInProgress();
        _fileOperationLock.EnterReadLock();

        if (Volatile.Read(ref _disposed) == 1 || Volatile.Read(ref _disposeRequested) == 1)
        {
            _fileOperationLock.ExitReadLock();
            throw new ObjectDisposedException(nameof(Sandbox));
        }
        var timedOutTask = Volatile.Read(ref _timedOutCommandTask);
        if (timedOutTask is not null && !timedOutTask.IsCompleted)
        {
            _fileOperationLock.ExitReadLock();
            throw new InvalidOperationException(TimedOutCommandInProgressError);
        }
    }

    private void ExitFileReadLane()
    {
        if (_fileOperationLock.IsReadLockHeld)
        {
            _fileOperationLock.ExitReadLock();
        }
    }

    private void EnterFileWriteLane()
    {
        ThrowIfDisposed();
        ThrowIfTimedOutCommandInProgress();
        _fileOperationLock.EnterWriteLock();

        if (Volatile.Read(ref _disposed) == 1 || Volatile.Read(ref _disposeRequested) == 1)
        {
            _fileOperationLock.ExitWriteLock();
            throw new ObjectDisposedException(nameof(Sandbox));
        }
        var timedOutTask = Volatile.Read(ref _timedOutCommandTask);
        if (timedOutTask is not null && !timedOutTask.IsCompleted)
        {
            _fileOperationLock.ExitWriteLock();
            throw new InvalidOperationException(TimedOutCommandInProgressError);
        }
    }

    private void ExitFileWriteLane()
    {
        if (_fileOperationLock.IsWriteLockHeld)
        {
            _fileOperationLock.ExitWriteLock();
        }
    }

    private void TrackTimedOutCommand(Task<ShellResult> executionTask)
    {
        Volatile.Write(ref _timedOutCommandTask, executionTask);
        if (executionTask.IsCompleted)
        {
            Volatile.Write(ref _timedOutCommandTask, null);
            ExitOperationGate();
            return;
        }

        executionTask.ContinueWith(static (task, state) =>
        {
            var sandbox = (Sandbox)state!;

            if (task.Status == TaskStatus.Faulted && task.Exception is not null)
            {
                sandbox._telemetry.RecordCommandError(task.Exception.GetBaseException());
            }

            Volatile.Write(ref sandbox._timedOutCommandTask, null);
            sandbox.ExitOperationGate();
        }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private SandboxStats BuildStatsUnsafe()
    {
        int commandCount;
        int capabilityOperationCount;
        lock (_journalSync)
        {
            commandCount = _operationJournal.CountByCategory("shell");
            capabilityOperationCount = _operationJournal.CountByCategory("capability");
        }

        return new SandboxStats
        {
            Id = Id,
            FileCount = _fileSystem.NodeCount,
            TotalSize = _fileSystem.TotalSize, // in bytes
            CommandCount = commandCount,
            CapabilityOperationCount = capabilityOperationCount,
            CurrentDirectory = _shell.CurrentDirectory,
            CreatedAt = CreatedAt,
            LastActivityAt = LastActivityAt
        };
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1 || Volatile.Read(ref _disposeRequested) == 1)
            throw new ObjectDisposedException(nameof(Sandbox));
    }

    private void ThrowIfTimedOutCommandInProgress()
    {
        var timedOutTask = Volatile.Read(ref _timedOutCommandTask);
        if (timedOutTask is not null && !timedOutTask.IsCompleted)
        {
            throw new InvalidOperationException(TimedOutCommandInProgressError);
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposeRequested, 1, 0) != 0)
        {
            return;
        }

        if (TryEnterDisposeGate(TimeSpan.FromSeconds(5)))
        {
            CompleteDisposeCleanup();
            return;
        }

        ScheduleDeferredCleanup();
    }

    private void CompleteDisposeCleanup()
    {
        _fileOperationLock.EnterWriteLock();
        try
        {
            if (Interlocked.CompareExchange(ref _cleanupCompleted, 1, 0) != 0)
            {
                return;
            }

            // Record sandbox disposal telemetry
            if (TelemetryEnabled)
            {
                _telemetry.RecordSandboxDisposed();
                
                // End sandbox-level activity
                lock (_journalSync)
                {
                    _sandboxActivity?.SetTag("sandbox.command_count", _operationJournal.CountByCategory("shell"));
                }
                _sandboxActivity?.Dispose();
            }

            var disposedCapabilities = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var capability in _capabilities.Values)
            {
                if (!disposedCapabilities.Add(capability))
                {
                    continue;
                }

                if (capability is IDisposable disposable)
                {
                    disposable.Dispose();
                    continue;
                }

                if (capability is IAsyncDisposable asyncDisposable)
                {
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }

            _capabilities.Clear();
            lock (_journalSync)
            {
                _operationJournal.Clear();
            }
            _observerManager.Clear();
            
            // Notify manager to remove reference
            _onDisposed?.Invoke(Id);
            Interlocked.Exchange(ref _disposed, 1);

            GC.SuppressFinalize(this);
        }
        finally
        {
            _fileOperationLock.ExitWriteLock();
            ExitOperationGate();
            _fileOperationLock.Dispose();
        }
    }

    private void ScheduleDeferredCleanup()
    {
        if (Interlocked.CompareExchange(ref _cleanupScheduled, 1, 0) != 0)
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                while (Volatile.Read(ref _cleanupCompleted) == 0)
                {
                    if (TryEnterDisposeGate(TimeSpan.FromMilliseconds(100)))
                    {
                        CompleteDisposeCleanup();
                        return;
                    }

                    var timedOutTask = Volatile.Read(ref _timedOutCommandTask);
                    if (timedOutTask is not null)
                    {
                        try
                        {
                            timedOutTask.Wait(TimeSpan.FromMilliseconds(100));
                        }
                        catch
                        {
                            // ignore and retry gate acquisition
                        }
                    }
                    else
                    {
                        Thread.Sleep(50);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _cleanupScheduled, 0);
            }
        });
    }

    private bool TryEnterCapabilityUnsafe<TCapability>(out TCapability? capability) where TCapability : class
    {
        if (_capabilities.TryGetValue(typeof(TCapability), out var instance))
        {
            capability = (TCapability)instance;
            return true;
        }

        capability = null;
        return false;
    }

    private bool TryGetCapabilityUnsafe<TCapability>(out TCapability? capability) where TCapability : class
        => TryEnterCapabilityUnsafe(out capability);

    private bool TryEnterDisposeGate(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) == 0)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    #endregion

    #region Metadata Journal

    private void HandleSandboxEvent(SandboxEvent sandboxEvent)
    {
        if (sandboxEvent is CapabilityOperationEvent capabilityEvent)
        {
            var metadata = new Dictionary<string, object?>(
                capabilityEvent.Metadata ?? new Dictionary<string, object?>());

            metadata["operationType"] = capabilityEvent.OperationType;
            metadata["errorCode"] = capabilityEvent.ErrorCode;
            metadata["errorMessage"] = capabilityEvent.ErrorMessage;

            lock (_journalSync)
            {
                _operationJournal.Append(new SandboxOperationRecord
                {
                    Timestamp = capabilityEvent.Timestamp,
                    Category = "capability",
                    Operation = capabilityEvent.OperationName,
                    Target = capabilityEvent.CapabilityName,
                    Success = capabilityEvent.Success ?? false,
                    Duration = capabilityEvent.Duration,
                    CorrelationId = capabilityEvent.TraceId,
                    Metadata = metadata
                });
            }
        }
    }

    private void AppendShellOperation(ShellResult result)
    {
        lock (_journalSync)
        {
            _operationJournal.Append(new SandboxOperationRecord
            {
                Timestamp = DateTime.UtcNow,
                Category = "shell",
                Operation = "execute",
                Target = _shell.CurrentDirectory,
                Success = result.Success,
                Duration = result.Duration,
                Metadata = new Dictionary<string, object?>
                {
                    ["command"] = result.Command
                },
                ShellResult = result
            });
        }
    }

    #endregion

    #region Sandbox Context Implementation

    private sealed class SandboxContext : ISandboxContext
    {
        private readonly IReadOnlyDictionary<Type, object> _capabilities;

        public SandboxContext(
            string sandboxId,
            SandboxOptions options,
            FileSystem.FileSystem fileSystem,
            SandboxShell shell,
            ISandboxEventEmitter eventEmitter,
            IReadOnlyDictionary<Type, object> capabilities)
        {
            SandboxId = sandboxId;
            Options = options;
            FileSystem = fileSystem;
            Shell = shell;
            EventEmitter = eventEmitter;
            Services = options.Services;
            _capabilities = capabilities;
        }

        public string SandboxId { get; }
        public SandboxOptions Options { get; }
        public IFileSystem FileSystem { get; }
        public ISandboxShellHost Shell { get; }
        public ISandboxEventEmitter EventEmitter { get; }
        public IServiceProvider? Services { get; }

        public TCapability GetCapability<TCapability>() where TCapability : class
        {
            if (TryGetCapability<TCapability>(out var capability))
            {
                return capability!;
            }

            throw new InvalidOperationException($"Capability '{typeof(TCapability).Name}' is not registered.");
        }

        public bool TryGetCapability<TCapability>(out TCapability? capability) where TCapability : class
        {
            if (_capabilities.TryGetValue(typeof(TCapability), out var instance))
            {
                capability = (TCapability)instance;
                return true;
            }

            capability = null;
            return false;
        }
    }

    #endregion
}
