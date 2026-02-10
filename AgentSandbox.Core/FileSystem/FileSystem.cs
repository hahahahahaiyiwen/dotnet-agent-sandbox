using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace AgentSandbox.Core.FileSystem;

/// <summary>
/// In-memory file system for agent sandboxing.
/// Thread-safe and serializable for snapshotting.
/// Implements IFileSystem, ISnapshotableFileSystem, and IFileSystemStats.
/// Uses IFileStorage for pluggable storage backends.
/// </summary>
public class FileSystem : IFileSystem, ISnapshotableFileSystem, IFileSystemStats
{
    private readonly IFileStorage _storage;
    private readonly FileSystemOptions _options;
    private readonly object _lock = new();

    /// <inheritdoc />
    public event EventHandler<FileSystemEventArgs>? Created;
    
    /// <inheritdoc />
    public event EventHandler<FileSystemEventArgs>? Changed;
    
    /// <inheritdoc />
    public event EventHandler<FileSystemEventArgs>? Deleted;
    
    /// <inheritdoc />
    public event EventHandler<FileSystemRenamedEventArgs>? Renamed;

    /// <summary>
    /// Creates a new FileSystem with in-memory storage.
    /// </summary>
    public FileSystem() : this(new InMemoryFileStorage(), null)
    {
    }

    /// <summary>
    /// Creates a new FileSystem with size limits.
    /// </summary>
    public FileSystem(FileSystemOptions? options) : this(new InMemoryFileStorage(), options)
    {
    }

    /// <summary>
    /// Creates a new FileSystem with the specified storage backend.
    /// </summary>
    /// <param name="storage">Storage backend for persisting file entries.</param>
    /// <param name="options">Optional size limit configuration.</param>
    public FileSystem(IFileStorage storage, FileSystemOptions? options = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options ?? new FileSystemOptions();
        
        // Initialize root directory if not exists
        if (!_storage.Exists("/"))
        {
            _storage.Set("/", new FileEntry
            {
                Path = "/",
                Name = "/",
                IsDirectory = true,
                Mode = 0755
            });
        }
    }

    #region IFileSystem - Path Operations
    
    /// <inheritdoc />
    public bool Exists(string path)
    {
        path = FileSystemPath.Normalize(path);
        return _storage.Exists(path);
    }
    
    /// <inheritdoc />
    public bool IsFile(string path)
    {
        path = FileSystemPath.Normalize(path);
        var entry = _storage.Get(path);
        return entry != null && !entry.IsDirectory;
    }

    /// <inheritdoc />
    public bool IsDirectory(string path)
    {
        path = FileSystemPath.Normalize(path);
        var entry = _storage.Get(path);
        return entry != null && entry.IsDirectory;
    }
    
    /// <inheritdoc />
    public FileEntry? GetEntry(string path)
    {
        path = FileSystemPath.Normalize(path);
        return _storage.Get(path);
    }

    #endregion

    #region IFileSystem - Directory Operations
    
    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
        path = FileSystemPath.Normalize(path);
        if (path == "/") return;

