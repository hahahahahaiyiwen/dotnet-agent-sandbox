using System.Reflection;
using AgentSandbox.Core;
using AgentSandbox.Core.Importing;
using AgentSandbox.Core.Skills;

namespace AgentSandbox.Tests;

public class EmbeddedSourceTests
{
    private const string ValidPrefix = "AgentSandbox.Tests.TestResources.Embedded.Valid";
    private const string InvalidPrefix = "AgentSandbox.Tests.TestResources.Embedded.Invalid";

    [Fact]
    public void GetFiles_WithMatchingResources_ReturnsExpectedRelativePathsAndContent()
    {
        var source = new EmbeddedSource(typeof(EmbeddedSourceTests).Assembly, ValidPrefix);

        var files = source.GetFiles().OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();

        Assert.Equal(5, files.Count);
        Assert.Equal(
            ["SKILL.md", "backups/archive.tar.gz", "references/docs/notes.md", "scripts/setup.sh", "types/index.d.ts"],
            files.Select(f => f.RelativePath).ToArray());

        var contentByPath = files.ToDictionary(f => f.RelativePath, f => f.GetContentAsString(), StringComparer.Ordinal);
        Assert.Contains("name: embedded-valid", contentByPath["SKILL.md"], StringComparison.Ordinal);
        Assert.Contains("archive-bytes", contentByPath["backups/archive.tar.gz"], StringComparison.Ordinal);
        Assert.Contains("echo embedded setup", contentByPath["scripts/setup.sh"], StringComparison.Ordinal);
        Assert.Contains("Embedded notes", contentByPath["references/docs/notes.md"], StringComparison.Ordinal);
        Assert.Contains("export type Item", contentByPath["types/index.d.ts"], StringComparison.Ordinal);
    }

    [Fact]
    public void GetFiles_PrefixWithTrailingDot_LoadsSameResources()
    {
        var source = new EmbeddedSource(typeof(EmbeddedSourceTests).Assembly, ValidPrefix + ".");

        var files = source.GetFiles().ToList();

        Assert.Equal(5, files.Count);
    }

    [Fact]
    public void GetFiles_NoMatchingResources_ReturnsEmpty()
    {
        var source = new EmbeddedSource(typeof(EmbeddedSourceTests).Assembly, "AgentSandbox.Tests.TestResources.Embedded.Missing");

        var files = source.GetFiles().ToList();

        Assert.Empty(files);
    }

    [Fact]
    public void Sandbox_InvalidEmbeddedSkillMetadata_ThrowsInvalidSkillException()
    {
        var options = new SandboxOptions
        {
            Imports =
            [
                new FileImportOptions
                {
                    Path = "/.sandbox/skills/invalid-embedded",
                    Source = new EmbeddedSource(typeof(EmbeddedSourceTests).Assembly, InvalidPrefix)
                }
            ],
            AgentSkills = new AgentSkillOptions { BasePath = "/.sandbox/skills" }
        };

        Assert.Throws<InvalidSkillException>(() => new Sandbox(options: options));
    }
}
