using AgentSandbox.Core.Importing;
using AgentSandbox.Core.Skills;

namespace AgentSandbox.Tests;

public class AgentSkillTests
{
    private static readonly string TestSkillsPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "TestSkills");

    [Fact]
    public void FromAssembly_CreatesEmbeddedSource_AndPreservesName()
    {
        var skill = AgentSkill.FromAssembly(typeof(AgentSkillTests).Assembly, "AgentSandbox.Tests.TestResources.Embedded.Valid", "embedded-skill");

        Assert.Equal("embedded-skill", skill.Name);
        Assert.IsType<EmbeddedSource>(skill.Source);
    }

    [Fact]
    public void FromSource_UsesProvidedSource_AndPreservesName()
    {
        var source = new InMemorySource().AddFile("SKILL.md", "---\nname: source-test\ndescription: Source test\n---\n");

        var skill = AgentSkill.FromSource(source, "from-source");

        Assert.Equal("from-source", skill.Name);
        Assert.Same(source, skill.Source);
    }

    [Fact]
    public void FromPath_NullOrWhitespacePath_ThrowsExpectedArgumentExceptions()
    {
        Assert.Throws<ArgumentNullException>(() => AgentSkill.FromPath(null!));
        Assert.Throws<ArgumentException>(() => AgentSkill.FromPath(""));
        Assert.Throws<ArgumentException>(() => AgentSkill.FromPath("   "));
    }

    [Fact]
    public void FromPath_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var missingPath = Path.Combine(TestSkillsPath, "does-not-exist");

        Assert.Throws<DirectoryNotFoundException>(() => AgentSkill.FromPath(missingPath));
    }

    [Fact]
    public void FromAssembly_NullAssembly_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AgentSkill.FromAssembly(null!, "prefix"));
    }

    [Fact]
    public void FromAssembly_NullOrWhitespacePrefix_ThrowsExpectedArgumentExceptions()
    {
        var assembly = typeof(AgentSkillTests).Assembly;

        Assert.Throws<ArgumentNullException>(() => AgentSkill.FromAssembly(assembly, null!));
        Assert.Throws<ArgumentException>(() => AgentSkill.FromAssembly(assembly, ""));
        Assert.Throws<ArgumentException>(() => AgentSkill.FromAssembly(assembly, "   "));
    }

    [Fact]
    public void FromFiles_NullFiles_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AgentSkill.FromFiles(null!));
    }

    [Fact]
    public void FromSource_NullSource_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AgentSkill.FromSource(null!));
    }
}
