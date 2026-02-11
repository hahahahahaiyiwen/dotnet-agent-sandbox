namespace AgentSandbox.Core.Skills;

/// <summary>
/// Configuration options for agent skills.
/// Skills are imported via FileImportOptions into the sandbox filesystem,
/// then discovered and loaded as SkillInfo objects.
/// </summary>
public class AgentSkillOptions
{
    /// <summary>
    /// Base path where skills are located after import. Default: /.sandbox/skills
    /// Skills should be imported to paths under this BasePath via FileImportOptions.
    /// </summary>
    public string BasePath { get; set; } = "/.sandbox/skills";
}

