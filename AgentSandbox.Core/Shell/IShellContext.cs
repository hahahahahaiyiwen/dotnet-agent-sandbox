using AgentSandbox.Core.FileSystem;

namespace AgentSandbox.Core.Shell;

/// <summary>
/// Provides context for shell command execution.
/// </summary>
public interface IShellContext
{
    /// <summary>
    /// The virtual filesystem.
    /// </summary>
    IFileSystem FileSystem { get; }

    /// <summary>
    /// Current working directory.
    /// </summary>
    string CurrentDirectory { get; set; }

    /// <summary>
    /// Environment variables.
    /// </summary>
    IDictionary<string, string> Environment { get; }

    /// <summary>
    /// Resolves a path relative to the current directory.
    /// </summary>
    string ResolvePath(string path);

    /// <summary>
    /// Gets or creates a cached value scoped to this session.
    /// Use for expensive objects like compiled regex patterns.
    /// Cache is cleared when the sandbox is disposed.
    /// </summary>
    /// <typeparam name="T">Type of the cached value.</typeparam>
    /// <param name="key">Cache key (should fully identify the value, e.g., (pattern, options) tuple).</param>
    /// <param name="factory">Factory function to create the value if not cached.</param>
    /// <returns>The cached or newly created value.</returns>
    T GetOrCreate<T>(object key, Func<T> factory);
}
