using AgentSandbox.Core;
using AgentSandbox.Core.Importing;
using AgentSandbox.Core.Skills;

namespace AgentSandbox.Tests;

public class AgentSkillsTests
{
    private static readonly string TestSkillsPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "TestSkills");

    [Fact]
    public void Sandbox_WithNoSkills_HasEmptyLoadedSkills()
    {
        var sandbox = new Sandbox();
        
        Assert.Empty(sandbox.GetSkills());
    }

    [Fact]
    public void Sandbox_LoadsSkillToVirtualFs()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions { Skills = [AgentSkill.FromPath(skillPath)] }
        };

        var sandbox = new Sandbox(options: options);

        // Verify skill is loaded
        var skills = sandbox.GetSkills();
        Assert.Single(skills);
        Assert.Equal("test-skill", skills[0].Name);
        Assert.Equal("/.sandbox/skills/test-skill", skills[0].Path);
    }

    [Fact]
    public void Sandbox_ParsesSkillMetadata()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions { Skills = [AgentSkill.FromPath(skillPath)] }
        };

        var sandbox = new Sandbox(options: options);

        var skill = sandbox.GetSkills()[0];
        Assert.NotNull(skill.Metadata);
        Assert.Equal("test-skill", skill.Metadata.Name);
        Assert.Equal("Test skill for unit tests", skill.Metadata.Description);
        Assert.NotNull(skill.Metadata.Instructions);
        Assert.Contains("When to use this skill", skill.Metadata.Instructions);
    }

    [Fact]
    public void Sandbox_UsesMetadataDescription()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions { Skills = [AgentSkill.FromPath(skillPath)] }
        };

        var sandbox = new Sandbox(options: options);

        var skill = sandbox.GetSkills()[0];
        Assert.Equal("Test skill for unit tests", skill.Description);
    }

    [Fact]
    public void Sandbox_CanReadSkillFiles()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions { Skills = [AgentSkill.FromPath(skillPath)] }
        };

        var sandbox = new Sandbox(options: options);

        // Read SKILL.md
        var result = sandbox.Execute("cat /.sandbox/skills/test-skill/SKILL.md");
        Assert.True(result.Success);
        Assert.Contains("Test Skill", result.Stdout);
        Assert.Contains("name: test-skill", result.Stdout);
    }

    [Fact]
    public void Sandbox_CanExecuteSkillScript()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions { Skills = [AgentSkill.FromPath(skillPath)] }
        };

        var sandbox = new Sandbox(options: options);

        var result = sandbox.Execute("sh /.sandbox/skills/test-skill/scripts/hello.sh");
        
        Assert.True(result.Success);
        Assert.Equal("Hello from skill", result.Stdout);
    }

    [Fact]
    public void Sandbox_CanListSkillDirectory()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions { Skills = [AgentSkill.FromPath(skillPath)] }
        };

        var sandbox = new Sandbox(options: options);

        var result = sandbox.Execute("ls /.sandbox/skills/test-skill");
        
        Assert.True(result.Success);
        Assert.Contains("SKILL.md", result.Stdout);
        Assert.Contains("scripts", result.Stdout);
    }

    [Fact]
    public void Sandbox_LoadsMultipleSkills()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        
        // Create a second skill using in-memory source
        var skill2 = AgentSkill.FromFiles(new Dictionary<string, string>
        {
            ["SKILL.md"] = "---\nname: temp-skill\ndescription: Temporary skill\n---\n\n# Temp Skill"
        });

        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions { Skills = [AgentSkill.FromPath(skillPath), skill2] }
        };

        var sandbox = new Sandbox(options: options);

        var skills = sandbox.GetSkills();
        Assert.Equal(2, skills.Count);
        Assert.Contains(skills, s => s.Name == "test-skill");
        Assert.Contains(skills, s => s.Name == "temp-skill");

        // Both should be accessible
        var result1 = sandbox.Execute("cat /.sandbox/skills/test-skill/SKILL.md");
        var result2 = sandbox.Execute("cat /.sandbox/skills/temp-skill/SKILL.md");
        Assert.True(result1.Success);
        Assert.True(result2.Success);
    }

    [Fact]
    public void Sandbox_CustomSkillsBasePath()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions
            {
                BasePath = "/custom/skills",
                Skills = [AgentSkill.FromPath(skillPath)]
            }
        };

        var sandbox = new Sandbox(options: options);

        var skill = sandbox.GetSkills()[0];
        Assert.Equal("/custom/skills/test-skill", skill.Path);

        var result = sandbox.Execute("cat /custom/skills/test-skill/SKILL.md");
        Assert.True(result.Success);
    }

    [Fact]
    public void SandboxOptions_Clone_IncludesSkills()
    {
        var skill1 = AgentSkill.FromFiles(new Dictionary<string, string>
        {
            ["SKILL.md"] = "---\nname: skill1\ndescription: Skill 1\n---\n"
        });
        var skill2 = AgentSkill.FromFiles(new Dictionary<string, string>
        {
            ["SKILL.md"] = "---\nname: skill2\ndescription: Skill 2\n---\n"
        });

        var options = new SandboxOptions
        {
            AgentSkills = new AgentSkillOptions
            {
                Skills = [skill1, skill2],
                BasePath = "/custom/path"
            }
        };

        var clone = options.Clone();

        Assert.Equal(2, clone.AgentSkills.Skills.Count);
        Assert.Equal("/custom/path", clone.AgentSkills.BasePath);
        Assert.NotSame(options.AgentSkills.Skills, clone.AgentSkills.Skills);
    }

    [Fact]
    public void AgentSkill_FromPath_CreatesFileSystemSource()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var skill = AgentSkill.FromPath(skillPath);

        Assert.IsType<FileSystemSource>(skill.Source);
    }

    [Fact]
    public void AgentSkill_FromFiles_CreatesInMemorySource()
    {
        var skill = AgentSkill.FromFiles(new Dictionary<string, string>
        {
            ["SKILL.md"] = "---\nname: test\ndescription: Test\n---\n"
        });

        Assert.IsType<InMemorySource>(skill.Source);
    }

    [Fact]
    public void AgentSkill_NameOverride_UsesProvidedName()
    {
        var skill = AgentSkill.FromFiles(new Dictionary<string, string>
        {
            ["SKILL.md"] = "---\nname: original-name\ndescription: Test\n---\n"
        }, name: "override-name");

        var options = new SandboxOptions { AgentSkills = new AgentSkillOptions { Skills = [skill] } };
        var sandbox = new Sandbox(options: options);

        var loaded = sandbox.GetSkills()[0];
        Assert.Equal("override-name", loaded.Name);
    }

    [Fact]
    public void SkillMetadata_Parse_ExtractsFrontmatter()
    {
        var content = "---\nname: my-skill\ndescription: My description\n---\n\n# Instructions\nDo this.";
        var metadata = SkillMetadata.Parse(content);

        Assert.Equal("my-skill", metadata.Name);
        Assert.Equal("My description", metadata.Description);
        Assert.Equal("# Instructions\nDo this.", metadata.Instructions);
    }

    [Fact]
    public void SkillMetadata_Parse_ThrowsOnMissingName()
    {
        var content = "---\ndescription: Test\n---\n";
        
        Assert.Throws<InvalidSkillException>(() => SkillMetadata.Parse(content));
    }

    [Fact]
    public void SkillMetadata_Parse_ThrowsOnMissingDescription()
    {
        var content = "---\nname: test\n---\n";
        
        Assert.Throws<InvalidSkillException>(() => SkillMetadata.Parse(content));
    }

    [Fact]
    public void SkillMetadata_Parse_ThrowsOnMissingFrontmatter()
    {
        var content = "# No frontmatter here";
        
        Assert.Throws<InvalidSkillException>(() => SkillMetadata.Parse(content));
    }

    [Fact]
    public void Sandbox_ThrowsOnMissingSkillMd()
    {
        var skill = AgentSkill.FromFiles(new Dictionary<string, string>
        {
            ["README.md"] = "No SKILL.md here"
        });

        var options = new SandboxOptions { AgentSkills = new AgentSkillOptions { Skills = [skill] } };
        
        Assert.Throws<InvalidSkillException>(() => new Sandbox(options: options));
    }

    [Fact]
    public void InMemorySource_FluentBuilder()
    {
        var source = new InMemorySource()
            .AddFile("SKILL.md", "---\nname: test\ndescription: Test\n---\n")
            .AddFile("scripts/run.sh", "echo hello");

        var files = source.GetFiles().ToList();
        
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.RelativePath == "SKILL.md");
        Assert.Contains(files, f => f.RelativePath == "scripts/run.sh");
    }

    [Fact]
    public void Sandbox_ImportFiles_ImportsFilesToPath()
    {
        var source = new InMemorySource()
            .AddFile("config.json", """{"key": "value"}""")
            .AddFile("data/users.csv", "id,name\n1,Alice");

        var options = new SandboxOptions
        {
            Imports = [new FileImportOptions { Path = "/data", Source = source }]
        };
        var sandbox = new Sandbox(options: options);

        var result = sandbox.Execute("cat /data/config.json");
        Assert.True(result.Success);
        Assert.Contains("key", result.Stdout);

        var result2 = sandbox.Execute("cat /data/data/users.csv");
        Assert.True(result2.Success);
        Assert.Contains("Alice", result2.Stdout);
    }

    [Fact]
    public void Sandbox_ImportFiles_FromFileSystem()
    {
        var skillPath = Path.Combine(TestSkillsPath, "test-skill");
        var options = new SandboxOptions
        {
            Imports = [new FileImportOptions { Path = "/imported", Source = new FileSystemSource(skillPath) }]
        };
        var sandbox = new Sandbox(options: options);

        var result = sandbox.Execute("cat /imported/SKILL.md");
        Assert.True(result.Success);
        Assert.Contains("test-skill", result.Stdout);
    }

    [Fact]
    public void Sandbox_ImportFiles_NormalizesPath()
    {
        var source = new InMemorySource()
            .AddFile("test.txt", "hello");

        // Path without leading slash
        var options = new SandboxOptions
        {
            Imports = [new FileImportOptions { Path = "data", Source = source }]
        };
        var sandbox = new Sandbox(options: options);

        var result = sandbox.Execute("cat /data/test.txt");
        Assert.True(result.Success);
        Assert.Equal("hello", result.Stdout);
    }

    [Fact]
    public void SandboxOptions_Clone_IncludesImports()
    {
        var source = new InMemorySource().AddFile("test.txt", "hello");
        var options = new SandboxOptions
        {
            Imports = [new FileImportOptions { Path = "/data", Source = source }]
        };

        var clone = options.Clone();

        Assert.Single(clone.Imports);
        Assert.Equal("/data", clone.Imports[0].Path);
        Assert.NotSame(options.Imports, clone.Imports);
    }
}
