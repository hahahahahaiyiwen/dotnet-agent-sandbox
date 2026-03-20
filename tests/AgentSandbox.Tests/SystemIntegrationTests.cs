using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Security;
using AgentSandbox.Core.Shell;
using System.Text.RegularExpressions;

namespace AgentSandbox.Tests;

public class SystemIntegrationTests
{
    [Fact]
    public void Move_Directory_WhenDeletePhaseThrows_ShouldRemainAtomic()
    {
        var fs = new FileSystem();
        fs.WriteFile("/src/a.txt", "A");
        fs.WriteFile("/src/b.txt", "B");

        fs.Deleted += (_, args) =>
        {
            if (args.Path == "/src/a.txt")
            {
                throw new InvalidOperationException("Injected delete-phase failure");
            }
        };

        Assert.Throws<InvalidOperationException>(() => fs.Move("/src", "/dest"));

        Assert.True(fs.Exists("/src/a.txt"));
        Assert.True(fs.Exists("/src/b.txt"));
        Assert.False(fs.Exists("/dest"));
    }

    [Theory]
    [InlineData("cat /missing.txt")]
    [InlineData("head /missing.txt")]
    [InlineData("tail /missing.txt")]
    [InlineData("wc /missing.txt")]
    public void ReadCommands_MissingFile_ShouldUseConsistentNoSuchFileContract(string commandLine)
    {
        var shell = new SandboxShell(new FileSystem());

        var result = shell.Execute(commandLine);

        Assert.False(result.Success);
        Assert.Contains("No such file or directory", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Wc_WhenFileMutatesMidExecution_ShouldUseSingleSnapshotForCounts()
    {
        var inner = new FileSystem();
        inner.WriteFile("/file.txt", "alpha beta");
        var shell = new SandboxShell(new MutatingReadFileSystem(
            inner,
            "/file.txt",
            "alpha\nbeta\ngamma"));

        var result = shell.Execute("wc /file.txt");

        Assert.True(result.Success);

        var match = Regex.Match(
            result.Stdout,
            @"^\s*(\d+)\s+(\d+)\s+(\d+)\s+/file\.txt$",
            RegexOptions.Multiline);

        Assert.True(match.Success, $"Unexpected wc output: {result.Stdout}");
        Assert.Equal(1, int.Parse(match.Groups[1].Value));
        Assert.Equal(2, int.Parse(match.Groups[2].Value));
        Assert.Equal(10, int.Parse(match.Groups[3].Value));
    }

    private sealed class MutatingReadFileSystem : IFileSystem
    {
        private readonly IFileSystem _inner;
        private readonly string _targetPath;
        private readonly string _replacementContent;
        private bool _mutated;

        public MutatingReadFileSystem(IFileSystem inner, string targetPath, string replacementContent)
        {
            _inner = inner;
            _targetPath = targetPath;
            _replacementContent = replacementContent;
        }

        public bool Exists(string path) => _inner.Exists(path);
        public bool IsFile(string path) => _inner.IsFile(path);
        public bool IsDirectory(string path) => _inner.IsDirectory(path);
        public FileEntry? GetEntry(string path) => _inner.GetEntry(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public IEnumerable<string> ListDirectory(string path) => _inner.ListDirectory(path);
        public byte[] ReadFileBytes(string path) => _inner.ReadFileBytes(path);
        public string ReadFile(string path) => _inner.ReadFile(path);
        public void WriteFile(string path, byte[] content) => _inner.WriteFile(path, content);
        public void WriteFile(string path, string content) => _inner.WriteFile(path, content);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void DeleteDirectory(string path, bool recursive = false) => _inner.DeleteDirectory(path, recursive);
        public void Delete(string path, bool recursive = false) => _inner.Delete(path, recursive);
        public void Copy(string source, string destination, bool overwrite = false) => _inner.Copy(source, destination, overwrite);
        public void Move(string source, string destination, bool overwrite = false) => _inner.Move(source, destination, overwrite);

        public IEnumerable<string> ReadFileLines(string path, int? startLine = null, int? endLine = null)
        {
            if (!_mutated && string.Equals(path, _targetPath, StringComparison.Ordinal))
            {
                _inner.WriteFile(path, _replacementContent);
                _mutated = true;
            }

            return _inner.ReadFileLines(path, startLine, endLine);
        }
    }
}
