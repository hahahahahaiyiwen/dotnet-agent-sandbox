using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Importing;
using AgentSandbox.Core.Skills;
using FsImpl = AgentSandbox.Core.FileSystem.FileSystem;

namespace AgentSandbox.Tests;

public class SkillManagerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LoadSkills_NullOrWhitespacePath_Throws(string? basePath)
    {
        var manager = new SkillManager(new FsImpl());

        Assert.Throws<ArgumentNullException>(() => manager.LoadSkills(basePath!));
    }

    [Fact]
    public void LoadSkills_MissingBasePath_ReturnsEmptyAndClearsLoadedCache()
    {
        var fs = new FsImpl();
        CreateSkill(fs, "/skills/demo", "demo", "Demo skill");
        var manager = new SkillManager(fs);

        var firstLoad = manager.LoadSkills("/skills");
        Assert.Single(firstLoad);

        var secondLoad = manager.LoadSkills("/missing");

        Assert.Empty(secondLoad);
        Assert.Empty(manager.GetSkills());
    }

    [Fact]
    public void LoadSkills_CacheSnapshot_RemainsStableAfterSubsequentReload()
    {
        var fs = new FsImpl();
        CreateSkill(fs, "/skills/demo", "demo", "Demo skill");
        var manager = new SkillManager(fs);

        var firstSnapshot = manager.LoadSkills("/skills");
        Assert.Single(firstSnapshot);

        manager.LoadSkills("/missing");

        Assert.Single(firstSnapshot);
        Assert.Equal("demo", firstSnapshot[0].Name);
        Assert.Empty(manager.GetSkills());
    }

    [Fact]
    public void LoadSkills_InvalidMetadata_ThrowsInvalidSkillException()
    {
        var fs = new FsImpl();
        fs.CreateDirectory("/skills/bad");
        fs.WriteFile("/skills/bad/SKILL.md", "---\nname: bad\n---\n");
        var manager = new SkillManager(fs);

        Assert.Throws<InvalidSkillException>(() => manager.LoadSkills("/skills"));
    }

    [Fact]
    public void LoadSkills_InvalidMetadata_ClearsLoadedCacheAfterPreviousSuccess()
    {
        var fs = new FsImpl();
        CreateSkill(fs, "/skills/good", "good", "Good skill");
        var manager = new SkillManager(fs);

        var firstLoad = manager.LoadSkills("/skills");
        Assert.Single(firstLoad);

        fs.CreateDirectory("/skills/bad");
        fs.WriteFile("/skills/bad/SKILL.md", "---\nname: bad\n---\n");

        Assert.Throws<InvalidSkillException>(() => manager.LoadSkills("/skills"));
        Assert.Empty(manager.GetSkills());
    }

    [Fact]
    public void LoadSkills_ReturnsSkillsInDeterministicDirectoryOrder()
    {
        var fs = new FsImpl();
        CreateSkill(fs, "/skills/zeta", "zeta", "Zeta skill");
        CreateSkill(fs, "/skills/alpha", "alpha", "Alpha skill");
        var manager = new SkillManager(fs);

        var loaded = manager.LoadSkills("/skills");

        Assert.Equal(["alpha", "zeta"], loaded.Select(skill => skill.Name).ToArray());
    }

    [Fact]
    public void LoadSkills_SkillReadFailure_WrapsInInvalidSkillException()
    {
        var fs = new FsImpl();
        fs.CreateDirectory("/skills/broken");
        fs.CreateDirectory("/skills/broken/SKILL.md");
        var manager = new SkillManager(fs);

        var ex = Assert.Throws<InvalidSkillException>(() => manager.LoadSkills("/skills"));
        Assert.Contains("Failed to load skill 'broken'", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void LoadSkills_DirectoryNotFoundDuringScan_ReturnsEmpty()
    {
        var fs = new FsImpl(new InconsistentExistsStorage("/skills"), options: null);
        var manager = new SkillManager(fs);

        var loaded = manager.LoadSkills("/skills");

        Assert.Empty(loaded);
        Assert.Empty(manager.GetSkills());
    }

    [Fact]
    public void GetSkillsDescription_NoSkills_ReturnsDefaultMessage()
    {
        var manager = new SkillManager(new FsImpl());

        var description = manager.GetSkillsDescription();

        Assert.Equal("No skills are currently available.", description);
    }

    [Fact]
    public void GetSkillsDescription_WithLoadedSkills_RendersExpectedXml()
    {
        var fs = new FsImpl();
        CreateSkill(fs, "/skills/demo", "demo", "Demo skill");
        var manager = new SkillManager(fs);
        manager.LoadSkills("/skills");

        var description = manager.GetSkillsDescription();

        Assert.Contains("<available_skills>", description, StringComparison.Ordinal);
        Assert.Contains("<name>demo</name>", description, StringComparison.Ordinal);
        Assert.Contains("<description>Demo skill</description>", description, StringComparison.Ordinal);
        Assert.Contains("<location>/skills/demo/SKILL.md</location>", description, StringComparison.Ordinal);
        Assert.Contains("</available_skills>", description, StringComparison.Ordinal);
    }

    private static void CreateSkill(FsImpl fs, string skillPath, string name, string description)
    {
        fs.CreateDirectory(skillPath);
        fs.WriteFile(
            $"{skillPath}/SKILL.md",
            $"---\nname: {name}\ndescription: {description}\n---\n\n# {name}\nUse this skill.");
    }

    private sealed class InconsistentExistsStorage : IFileStorage
    {
        private readonly HashSet<string> _exists;
        private readonly Dictionary<string, FileEntry> _entries = new(StringComparer.Ordinal);

        public InconsistentExistsStorage(params string[] existingPaths)
        {
            _exists = new HashSet<string>(existingPaths, StringComparer.Ordinal) { "/" };
        }

        public FileEntry? Get(string path) => _entries.TryGetValue(path, out var entry) ? entry : null;
        public void Set(string path, FileEntry entry) => _entries[path] = entry;
        public bool Delete(string path) => _entries.Remove(path);
        public bool Exists(string path) => _exists.Contains(path) || _entries.ContainsKey(path);
        public IEnumerable<string> GetAllPaths() => _entries.Keys;
        public IEnumerable<string> GetPathsByPrefix(string prefix) => _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal));
        public IEnumerable<string> GetChildren(string directoryPath) => Enumerable.Empty<string>();
        public void Clear() => _entries.Clear();
        public int Count => _entries.Count;
        public IEnumerable<KeyValuePair<string, FileEntry>> GetAll() => _entries;
        public void SetMany(IEnumerable<KeyValuePair<string, FileEntry>> entries)
        {
            foreach (var entry in entries)
            {
                _entries[entry.Key] = entry.Value;
            }
        }
    }
}
