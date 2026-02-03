using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Shell;

namespace AgentSandbox.Tests;

public class SandboxShellTests
{
    private readonly FileSystem _fs;
    private readonly SandboxShell _shell;

    public SandboxShellTests()
    {
        _fs = new FileSystem();
        _shell = new SandboxShell(_fs);
    }

    [Fact]
    public void Pwd_ReturnsCurrentDirectory()
    {
        var result = _shell.Execute("pwd");
        
        Assert.True(result.Success);
        Assert.Equal("/", result.Stdout);
    }

    [Fact]
    public void Cd_ChangesDirectory()
    {
        _fs.CreateDirectory("/mydir");
        
        var result = _shell.Execute("cd /mydir");
        
        Assert.True(result.Success);
        Assert.Equal("/mydir", ((IShellContext)_shell).CurrentDirectory);
    }

    [Fact]
    public void Cd_FailsForNonexistentDirectory()
    {
        var result = _shell.Execute("cd /nonexistent");
        
        Assert.False(result.Success);
        Assert.Contains("No such file or directory", result.Stderr);
    }

    [Fact]
    public void Mkdir_CreatesDirectory()
    {
        var result = _shell.Execute("mkdir /newdir");
        
        Assert.True(result.Success);
        Assert.True(_fs.Exists("/newdir"));
        Assert.True(_fs.IsDirectory("/newdir"));
    }

    [Fact]
    public void Mkdir_WithP_CreatesParents()
    {
        var result = _shell.Execute("mkdir -p /a/b/c");
        
        Assert.True(result.Success);
        Assert.True(_fs.Exists("/a/b/c"));
    }

    [Fact]
    public void Touch_CreatesEmptyFile()
    {
        var result = _shell.Execute("touch /newfile.txt");
        
        Assert.True(result.Success);
        Assert.True(_fs.Exists("/newfile.txt"));
        Assert.Equal(0, _fs.ReadFile("/newfile.txt", System.Text.Encoding.UTF8).Length);
    }

    [Fact]
    public void Echo_PrintsText()
    {
        var result = _shell.Execute("echo Hello World");
        
        Assert.True(result.Success);
        Assert.Equal("Hello World", result.Stdout);
    }

    [Fact]
    public void Cat_PrintsFileContent()
    {
        _fs.WriteFile("/test.txt", "file content");
        
        var result = _shell.Execute("cat /test.txt");
        
        Assert.True(result.Success);
        Assert.Equal("file content", result.Stdout);
    }

    [Fact]
    public void Ls_ListsDirectoryContents()
    {
        _fs.WriteFile("/a.txt", "a");
        _fs.WriteFile("/b.txt", "b");
        
        var result = _shell.Execute("ls /");
        
        Assert.True(result.Success);
        Assert.Contains("a.txt", result.Stdout);
        Assert.Contains("b.txt", result.Stdout);
    }

    [Fact]
    public void Rm_RemovesFile()
    {
        _fs.WriteFile("/delete.txt", "x");
        
        var result = _shell.Execute("rm /delete.txt");
        
        Assert.True(result.Success);
        Assert.False(_fs.Exists("/delete.txt"));
    }

    [Fact]
    public void Rm_Rf_RemovesDirectoryRecursively()
    {
        _fs.WriteFile("/dir/sub/file.txt", "x");
        
        var result = _shell.Execute("rm -rf /dir");
        
        Assert.True(result.Success);
        Assert.False(_fs.Exists("/dir"));
    }

    [Fact]
    public void Cp_CopiesFile()
    {
        _fs.WriteFile("/source.txt", "content");
        
        var result = _shell.Execute("cp /source.txt /dest.txt");
        
        Assert.True(result.Success);
        Assert.True(_fs.Exists("/dest.txt"));
        Assert.Equal("content", _fs.ReadFile("/dest.txt", System.Text.Encoding.UTF8));
    }

    [Fact]
    public void Mv_MovesFile()
    {
        _fs.WriteFile("/old.txt", "content");
        
        var result = _shell.Execute("mv /old.txt /new.txt");
        
        Assert.True(result.Success);
        Assert.False(_fs.Exists("/old.txt"));
        Assert.True(_fs.Exists("/new.txt"));
    }

