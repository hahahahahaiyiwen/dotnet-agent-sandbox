using System.Text;
using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Skills;

namespace AgentSandbox.Core.Importing;

/// <summary>
/// Discovers and loads agent skills from the virtual filesystem.
/// Skills must be pre-imported via FileImportManager before calling LoadSkills.
/// Scans skill directories for SKILL.md files, parses metadata, and caches SkillInfo objects.
/// </summary>
internal class SkillManager
{
    private readonly FileSystem.FileSystem _fileSystem;
    private IReadOnlyList<SkillInfo> _loadedSkills = Array.Empty<SkillInfo>();

    public SkillManager(FileSystem.FileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Discovers and loads all skills in the skill base path.
    /// Each subdirectory under basePath containing SKILL.md becomes a loaded skill.
    /// Results are cached for subsequent calls to GetSkills() and GetSkillsDescription().
    /// </summary>
    /// <param name="basePath">The base path where skills are located (e.g., /.sandbox/skills).</param>
    /// <returns>List of discovered skill information.</returns>
    /// <exception cref="ArgumentNullException">BasePath is null or empty.</exception>
    /// <exception cref="InvalidSkillException">A skill is missing SKILL.md or metadata is invalid.</exception>
    public IReadOnlyList<SkillInfo> LoadSkills(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentNullException(nameof(basePath));

        // Ensure base path exists
        if (!_fileSystem.Exists(basePath))
        {
            _loadedSkills = Array.Empty<SkillInfo>();
            return _loadedSkills;
        }

        var skills = new List<SkillInfo>();

        // Scan subdirectories for skill definitions
        try
        {
            var entries = _fileSystem.ListDirectory(basePath);
            foreach (var entryName in entries)
            {
                var skillPath = $"{basePath}/{entryName}";
                
                // Check if this entry is a directory and contains SKILL.md
                if (_fileSystem.IsDirectory(skillPath))
                {
                    var skillMdPath = $"{skillPath}/SKILL.md";
                    if (_fileSystem.Exists(skillMdPath))
                    {
                        var skillInfo = LoadSkill(entryName, skillPath, skillMdPath);
                        if (skillInfo != null)
                        {
                            skills.Add(skillInfo);
                        }
                    }
                }
            }
        }
        catch (InvalidSkillException)
        {
            // Clear stale cache when discovery fails to avoid exposing outdated skills.
            _loadedSkills = Array.Empty<SkillInfo>();
            throw;
        }
        catch (DirectoryNotFoundException)
        {
            // BasePath doesn't exist, return empty list
            _loadedSkills = Array.Empty<SkillInfo>();
            return _loadedSkills;
        }

        // Atomically publish a new immutable snapshot for readers.
        var snapshot = skills.AsReadOnly();
        _loadedSkills = snapshot;
        return snapshot;
    }

    /// <summary>
    /// Loads a single skill from its SKILL.md file.
    /// </summary>
    private SkillInfo? LoadSkill(string skillName, string skillPath, string skillMdPath)
    {
        try
        {
            var skillMdContent = _fileSystem.ReadFile(skillMdPath);
            var metadata = SkillMetadata.Parse(skillMdContent);

            return new SkillInfo
            {
                Name = skillName,
                Description = metadata.Description,
                Path = skillPath,
                Metadata = metadata
            };
        }
        catch (InvalidSkillException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidSkillException($"Failed to load skill '{skillName}' from {skillPath}", ex);
        }
    }

    /// <summary>
    /// Gets information about all loaded skills.
    /// </summary>
    public IReadOnlyList<SkillInfo> GetSkills() => _loadedSkills;

    /// <summary>
    /// Gets a description of loaded skills for use in AI function descriptions.
    /// Uses the XML format recommended by agentskills.io specification.
    /// </summary>
    public string GetSkillsDescription()
    {
        var loadedSkills = _loadedSkills;

        if (loadedSkills.Count == 0)
        {
            return "No skills are currently available.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("<available_skills>");

        foreach (var skill in loadedSkills)
        {
            sb.AppendLine("  <skill>");
            sb.AppendLine($"    <name>{skill.Name}</name>");
            sb.AppendLine($"    <description>{skill.Description}</description>");
            sb.AppendLine($"    <location>{skill.Path}/SKILL.md</location>");
            sb.AppendLine("  </skill>");
        }

        sb.AppendLine("</available_skills>");

        return sb.ToString();
    }
}
