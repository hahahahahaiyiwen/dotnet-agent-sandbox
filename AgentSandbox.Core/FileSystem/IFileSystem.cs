namespace AgentSandbox.Core.FileSystem;

/// <summary>
/// Core filesystem operations interface.
/// Implementations can be in-memory, disk-backed, remote, etc.
/// </summary>
public interface IFileSystem
{
    #region Path Operations
    
    /// <summary>
    /// Checks if a path exists (file or directory).
    /// </summary>
    bool Exists(string path);
    
    /// <summary>
    /// Checks if a path exists and is a file.
    /// </summary>
    bool IsFile(string path);
    
    /// <summary>
    /// Checks if a path exists and is a directory.
    /// </summary>
    bool IsDirectory(string path);
    
    /// <summary>
    /// Gets the file entry (metadata and content) for a path.
    /// Returns null if path does not exist.
    /// </summary>
    FileEntry? GetEntry(string path);
    
    #endregion
    
    #region Directory Operations
    
    /// <summary>
    /// Creates a directory. Creates parent directories if they don't exist.
    /// </summary>
    /// <param name="path">Path to the directory to create.</param>
    /// <exception cref="InvalidOperationException">If path exists as a file.</exception>
    void CreateDirectory(string path);
    
    /// <summary>
    /// Lists the names of immediate children in a directory.
    /// </summary>
    /// <param name="path">Path to the directory.</param>
    /// <returns>Names of files and subdirectories (not full paths).</returns>
    /// <exception cref="DirectoryNotFoundException">If directory does not exist.</exception>
    /// <exception cref="InvalidOperationException">If path is not a directory.</exception>
    IEnumerable<string> ListDirectory(string path);
    
    #endregion
    
    #region File Read Operations
    
    /// <summary>
    /// Reads the entire contents of a file as bytes (raw, not decoded).
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <returns>File contents as byte array (UTF-8 encoded).</returns>
    /// <exception cref="FileNotFoundException">If file does not exist.</exception>
    /// <exception cref="InvalidOperationException">If path is a directory.</exception>
    byte[] ReadFileBytes(string path);

    /// <summary>
    /// Reads the entire contents of a file as UTF-8 text.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <returns>File contents as UTF-8 string, with normalized line endings (line ending removed if file ends with newline).</returns>
    /// <exception cref="FileNotFoundException">If file does not exist.</exception>
    /// <exception cref="InvalidOperationException">If path is a directory.</exception>
    string ReadFile(string path);

    /// <summary>
    /// Reads file lines within a range as a lazy-evaluated stream.
    /// Uses incremental line boundary detection - only bytes up to the requested range are processed.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="startLine">Starting line number (1-indexed), inclusive. If null, defaults to 1.</param>
    /// <param name="endLine">Ending line number (1-indexed), exclusive. If null, reads to end of file.</param>
    /// <returns>Enumerable of lines within the requested range, each line as a UTF-8 string with normalized line endings.</returns>
    /// <remarks>
    /// - Line numbers are 1-indexed (first line = 1)
    /// - endLine is exclusive, like array ranges (startLine=1, endLine=4 means lines 1, 2, 3)
    /// - If startLine > file line count, returns empty enumeration (no lines yielded)
    /// - Line endings (CRLF, LF, CR) are normalized to LF (\n) within each line
    /// - Lazy evaluation: enumeration stops as soon as endLine is reached
    /// </remarks>
    /// <exception cref="FileNotFoundException">If file does not exist.</exception>
    /// <exception cref="InvalidOperationException">If path is a directory.</exception>
    IEnumerable<string> ReadFileLines(string path, int? startLine = null, int? endLine = null);
    
    #endregion
    
    #region File Write Operations
    
    /// <summary>
    /// Writes UTF-8 encoded bytes to a file, creating it if it doesn't exist, overwriting if it does.
    /// Creates parent directories as needed.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="content">Content to write (must be valid UTF-8 encoded bytes).</param>
    /// <exception cref="InvalidOperationException">If path is a directory or content is not valid UTF-8.</exception>
    void WriteFile(string path, byte[] content);
    