    [Fact]
    public void Head_ShowsFirstLines()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2\nline3\nline4\nline5");
        
        var result = _shell.Execute("head -n 2 /lines.txt");
        
        Assert.True(result.Success);
        Assert.Contains("line1", result.Stdout);
        Assert.Contains("line2", result.Stdout);
        Assert.DoesNotContain("line3", result.Stdout);
    }

    [Fact]
    public void Tail_ShowsLastLines()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2\nline3\nline4\nline5");
        
        var result = _shell.Execute("tail -n 2 /lines.txt");
        
        Assert.True(result.Success);
        Assert.Contains("line4", result.Stdout);
        Assert.Contains("line5", result.Stdout);
        Assert.DoesNotContain("line1", result.Stdout);
    }

    [Fact]
    public void Grep_FindsMatchingLines()
    {
        _fs.WriteFile("/search.txt", "apple\nbanana\napricot\ncherry");
        
        var result = _shell.Execute("grep ap /search.txt");
        
        Assert.True(result.Success);
        Assert.Contains("apple", result.Stdout);
        Assert.Contains("apricot", result.Stdout);
        Assert.DoesNotContain("banana", result.Stdout);
    }

    [Fact]
    public void Export_SetsEnvironmentVariable()
    {
        _shell.Execute("export MY_VAR=my_value");
        
        Assert.Equal("my_value", ((IShellContext)_shell).Environment["MY_VAR"]);
    }

    [Fact]
    public void VariableExpansion_Works()
    {
        _shell.Execute("export NAME=World");
        
        var result = _shell.Execute("echo Hello $NAME");
        
        Assert.Equal("Hello World", result.Stdout);
    }

    [Fact]
    public void RelativePaths_Work()
    {
        _fs.CreateDirectory("/home/user");
        _shell.Execute("cd /home/user");
        _shell.Execute("mkdir mydir");
        _shell.Execute("touch mydir/file.txt");
        
        Assert.True(_fs.Exists("/home/user/mydir"));
        Assert.True(_fs.Exists("/home/user/mydir/file.txt"));
    }

    [Fact]
    public void UnknownCommand_ReturnsError()
    {
        var result = _shell.Execute("unknowncommand");
        
        Assert.False(result.Success);
        Assert.Equal(127, result.ExitCode);
        Assert.Contains("command not found", result.Stderr);
    }

    [Fact]
    public void Help_ListsAvailableCommands()
    {
        var result = _shell.Execute("help");

        Assert.True(result.Success);
        Assert.Contains("pwd", result.Stdout);
        Assert.Contains("cd", result.Stdout);
        Assert.Contains("ls", result.Stdout);
        Assert.Contains("sh", result.Stdout);
        Assert.Contains("-h", result.Stdout); // Should mention -h for command help
    }

    #region Command Help Tests

    [Theory]
    [InlineData("pwd")]
    [InlineData("cd")]
    [InlineData("ls")]
    [InlineData("cat")]
    [InlineData("echo")]
    [InlineData("mkdir")]
    [InlineData("rm")]
    [InlineData("cp")]
    [InlineData("mv")]
    [InlineData("touch")]
    [InlineData("head")]
    [InlineData("tail")]
    [InlineData("wc")]
    [InlineData("grep")]
    [InlineData("find")]
    [InlineData("env")]
    [InlineData("export")]
    [InlineData("sh")]
    [InlineData("clear")]
    [InlineData("help")]
    public void BuiltinCommands_SupportHelpFlag(string command)
    {
        var result = _shell.Execute($"{command} -h");

        Assert.True(result.Success, $"{command} -h should succeed");
        Assert.False(string.IsNullOrEmpty(result.Stdout), $"{command} -h should return help text");
        Assert.Contains("Usage:", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LsHelp_ShowsOptions()
    {
        var result = _shell.Execute("ls -h");

        Assert.True(result.Success);
        Assert.Contains("-a", result.Stdout);
        Assert.Contains("-l", result.Stdout);
    }

    [Fact]
    public void GrepHelp_ShowsOptions()
    {
        var result = _shell.Execute("grep -h");

        Assert.True(result.Success);
        Assert.Contains("-i", result.Stdout);
        Assert.Contains("-n", result.Stdout);
    }

    #endregion

    #region sh Command Tests

    [Fact]
    public void Sh_ExecutesSimpleScript()
    {
        _fs.WriteFile("/script.sh", "echo Hello\necho World");
        
        var result = _shell.Execute("sh /script.sh");
        
        Assert.True(result.Success);
        Assert.Contains("Hello", result.Stdout);
        Assert.Contains("World", result.Stdout);
    }

    [Fact]
    public void Sh_SkipsComments()
    {
        _fs.WriteFile("/script.sh", "# This is a comment\necho Hello\n# Another comment");
        
        var result = _shell.Execute("sh /script.sh");
        
        Assert.True(result.Success);
        Assert.Equal("Hello", result.Stdout);
    }

    [Fact]
    public void Sh_SkipsShebang()
    {
        _fs.WriteFile("/script.sh", "#!/bin/bash\necho Hello");
        
        var result = _shell.Execute("sh /script.sh");
        
        Assert.True(result.Success);
        Assert.Equal("Hello", result.Stdout);
    }

    [Fact]
    public void Sh_SupportsPositionalParameters()
    {
        _fs.WriteFile("/script.sh", "echo $1 $2");
        
        var result = _shell.Execute("sh /script.sh foo bar");
        
        Assert.True(result.Success);
        Assert.Equal("foo bar", result.Stdout);
    }

    [Fact]
    public void Sh_SupportsAllArgsParameter()
    {
        _fs.WriteFile("/script.sh", "echo $@");
        
        var result = _shell.Execute("sh /script.sh one two three");
        
        Assert.True(result.Success);
        Assert.Equal("one two three", result.Stdout);
    }

    [Fact]
    public void Sh_SupportsArgCountParameter()
    {
        _fs.WriteFile("/script.sh", "echo $#");
        
        var result = _shell.Execute("sh /script.sh a b c");
        
        Assert.True(result.Success);
        Assert.Equal("3", result.Stdout);
    }

    [Fact]
    public void Sh_StopsOnError()
    {
        _fs.WriteFile("/script.sh", "echo First\ncat /nonexistent\necho Second");
        
        var result = _shell.Execute("sh /script.sh");
        
        Assert.False(result.Success);
        Assert.Contains("First", result.Stdout);
        Assert.DoesNotContain("Second", result.Stdout);
    }

    [Fact]
    public void Sh_FileNotFound()
    {
        var result = _shell.Execute("sh /nonexistent.sh");
        
        Assert.False(result.Success);
        Assert.Contains("No such file", result.Stderr);
    }

    [Fact]
    public void Sh_MissingPath()
    {
        var result = _shell.Execute("sh");
        
        Assert.False(result.Success);
        Assert.Contains("missing script path", result.Stderr);
    }

    [Fact]
    public void Sh_ExecutesFileOperations()
    {
        _fs.WriteFile("/script.sh", "mkdir /testdir\ntouch /testdir/file.txt\necho done");
        
        var result = _shell.Execute("sh /script.sh");
        
        Assert.True(result.Success);
        Assert.True(_fs.Exists("/testdir"));
        Assert.True(_fs.Exists("/testdir/file.txt"));
    }

    [Fact]
    public void Sh_DirectExecution_WithDotSlash()
    {
        _fs.WriteFile("/script.sh", "echo Direct");
        
        var result = _shell.Execute("./script.sh");
        
        Assert.True(result.Success);
        Assert.Equal("Direct", result.Stdout);
    }

    [Fact]
    public void Sh_DirectExecution_WithAbsolutePath()
    {
        _fs.CreateDirectory("/scripts");
        _fs.WriteFile("/scripts/test.sh", "echo Absolute");
        
        var result = _shell.Execute("/scripts/test.sh");
        
        Assert.True(result.Success);
        Assert.Equal("Absolute", result.Stdout);
    }

    [Fact]
    public void Sh_DirectExecution_WithArgs()
    {
        _fs.WriteFile("/script.sh", "echo $1");
        
        var result = _shell.Execute("./script.sh myarg");
        
        Assert.True(result.Success);
        Assert.Equal("myarg", result.Stdout);
    }

    [Fact]
    public void Sh_RestoresEnvironmentAfterExecution()
    {
        _shell.Execute("export VAR=original");
        _fs.WriteFile("/script.sh", "export VAR=modified\necho $1");
        
        _shell.Execute("sh /script.sh test");
        
        // Check that positional parameters are restored (not leaking from script)
        var result = _shell.Execute("echo $1");
        Assert.Equal("", result.Stdout.Trim());
    }

    [Fact]
    public void Sh_HandlesEmptyLines()
    {
        _fs.WriteFile("/script.sh", "echo First\n\n\necho Second");
        
        var result = _shell.Execute("sh /script.sh");
        
        Assert.True(result.Success);
        Assert.Contains("First", result.Stdout);
        Assert.Contains("Second", result.Stdout);
    }

    [Fact]
    public void Sh_ExecutesNestedScript()
    {
        _fs.WriteFile("/outer.sh", "echo Outer\nsh /inner.sh\necho Done");
        _fs.WriteFile("/inner.sh", "echo Inner");
        
        var result = _shell.Execute("sh /outer.sh");
        
        Assert.True(result.Success);
        Assert.Contains("Outer", result.Stdout);
        Assert.Contains("Inner", result.Stdout);
        Assert.Contains("Done", result.Stdout);
    }

    #endregion

    #region Glob Expansion Tests

    [Fact]
    public void GlobExpansion_StarPattern_MatchesFiles()
    {
        _fs.WriteFile("/file1.txt", "content1");
        _fs.WriteFile("/file2.txt", "content2");
        _fs.WriteFile("/file3.log", "content3");

        var result = _shell.Execute("cat *.txt");

        Assert.True(result.Success);
        Assert.Contains("content1", result.Stdout);
        Assert.Contains("content2", result.Stdout);
        Assert.DoesNotContain("content3", result.Stdout);
    }

    [Fact]
    public void GlobExpansion_QuestionMark_MatchesSingleChar()
    {
        _fs.WriteFile("/file1.txt", "one");
        _fs.WriteFile("/file2.txt", "two");
        _fs.WriteFile("/file10.txt", "ten");

        var result = _shell.Execute("cat file?.txt");

        Assert.True(result.Success);
        Assert.Contains("one", result.Stdout);
        Assert.Contains("two", result.Stdout);
        Assert.DoesNotContain("ten", result.Stdout);
    }

    [Fact]
    public void GlobExpansion_QuotedPattern_NotExpanded()
    {
        _fs.WriteFile("/file1.txt", "content1");
        _fs.WriteFile("/*.txt", "literal");

        var result = _shell.Execute("cat '*.txt'");

        Assert.True(result.Success);
        Assert.Equal("literal", result.Stdout);
    }

    [Fact]
    public void GlobExpansion_NoMatches_KeepsPattern()
    {
        var result = _shell.Execute("cat *.nonexistent");

        Assert.False(result.Success);
        Assert.Contains("*.nonexistent", result.Stderr);
    }

    [Fact]
    public void GlobExpansion_InSubdirectory()
    {
        _fs.WriteFile("/src/app.cs", "app");
        _fs.WriteFile("/src/util.cs", "util");
        _fs.WriteFile("/src/readme.md", "readme");

        _shell.Execute("cd /src");
        var result = _shell.Execute("cat *.cs");

        Assert.True(result.Success);
        Assert.Contains("app", result.Stdout);
        Assert.Contains("util", result.Stdout);
        Assert.DoesNotContain("readme", result.Stdout);
    }

    [Fact]
    public void GlobExpansion_AbsolutePath()
    {
        _fs.WriteFile("/data/file1.csv", "csv1");
        _fs.WriteFile("/data/file2.csv", "csv2");

        var result = _shell.Execute("cat /data/*.csv");

        Assert.True(result.Success);
        Assert.Contains("csv1", result.Stdout);
        Assert.Contains("csv2", result.Stdout);
    }

    [Fact]
    public void GlobExpansion_WithGrep()
    {
        _fs.WriteFile("/test1.cs", "class Foo {}");
        _fs.WriteFile("/test2.cs", "class Bar {}");
        _fs.WriteFile("/test3.txt", "class Baz {}");

        var result = _shell.Execute("grep class *.cs");

        Assert.True(result.Success);
        Assert.Contains("Foo", result.Stdout);
        Assert.Contains("Bar", result.Stdout);
        Assert.DoesNotContain("Baz", result.Stdout);
    }

    [Fact]
    public void GlobExpansion_WithRm()
    {
        _fs.WriteFile("/temp1.log", "log1");
        _fs.WriteFile("/temp2.log", "log2");
        _fs.WriteFile("/keep.txt", "keep");

        var result = _shell.Execute("rm *.log");

        Assert.True(result.Success);
        Assert.False(_fs.Exists("/temp1.log"));
        Assert.False(_fs.Exists("/temp2.log"));
        Assert.True(_fs.Exists("/keep.txt"));
    }

    [Fact]
    public void GlobExpansion_RelativePath()
    {
        _fs.WriteFile("/project/src/a.cs", "a");
        _fs.WriteFile("/project/src/b.cs", "b");

        _shell.Execute("cd /project");
        var result = _shell.Execute("cat src/*.cs");

        Assert.True(result.Success);
        Assert.Contains("a", result.Stdout);
        Assert.Contains("b", result.Stdout);
    }

    [Fact]
    public void GlobExpansion_FlagsNotExpanded()
    {
        _fs.WriteFile("/-n", "should not match");
        _fs.WriteFile("/file.txt", "hello world");

        var result = _shell.Execute("grep -n hello file.txt");

        Assert.True(result.Success);
        Assert.Contains("1:hello world", result.Stdout);
    }

    #endregion

    #region Escape Sequence Tests

    [Fact]
    public void EscapeSequence_EscapedQuoteInDoubleQuotes()
    {
        var result = _shell.Execute("echo \"say \\\"hello\\\"\"");

        Assert.True(result.Success);
        Assert.Equal("say \"hello\"", result.Stdout);
    }

    [Fact]
    public void EscapeSequence_EscapedQuoteInSingleQuotes()
    {
        var result = _shell.Execute("echo 'it\\'s working'");

        Assert.True(result.Success);
        Assert.Equal("it's working", result.Stdout);
    }

    [Fact]
    public void EscapeSequence_Newline()
    {
        _fs.WriteFile("/test.txt", "line1\\nline2");
        
        var result = _shell.Execute("echo \"line1\\nline2\"");

        Assert.True(result.Success);
        Assert.Equal("line1\nline2", result.Stdout);
    }

    [Fact]
    public void EscapeSequence_Tab()
    {
        var result = _shell.Execute("echo \"col1\\tcol2\"");

        Assert.True(result.Success);
        Assert.Equal("col1\tcol2", result.Stdout);
    }

    [Fact]
    public void EscapeSequence_Backslash()
    {
        var result = _shell.Execute("echo \"path\\\\to\\\\file\"");

        Assert.True(result.Success);
        Assert.Equal("path\\to\\file", result.Stdout);
    }

    [Fact]
    public void EscapeSequence_OutsideQuotes()
    {
        var result = _shell.Execute("echo hello\\ world");

        Assert.True(result.Success);
        Assert.Equal("hello world", result.Stdout);
    }

    [Fact]
    public void EscapeSequence_GrepWithQuotedPattern()
    {
        _fs.WriteFile("/test.txt", "case \"grep\" => handler");

        var result = _shell.Execute("grep \"\\\"grep\\\"\" /test.txt");

        Assert.True(result.Success);
        Assert.Contains("grep", result.Stdout);
    }

    [Fact]
    public void EscapeSequence_UnrecognizedEscape_PassedLiterally()
    {
        var result = _shell.Execute("echo \"\\x\"");

        Assert.True(result.Success);
        Assert.Equal("\\x", result.Stdout);
    }

    #endregion

    #region Recursive Options Tests

    [Fact]
    public void Ls_Recursive_ListsSubdirectories()
    {
        _fs.WriteFile("/project/src/app.cs", "app");
        _fs.WriteFile("/project/src/util.cs", "util");
        _fs.WriteFile("/project/tests/test.cs", "test");
        _fs.WriteFile("/project/README.md", "readme");

        var result = _shell.Execute("ls -R /project");

        Assert.True(result.Success);
        Assert.Contains("/project:", result.Stdout);
        Assert.Contains("src", result.Stdout);
        Assert.Contains("tests", result.Stdout);
        Assert.Contains("/project/src:", result.Stdout);
        Assert.Contains("app.cs", result.Stdout);
        Assert.Contains("/project/tests:", result.Stdout);
        Assert.Contains("test.cs", result.Stdout);
    }

    [Fact]
    public void Ls_Recursive_WithLongFormat()
    {
        _fs.WriteFile("/dir/file.txt", "content");
        _fs.WriteFile("/dir/sub/nested.txt", "nested");

        var result = _shell.Execute("ls -lR /dir");

        Assert.True(result.Success);
        Assert.Contains("/dir:", result.Stdout);
        Assert.Contains("file.txt", result.Stdout);
        Assert.Contains("/dir/sub:", result.Stdout);
        Assert.Contains("nested.txt", result.Stdout);
    }

    [Fact]
    public void Cp_Recursive_CopiesDirectory()
    {
        _fs.WriteFile("/src/file1.txt", "content1");
        _fs.WriteFile("/src/sub/file2.txt", "content2");

        var result = _shell.Execute("cp -r /src /dest");

        Assert.True(result.Success);
        Assert.True(_fs.Exists("/dest/file1.txt"));
        Assert.True(_fs.Exists("/dest/sub/file2.txt"));
        Assert.Equal("content1", _fs.ReadFile("/dest/file1.txt", System.Text.Encoding.UTF8));
        Assert.Equal("content2", _fs.ReadFile("/dest/sub/file2.txt", System.Text.Encoding.UTF8));
    }

    [Fact]
    public void Cp_WithoutRecursive_FailsOnDirectory()
    {
        _fs.CreateDirectory("/mydir");

        var result = _shell.Execute("cp /mydir /dest");

        Assert.False(result.Success);
        Assert.Contains("-r not specified", result.Stderr);
    }

    [Fact]
    public void Grep_Recursive_SearchesDirectories()
    {
        _fs.WriteFile("/project/src/app.cs", "class MyApp { }");
        _fs.WriteFile("/project/src/util.cs", "class MyUtil { }");
        _fs.WriteFile("/project/tests/test.cs", "class MyTest { }");
        _fs.WriteFile("/project/README.md", "# My Project");

        var result = _shell.Execute("grep -r class /project");

        Assert.True(result.Success);
        Assert.Contains("app.cs", result.Stdout);
        Assert.Contains("util.cs", result.Stdout);
        Assert.Contains("test.cs", result.Stdout);
        Assert.DoesNotContain("README", result.Stdout);
    }

    [Fact]
    public void Grep_Recursive_WithLineNumbers()
    {
        _fs.WriteFile("/dir/file1.txt", "line1\nmatch here\nline3");
        _fs.WriteFile("/dir/file2.txt", "no match");

        var result = _shell.Execute("grep -rn match /dir");

        Assert.True(result.Success);
        Assert.Contains("file1.txt:2:match here", result.Stdout);
    }

    [Fact]
    public void Grep_WithoutRecursive_FailsOnDirectory()
    {
        _fs.CreateDirectory("/mydir");

        var result = _shell.Execute("grep pattern /mydir");

        Assert.False(result.Success);
        Assert.Contains("Is a directory", result.Stderr);
    }

    [Fact]
    public void Grep_Recursive_CombinedFlags()
    {
        _fs.WriteFile("/src/a.cs", "Hello World");
        _fs.WriteFile("/src/b.cs", "hello world");

        var result = _shell.Execute("grep -rin hello /src");

        Assert.True(result.Success);
        Assert.Contains("a.cs:1:Hello World", result.Stdout);
        Assert.Contains("b.cs:1:hello world", result.Stdout);
    }

    #endregion
}
