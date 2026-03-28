using AgentSandbox.Core.Importing;
using System.Reflection;

namespace AgentSandbox.Core.Skills;

/// <summary>
/// Represents an agent skill to be loaded into the sandbox filesystem.
/// Skills are folders containing SKILL.md (required), scripts/, references/, and assets/.
/// </summary>
public class AgentSkill
{
    /// <summary>
    /// Unique name for the skill. Used as the folder name under /.sandbox/skills/
    /// If not provided, will be extracted from SKILL.md frontmatter.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Source for loading skill files.
    /// </summary>
    public required IFileSource Source { get; init; }

    /// <summary>
    /// Creates a skill from a local filesystem path.
    /// </summary>
    /// <param name="path">Path to the skill folder containing SKILL.md.</param>
    /// <param name="name">Optional name override. If not provided, uses name from SKILL.md.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the directory at <paramref name="path"/> does not exist.</exception>
    public static AgentSkill FromPath(string path, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new AgentSkill
        {
            Name = name,
            Source = new FileSystemSource(path)
        };
    }

    /// <summary>
    /// Creates a skill from embedded assembly resources.
    /// </summary>
    /// <param name="assembly">The assembly containing embedded resources.</param>
    /// <param name="resourcePrefix">
    /// The resource name prefix (e.g., "MyApp.Skills.PythonDev").
    /// Resources should be embedded with names like "MyApp.Skills.PythonDev.SKILL.md".
    /// </param>
    /// <param name="name">Optional name override. If not provided, uses name from SKILL.md.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly"/> or <paramref name="resourcePrefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resourcePrefix"/> is empty or whitespace.</exception>
    public static AgentSkill FromAssembly(Assembly assembly, string resourcePrefix, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);

        return new AgentSkill
        {
            Name = name,
            Source = new EmbeddedSource(assembly, resourcePrefix)
        };
    }

    /// <summary>
    /// Creates a skill from in-memory files. Useful for testing.
    /// </summary>
    /// <param name="files">Dictionary of relative path to file content.</param>
    /// <param name="name">Optional name override. If not provided, uses name from SKILL.md.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="files"/> is <see langword="null"/>.</exception>
    public static AgentSkill FromFiles(IDictionary<string, string> files, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        return new AgentSkill
        {
            Name = name,
            Source = new InMemorySource(files)
        };
    }

    /// <summary>
    /// Creates a skill from an IFileSource. Useful for fluent building.
    /// </summary>
    /// <param name="source">The file source.</param>
    /// <param name="name">Optional name override. If not provided, uses name from SKILL.md.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    public static AgentSkill FromSource(IFileSource source, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new AgentSkill
        {
            Name = name,
            Source = source
        };
    }
}
