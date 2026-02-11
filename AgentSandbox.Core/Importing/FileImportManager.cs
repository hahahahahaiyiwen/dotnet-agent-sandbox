using AgentSandbox.Core.FileSystem;

namespace AgentSandbox.Core.Importing;

/// <summary>
/// Manages all file imports into the sandbox from various sources.
/// Provides unified copying of files into the virtual filesystem.
/// Skills are imported as regular files, then discovered via SkillManager.
/// </summary>
internal class FileImportManager
{
    private readonly FileSystem.FileSystem _fileSystem;

    public FileImportManager(FileSystem.FileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Imports files from a source to a destination path in the sandbox.
    /// Supports generic files, skills, models, datasets, or any other file-based imports.
    /// </summary>
    /// <param name="path">The destination path in the sandbox.</param>
    /// <param name="source">The file source (filesystem, embedded, in-memory, etc.).</param>
    /// <exception cref="ArgumentNullException">Path or source is null.</exception>
    public void Import(string path, IFileSource source)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentNullException(nameof(path));
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        var files = source.GetFiles().ToList();
        CopyFiles(path, files);
    }

    /// <summary>
    /// Copies files from a collection to a destination directory in the virtual filesystem.
    /// Creates parent directories as needed.
    /// </summary>
    internal void CopyFiles(string destPath, IReadOnlyList<FileData> files)
    {
        // Normalize path
        if (!destPath.StartsWith("/"))
        {
            destPath = "/" + destPath;
        }

        // Create destination directory
        _fileSystem.CreateDirectory(destPath);

        // Copy all files to virtual filesystem
        foreach (var file in files)
        {
            var filePath = $"{destPath}/{file.RelativePath}";

            // Ensure parent directory exists
            var lastSlash = filePath.LastIndexOf('/');
            if (lastSlash > 0)
            {
                var parentDir = filePath[..lastSlash];
                if (parentDir != destPath && !_fileSystem.Exists(parentDir))
                {
                    _fileSystem.CreateDirectory(parentDir);
                }
            }

            _fileSystem.WriteFile(filePath, file.Content);
        }
    }
}