    /// <summary>
    /// Writes text to a file as UTF-8 encoded bytes, creating it if it doesn't exist, overwriting if it does.
    /// Creates parent directories as needed.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="content">Content to write.</param>
    /// <exception cref="InvalidOperationException">If path is a directory or content is not valid UTF-8.</exception>
    void WriteFile(string path, string content);
    
    #endregion
    
    #region Delete Operations
    
    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <exception cref="FileNotFoundException">If file does not exist.</exception>
    /// <exception cref="InvalidOperationException">If path is a directory.</exception>
    void DeleteFile(string path);
    
    /// <summary>
    /// Deletes a directory.
    /// </summary>
    /// <param name="path">Path to the directory.</param>
    /// <param name="recursive">If true, delete contents recursively; otherwise fail if not empty.</param>
    /// <exception cref="DirectoryNotFoundException">If directory does not exist.</exception>
    /// <exception cref="InvalidOperationException">If directory is not empty and recursive is false.</exception>
    void DeleteDirectory(string path, bool recursive = false);
    
    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    /// <param name="path">Path to delete.</param>
    /// <param name="recursive">If true and path is a directory, delete recursively.</param>
    /// <exception cref="FileNotFoundException">If path does not exist.</exception>
    void Delete(string path, bool recursive = false);
    
    #endregion
    
    #region Copy/Move Operations
    
    /// <summary>
    /// Copies a file or directory to a new location.
    /// </summary>
    /// <param name="source">Source path.</param>
    /// <param name="destination">Destination path.</param>
    /// <param name="overwrite">If true, overwrite existing destination.</param>
    /// <exception cref="FileNotFoundException">If source does not exist.</exception>
    /// <exception cref="InvalidOperationException">If destination exists and overwrite is false.</exception>
    void Copy(string source, string destination, bool overwrite = false);
    
    /// <summary>
    /// Moves or renames a file or directory.
    /// </summary>
    /// <param name="source">Source path.</param>
    /// <param name="destination">Destination path.</param>
    /// <param name="overwrite">If true, overwrite existing destination.</param>
    /// <exception cref="FileNotFoundException">If source does not exist.</exception>
    /// <exception cref="InvalidOperationException">If destination exists and overwrite is false.</exception>
    void Move(string source, string destination, bool overwrite = false);
    
    #endregion
}

/// <summary>
/// Extended filesystem interface with snapshot/restore capabilities.
/// </summary>
public interface ISnapshotableFileSystem : IFileSystem
{
    /// <summary>
    /// Creates a serialized snapshot of the entire filesystem state.
    /// </summary>
    /// <returns>Snapshot data that can be stored or transmitted.</returns>
    byte[] CreateSnapshot();
    
    /// <summary>
    /// Restores the filesystem to a previous state from a snapshot.
    /// </summary>
    /// <param name="snapshotData">Snapshot data from CreateSnapshot().</param>
    void RestoreSnapshot(byte[] snapshotData);
}

/// <summary>
/// Extended filesystem interface with statistics.
/// </summary>
public interface IFileSystemStats
{
    /// <summary>Total size of all files in bytes.</summary>
    long TotalSize { get; }
    
    /// <summary>Total number of files.</summary>
    int FileCount { get; }
    
    /// <summary>Total number of directories.</summary>
    int DirectoryCount { get; }
    
    /// <summary>Total number of nodes (files + directories).</summary>
    int NodeCount { get; }
}

/// <summary>
/// Event args for filesystem change events.
/// </summary>
public class FileSystemEventArgs : EventArgs
{
    public string Path { get; }
    public bool IsDirectory { get; }
    
    public FileSystemEventArgs(string path, bool isDirectory)
    {
        Path = path;
        IsDirectory = isDirectory;
    }
}

/// <summary>
/// Event args for rename/move events.
/// </summary>
public class FileSystemRenamedEventArgs : FileSystemEventArgs
{
    public string OldPath { get; }
    
    public FileSystemRenamedEventArgs(string oldPath, string newPath, bool isDirectory)
        : base(newPath, isDirectory)
    {
        OldPath = oldPath;
    }
}