        lock (_lock)
        {
            var existing = _storage.Get(path);
            if (existing != null)
            {
                if (!existing.IsDirectory)
                    throw new InvalidOperationException($"Path exists as a file: {path}");
                return;
            }

            // Create parent directories
            var parent = FileSystemPath.GetParent(path);
            if (!Exists(parent))
            {
                CreateDirectory(parent);
            }

            _storage.Set(path, new FileEntry
            {
                Path = path,
                Name = FileSystemPath.GetName(path),
                IsDirectory = true,
                Mode = 0755
            });
            
            Created?.Invoke(this, new FileSystemEventArgs(path, isDirectory: true));
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> ListDirectory(string path)
    {
        path = FileSystemPath.Normalize(path);
        
        var info = _storage.Get(path);
        if (info == null)
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        
        if (!info.IsDirectory)
            throw new InvalidOperationException($"Not a directory: {path}");

        return _storage.GetChildren(path)
            .Select(childPath => FileSystemPath.GetName(childPath))
            .OrderBy(name => name);
    }

    #endregion

    #region IFileSystem - File Read Operations
    
    /// <inheritdoc />
    public byte[] ReadFile(string path)
    {
        path = FileSystemPath.Normalize(path);
        
        var entry = _storage.Get(path);
        if (entry == null)
            throw new FileNotFoundException($"File not found: {path}");
        
        if (entry.IsDirectory)
            throw new InvalidOperationException($"Cannot read directory: {path}");
        
        return entry.Content;
    }
    
    /// <inheritdoc />
    public string ReadFile(string path, Encoding encoding)
    {
        return encoding.GetString(ReadFile(path));
    }
    
    #endregion

    #region IFileSystem - File Write Operations
    
    /// <inheritdoc />
    public void WriteFile(string path, byte[] content)
    {
        path = FileSystemPath.Normalize(path);
        
        // Validate size limits
        ValidateFileSize(content.Length);
        
        lock (_lock)
        {
            var existing = _storage.Get(path);
            var existingSize = existing?.Content.Length ?? 0;
            var isNew = existing == null;
            ValidateTotalSize(content.Length - existingSize);
            
            if (!existing?.IsDirectory ?? true)
            {
                ValidateNodeCount(isNew);
            }
            
            var parent = FileSystemPath.GetParent(path);
            if (!Exists(parent))
            {
                CreateDirectory(parent);
            }

            existing = _storage.Get(path);
            if (existing != null)
            {
                if (existing.IsDirectory)
                    throw new InvalidOperationException($"Cannot write to directory: {path}");
                
                existing.Content = content;
                existing.ModifiedAt = DateTime.UtcNow;
                _storage.Set(path, existing);
                
                Changed?.Invoke(this, new FileSystemEventArgs(path, isDirectory: false));
            }
            else
            {
                _storage.Set(path, new FileEntry
                {
                    Path = path,
                    Name = FileSystemPath.GetName(path),
                    IsDirectory = false,
                    Content = content,
                    Mode = 0644
                });
                
                Created?.Invoke(this, new FileSystemEventArgs(path, isDirectory: false));
            }
        }
    }
    
    private void ValidateFileSize(long size)
    {
        if (_options.MaxFileSize.HasValue && size > _options.MaxFileSize.Value)
        {
            throw new InvalidOperationException(
                $"File size ({size} bytes) exceeds maximum allowed ({_options.MaxFileSize.Value} bytes)");
        }
    }
    
    private void ValidateTotalSize(long additionalSize)
    {
        if (_options.MaxTotalSize.HasValue && additionalSize > 0)
        {
            var currentTotal = TotalSize;
            if (currentTotal + additionalSize > _options.MaxTotalSize.Value)
            {
                throw new InvalidOperationException(
                    $"Total size would exceed maximum ({_options.MaxTotalSize.Value} bytes)");
            }
        }
    }
    
    private void ValidateNodeCount(bool isNewNode)
    {
        if (_options.MaxNodeCount.HasValue && isNewNode)
        {
            if (NodeCount >= _options.MaxNodeCount.Value)
            {
                throw new InvalidOperationException(
                    $"Node count would exceed maximum ({_options.MaxNodeCount.Value})");
            }
        }
    }

    /// <inheritdoc />
    public void WriteFile(string path, string content, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        WriteFile(path, encoding.GetBytes(content));
    }

    #endregion

    #region IFileSystem - Delete Operations
    
    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        path = FileSystemPath.Normalize(path);
        
        var info = _storage.Get(path);
        if (info == null)
            throw new FileNotFoundException($"File not found: {path}");
        
        if (info.IsDirectory)
            throw new InvalidOperationException($"Path is a directory, use DeleteDirectory: {path}");
        
        _storage.Delete(path);
        
        Deleted?.Invoke(this, new FileSystemEventArgs(path, isDirectory: false));
    }
    
    /// <inheritdoc />
    public void DeleteDirectory(string path, bool recursive = false)
    {
        path = FileSystemPath.Normalize(path);
        
        if (path == "/")
            throw new InvalidOperationException("Cannot delete root directory");
        
        var info = _storage.Get(path);
        if (info == null)
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        
        if (!info.IsDirectory)
            throw new InvalidOperationException($"Path is not a directory: {path}");
        
        lock (_lock)
        {
            var children = ListDirectory(path).ToList();
            if (children.Count > 0 && !recursive)
                throw new InvalidOperationException($"Directory not empty: {path}");

            if (recursive)
            {
                foreach (var child in children)
                {
                    Delete(FileSystemPath.Combine(path, child), recursive: true);
                }
            }

            _storage.Delete(path);
            
            Deleted?.Invoke(this, new FileSystemEventArgs(path, isDirectory: true));
        }
    }

    /// <inheritdoc />
    public void Delete(string path, bool recursive = false)
    {
        path = FileSystemPath.Normalize(path);
        
        if (path == "/")
            throw new InvalidOperationException("Cannot delete root directory");

        lock (_lock)
        {
            var info = _storage.Get(path);
            if (info == null)
                throw new FileNotFoundException($"Path not found: {path}");

            if (info.IsDirectory)
            {
                DeleteDirectory(path, recursive);
            }
            else
            {
                DeleteFile(path);
            }
        }
    }

    #endregion

    #region IFileSystem - Copy/Move Operations

    /// <inheritdoc />
    public void Copy(string source, string destination, bool overwrite = false)
    {
        source = FileSystemPath.Normalize(source);
        destination = FileSystemPath.Normalize(destination);

        var entry = _storage.Get(source);
        if (entry == null)
            throw new FileNotFoundException($"Source not found: {source}");
        
        if (!overwrite && Exists(destination))
            throw new InvalidOperationException($"Destination already exists: {destination}");

        lock (_lock)
        {
            if (entry.IsDirectory)
            {
                CreateDirectory(destination);
                foreach (var child in ListDirectory(source))
                {
                    Copy(FileSystemPath.Combine(source, child), FileSystemPath.Combine(destination, child), overwrite);
                }
            }
            else
            {
                WriteFile(destination, entry.Content);
            }
        }
    }

    /// <inheritdoc />
    public void Move(string source, string destination, bool overwrite = false)
    {
        source = FileSystemPath.Normalize(source);
        destination = FileSystemPath.Normalize(destination);
        
        var sourceEntry = _storage.Get(source);
        var isDirectory = sourceEntry?.IsDirectory ?? false;
        
        Copy(source, destination, overwrite);
        Delete(source, recursive: true);
        
        Renamed?.Invoke(this, new FileSystemRenamedEventArgs(source, destination, isDirectory));
    }

    #endregion

    #region ISnapshotableFileSystem
    
    /// <inheritdoc />
    public byte[] CreateSnapshot()
    {
        if (_storage is ISerializableFileStorage serializable)
        {
            return serializable.Serialize();
        }
        
        // Fallback: serialize via GetAll with GZip compression
        var snapshot = _storage.GetAll()
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new FileEntry
                {
                    Name = kvp.Value.Name,
                    Path = kvp.Value.Path,
                    IsDirectory = kvp.Value.IsDirectory,
                    Content = kvp.Value.Content,
                    CreatedAt = kvp.Value.CreatedAt,
                    ModifiedAt = kvp.Value.ModifiedAt,
                    Mode = kvp.Value.Mode
                });
        
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(json);
        }
        return output.ToArray();
    }
    
    /// <inheritdoc />
    public void RestoreSnapshot(byte[] snapshotData)
    {
        if (_storage is ISerializableFileStorage serializable)
        {
            serializable.Deserialize(snapshotData);
            return;
        }
        
        // Fallback: decompress and deserialize
        using var input = new MemoryStream(snapshotData);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        var json = output.ToArray();

        var snapshot = JsonSerializer.Deserialize<Dictionary<string, FileEntry>>(json);
        if (snapshot == null) return;

        lock (_lock)
        {
            _storage.Clear();
            foreach (var kvp in snapshot)
            {
                _storage.Set(kvp.Key, kvp.Value);
            }
        }
    }

    #endregion

    #region IFileSystemStats
    
    /// <inheritdoc />
    public long TotalSize
    {
        get
        {
            long total = 0;
            foreach (var kvp in _storage.GetAll())
            {
                if (!kvp.Value.IsDirectory)
                {
                    total += kvp.Value.Content.Length;
                }
            }
            return total;
        }
    }
    
    /// <inheritdoc />
    public int FileCount => _storage.GetAll().Count(kvp => !kvp.Value.IsDirectory);
    
    /// <inheritdoc />
    public int DirectoryCount => _storage.GetAll().Count(kvp => kvp.Value.IsDirectory);
    
    /// <inheritdoc />
    public int NodeCount => _storage.Count;

    #endregion
}