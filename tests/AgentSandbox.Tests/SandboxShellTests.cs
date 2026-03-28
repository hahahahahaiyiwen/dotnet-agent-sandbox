using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Security;

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
    public void Mkdir_ExistingDirectoryWithoutP_Fails()
    {
        _fs.CreateDirectory("/existing");

        var result = _shell.Execute("mkdir /existing");

        Assert.False(result.Success);
        Assert.Contains("File exists", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Mkdir_ExistingDirectoryWithP_Succeeds()
    {
        _fs.CreateDirectory("/existing");

        var result = _shell.Execute("mkdir -p /existing");

        Assert.True(result.Success);
    }

    [Fact]
    public void Touch_CreatesEmptyFile()
    {
        var result = _shell.Execute("touch /newfile.txt");
        
        Assert.True(result.Success);
        Assert.True(_fs.Exists("/newfile.txt"));
        var bytes = _fs.ReadFile("/newfile.txt");
        Assert.Equal(0, bytes.Length);
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
    public void Cat_MultipleFiles_KeepsPriorOutputBeforeFailure()
    {
        _fs.WriteFile("/a.txt", "A");
        _fs.WriteFile("/c.txt", "C");

        var result = _shell.Execute("cat /a.txt /missing.txt /c.txt");

        Assert.False(result.Success);
        Assert.Equal("A", result.Stdout);
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
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
    public void Ls_MultiplePaths_LaterFailure_KeepsPriorOutputBeforeFailure()
    {
        _fs.WriteFile("/dir1/a.txt", "a");
        _fs.WriteFile("/dir2/b.txt", "b");

        var result = _shell.Execute("ls /dir1 /missing /dir2");

        Assert.False(result.Success);
        Assert.Contains("a.txt", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("b.txt", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("missing", result.Stderr, StringComparison.Ordinal);
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
    public void Rm_MultiplePaths_LaterFailure_KeepsEarlierDelete()
    {
        _fs.WriteFile("/a.txt", "a");
        _fs.WriteFile("/b.txt", "b");

        var result = _shell.Execute("rm /a.txt /missing.txt /b.txt");

        Assert.False(result.Success);
        Assert.False(_fs.Exists("/a.txt"));
        Assert.True(_fs.Exists("/b.txt"));
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Rm_Force_MultiplePaths_SkipsMissingAndContinues()
    {
        _fs.WriteFile("/a.txt", "a");
        _fs.WriteFile("/b.txt", "b");

        var result = _shell.Execute("rm -f /a.txt /missing.txt /b.txt");

        Assert.True(result.Success);
        Assert.False(_fs.Exists("/a.txt"));
        Assert.False(_fs.Exists("/b.txt"));
    }

    [Fact]
    public void Cp_CopiesFile()
    {
        _fs.WriteFile("/source.txt", "content");
        
        var result = _shell.Execute("cp /source.txt /dest.txt");
        
        Assert.True(result.Success);
        Assert.True(_fs.Exists("/dest.txt"));
        var contentBytes = _fs.ReadFileBytes("/dest.txt");
        var content = System.Text.Encoding.UTF8.GetString(contentBytes);
        Assert.Equal("content", content);
    }

    [Fact]
    public void Cp_SourceEqualsDestination_ReturnsError()
    {
        _fs.WriteFile("/same.txt", "content");

        var result = _shell.Execute("cp /same.txt /same.txt");

        Assert.False(result.Success);
        Assert.Contains("Destination already exists", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Cp_MultipleSources_LaterFailure_KeepsEarlierCopiedFile()
    {
        _fs.WriteFile("/s1.txt", "one");
        _fs.CreateDirectory("/dest");

        var result = _shell.Execute("cp /s1.txt /missing.txt /dest");

        Assert.False(result.Success);
        Assert.True(_fs.Exists("/dest/s1.txt"));
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
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
    public void Mv_SourceEqualsDestination_ReturnsError()
    {
        _fs.WriteFile("/same.txt", "content");

        var result = _shell.Execute("mv /same.txt /same.txt");

        Assert.False(result.Success);
        Assert.Contains("Destination already exists", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Mv_MultipleSources_LaterFailure_KeepsEarlierMovedFile()
    {
        _fs.WriteFile("/s1.txt", "one");
        _fs.WriteFile("/s2.txt", "two");
        _fs.CreateDirectory("/dest");

        var result = _shell.Execute("mv /s1.txt /missing.txt /s2.txt /dest");

        Assert.False(result.Success);
        Assert.True(_fs.Exists("/dest/s1.txt"));
        Assert.False(_fs.Exists("/s1.txt"));
        Assert.True(_fs.Exists("/s2.txt"));
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
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
    public void Head_FileShorterThanRequestedLines_ReturnsAllLines()
    {
        _fs.WriteFile("/short.txt", "line1\nline2");

        var result = _shell.Execute("head -n 10 /short.txt");

        Assert.True(result.Success);
        Assert.Equal("line1\nline2", result.Stdout.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Head_ZeroLines_ReturnsEmptyOutput()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2\nline3");

        var result = _shell.Execute("head -n 0 /lines.txt");

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Stdout);
    }

    [Fact]
    public void Head_MultipleFiles_LaterFailure_KeepsPriorOutputBeforeFailure()
    {
        _fs.WriteFile("/a.txt", "A1\nA2");
        _fs.WriteFile("/c.txt", "C1\nC2");

        var result = _shell.Execute("head -n 1 /a.txt /missing.txt /c.txt");

        Assert.False(result.Success);
        Assert.Equal("A1", result.Stdout.Replace("\r\n", "\n"));
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
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
    public void Tail_FileShorterThanRequestedLines_ReturnsAllLines()
    {
        _fs.WriteFile("/short.txt", "line1\nline2");

        var result = _shell.Execute("tail -n 10 /short.txt");

        Assert.True(result.Success);
        Assert.Equal("line1\nline2", result.Stdout.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Tail_ZeroLines_ReturnsEmptyOutput()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2\nline3");

        var result = _shell.Execute("tail -n 0 /lines.txt");

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Stdout);
    }

    [Fact]
    public void Tail_MultipleFiles_LaterFailure_KeepsPriorOutputBeforeFailure()
    {
        _fs.WriteFile("/a.txt", "A1\nA2");
        _fs.WriteFile("/c.txt", "C1\nC2");

        var result = _shell.Execute("tail -n 1 /a.txt /missing.txt /c.txt");

        Assert.False(result.Success);
        Assert.Equal("A2", result.Stdout.Replace("\r\n", "\n"));
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Head_MissingFileOperand_ReturnsContractError()
    {
        var result = _shell.Execute("head");

        Assert.False(result.Success);
        Assert.Contains("head: missing file operand", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Tail_MissingFileOperand_ReturnsContractError()
    {
        var result = _shell.Execute("tail");

        Assert.False(result.Success);
        Assert.Contains("tail: missing file operand", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Tail_InvalidNegativeN_ReturnsContractError()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2");

        var result = _shell.Execute("tail -n -2 /lines.txt");

        Assert.False(result.Success);
        Assert.Contains("tail: invalid number of lines", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Head_InvalidNonNumericN_ReturnsContractError()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2");

        var result = _shell.Execute("head -n x /lines.txt");

        Assert.False(result.Success);
        Assert.Contains("head: invalid number of lines", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Head_InvalidNegativeN_ReturnsContractError()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2");

        var result = _shell.Execute("head -n -2 /lines.txt");

        Assert.False(result.Success);
        Assert.Contains("head: invalid number of lines", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Tail_InvalidNonNumericN_ReturnsContractError()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2");

        var result = _shell.Execute("tail -n x /lines.txt");

        Assert.False(result.Success);
        Assert.Contains("tail: invalid number of lines", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Tail_ExcessiveN_ReturnsContractError()
    {
        _fs.WriteFile("/lines.txt", "line1\nline2");

        var result = _shell.Execute("tail -n 1000000000 /lines.txt");

        Assert.False(result.Success);
        Assert.Contains("tail: invalid number of lines", result.Stderr, StringComparison.Ordinal);
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
    public void Grep_EmptyPattern_MatchesAllLines()
    {
        _fs.WriteFile("/all.txt", "one\ntwo\nthree");

        var result = _shell.Execute("grep \"\" /all.txt");

        Assert.True(result.Success);
        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Grep_MissingPatternOrFile_ReturnsContractError()
    {
        var result = _shell.Execute("grep");

        Assert.False(result.Success);
        Assert.Contains("grep: missing pattern or file", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Grep_MultipleFiles_LaterFailure_KeepsPriorOutputBeforeFailure()
    {
        _fs.WriteFile("/a.txt", "apple\nbanana");
        _fs.WriteFile("/c.txt", "apple");

        var result = _shell.Execute("grep apple /a.txt /missing.txt /c.txt");

        Assert.False(result.Success);
        Assert.Contains("/a.txt:apple", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("/c.txt:apple", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_SetsEnvironmentVariable()
    {
        _shell.Execute("export MY_VAR=my_value");
        
        Assert.Equal("my_value", ((IShellContext)_shell).Environment["MY_VAR"]);
    }

    [Fact]
    public void Export_NoAssignments_ReturnsContractError()
    {
        var result = _shell.Execute("export");

        Assert.False(result.Success);
        Assert.Contains("export: missing assignment", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_InvalidAssignment_ReturnsContractError()
    {
        var result = _shell.Execute("export FOO");

        Assert.False(result.Success);
        Assert.Contains("export: invalid assignment", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_EmptyVariableName_ReturnsContractError()
    {
        var result = _shell.Execute("export =value");

        Assert.False(result.Success);
        Assert.Contains("export: invalid assignment", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_InvalidVariableName_ReturnsContractError()
    {
        var result = _shell.Execute("export 1FOO=value");

        Assert.False(result.Success);
        Assert.Contains("export: invalid assignment", result.Stderr, StringComparison.Ordinal);
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
    public void CommandChaining_OrOr_ReturnsExplicitError()
    {
        var result = _shell.Execute("echo a || echo b");

        Assert.False(result.Success);
        Assert.Contains("Command chaining (||) is not supported", result.Stderr);
    }

    [Fact]
    public void CommandSeparator_Semicolon_ExecutesSequentially()
    {
        var result = _shell.Execute("echo a; echo b");

        Assert.True(result.Success);
        Assert.Equal("a\nb", result.Stdout);
    }

    [Fact]
    public void CommandChaining_AndAnd_StopsOnFailure()
    {
        var result = _shell.Execute("echo start && missing_command && echo never");

        Assert.False(result.Success);
        Assert.Contains("missing_command: command not found", result.Stderr);
        Assert.Equal("start", result.Stdout);
    }

    [Fact]
    public void CommandChaining_MixedAndAndAndSemicolon_RunsAfterFailureWhenSeparated()
    {
        var result = _shell.Execute("echo start && missing_command; echo after");

        Assert.True(result.Success);
        Assert.Equal("start\nafter", result.Stdout);
        Assert.Contains("missing_command: command not found", result.Stderr);
    }

    [Fact]
    public void CommandChaining_AndAnd_FirstCommandFails_SkipsSubsequent()
    {
        var result = _shell.Execute("missing_command && echo never");

        Assert.False(result.Success);
        Assert.Contains("missing_command: command not found", result.Stderr);
        Assert.Equal(string.Empty, result.Stdout);
    }

    [Fact]
    public void CommandChaining_RedirectedSegment_PreservesAggregatedStdout()
    {
        var result = _shell.Execute("echo one && echo two > /out.txt && cat /out.txt");

        Assert.True(result.Success);
        Assert.Equal("one\ntwo\ntwo", result.Stdout);
    }

    [Fact]
    public void CommandChaining_RedirectedSegment_PreservesStderrAggregation()
    {
        var result = _shell.Execute("echo one && cat /missing > /out.txt && echo two");

        Assert.False(result.Success);
        Assert.Equal("one", result.Stdout);
        Assert.Contains("No such file", result.Stderr);
    }

    [Fact]
    public void CommandChaining_DoesNotAddExtraNewline_WhenSegmentAlreadyEndsWithNewline()
    {
        _fs.WriteFile("/line.txt", "line1\n");

        var result = _shell.Execute("cat /line.txt; echo line2");

        Assert.True(result.Success);
        Assert.Equal("line1\nline2", result.Stdout);
    }

    [Fact]
    public void CommandSeparator_Semicolon_MiddleFailure_FinalExitCodeFromLastCommand()
    {
        var result = _shell.Execute("echo a; missing_command; echo b");

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("a\nb", result.Stdout);
        Assert.Contains("missing_command: command not found", result.Stderr);
    }

    [Fact]
    public void CommandChaining_TrailingOperator_ReturnsSyntaxError()
    {
        var result = _shell.Execute("echo a &&");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("syntax error: missing command near", result.Stderr);
    }

    [Fact]
    public void CommandChaining_LeadingOperator_ReturnsSyntaxError()
    {
        var result = _shell.Execute("&& echo a");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("syntax error: missing command near", result.Stderr);
    }

    [Fact]
    public void CommandChaining_ConsecutiveOperators_ReturnsSyntaxError()
    {
        var result = _shell.Execute("echo a && && echo b");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Contains("syntax error: missing command near", result.Stderr);
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
        Assert.Contains("which", result.Stdout);
        Assert.Contains("date", result.Stdout);
        Assert.Contains("-h", result.Stdout); // Should mention -h for command help
    }

    #region Which Command Tests

    [Fact]
    public void Which_FindsBuiltinCommand()
    {
        var result = _shell.Execute("which ls");

        Assert.True(result.Success);
        Assert.Contains("/bin/ls", result.Stdout);
    }

    [Fact]
    public void Which_FindsSpecialCommands()
    {
        var resultHelp = _shell.Execute("which help");
        var resultWhich = _shell.Execute("which which");
        var resultSh = _shell.Execute("which sh");

        Assert.True(resultHelp.Success);
        Assert.True(resultWhich.Success);
        Assert.True(resultSh.Success);
        Assert.Contains("/bin/help", resultHelp.Stdout);
        Assert.Contains("/bin/which", resultWhich.Stdout);
        Assert.Contains("/bin/sh", resultSh.Stdout);
    }

    [Fact]
    public void Which_ReturnsErrorForUnknownCommand()
    {
        var result = _shell.Execute("which nonexistent");

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("command not found", result.Stderr);
    }

    [Fact]
    public void Which_RequiresArgument()
    {
        var result = _shell.Execute("which");

        Assert.False(result.Success);
        Assert.Contains("missing argument", result.Stderr);
    }

    #endregion

    #region Date Command Tests

    [Fact]
    public void Date_ReturnsCurrentDateTime()
    {
        var result = _shell.Execute("date");

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Stdout));
        Assert.Contains("UTC", result.Stdout);
    }

    [Fact]
    public void Date_SupportsIsoFormat()
    {
        var result = _shell.Execute("date +%F");

        Assert.True(result.Success);
        // Should be in YYYY-MM-DD format
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", result.Stdout);
    }

    [Fact]
    public void Date_SupportsTimeFormat()
    {
        var result = _shell.Execute("date +%T");

        Assert.True(result.Success);
        // Should be in HH:MM:SS format
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", result.Stdout);
    }

    [Fact]
    public void Date_SupportsCustomFormat()
    {
        var result = _shell.Execute("date +%Y/%m/%d");

        Assert.True(result.Success);
        Assert.Matches(@"^\d{4}/\d{2}/\d{2}$", result.Stdout);
    }

    [Fact]
    public void Date_SupportsUnixTimestamp()
    {
        var result = _shell.Execute("date +%s");

        Assert.True(result.Success);
        Assert.True(long.TryParse(result.Stdout, out var timestamp));
        Assert.True(timestamp > 0);
    }

    [Fact]
    public void Env_ListsVariablesInSortedOrder()
    {
        ((IShellContext)_shell).Environment["Z_VAR"] = "last";
        ((IShellContext)_shell).Environment["A_VAR"] = "first";

        var result = _shell.Execute("env");

        Assert.True(result.Success);
        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var aIndex = Array.FindIndex(lines, l => l.StartsWith("A_VAR=", StringComparison.Ordinal));
        var zIndex = Array.FindIndex(lines, l => l.StartsWith("Z_VAR=", StringComparison.Ordinal));
        Assert.True(aIndex >= 0 && zIndex >= 0);
        Assert.True(aIndex < zIndex);
    }

    [Fact]
    public void Wc_MissingOperand_ReturnsError()
    {
        var result = _shell.Execute("wc");

        Assert.False(result.Success);
        Assert.Contains("missing file operand", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wc_MultipleFiles_ReportsTotalLine()
    {
        _fs.WriteFile("/a.txt", "one\ntwo");
        _fs.WriteFile("/b.txt", "three");

        var result = _shell.Execute("wc /a.txt /b.txt");

        Assert.True(result.Success);
        Assert.Contains("/a.txt", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("/b.txt", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("total", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wc_HandlesWindowsLineEndings()
    {
        _fs.WriteFile("/windows.txt", "alpha\r\nbeta\r\ngamma");

        var result = _shell.Execute("wc /windows.txt");

        Assert.True(result.Success);
        Assert.Contains("3", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("/windows.txt", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Wc_MultipleFiles_LaterFailure_KeepsPriorOutputBeforeFailure()
    {
        _fs.WriteFile("/a.txt", "one\ntwo");
        _fs.WriteFile("/c.txt", "three");

        var result = _shell.Execute("wc /a.txt /missing.txt /c.txt");

        Assert.False(result.Success);
        Assert.Contains("/a.txt", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("total", result.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/c.txt", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("missing.txt", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_WithRelativePath_UsesCurrentDirectory()
    {
        _fs.WriteFile("/work/src/a.cs", "x");
        _fs.WriteFile("/work/src/a.txt", "x");
        _shell.Execute("cd /work");

        var result = _shell.Execute("find src -name *.cs");

        Assert.True(result.Success);
        Assert.Contains("/work/src/a.cs", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("/work/src/a.txt", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_WithoutNameFilter_ListsBaseAndChildren()
    {
        _fs.WriteFile("/root/child/file.txt", "x");

        var result = _shell.Execute("find /root");

        Assert.True(result.Success);
        Assert.Contains("/root", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("/root/child", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("/root/child/file.txt", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void RedirectAppend_AppendsMultipleCommandOutputs()
    {
        _shell.Execute("echo one > /out.txt");
        _shell.Execute("echo two >> /out.txt");

        var result = _shell.Execute("cat /out.txt");

        Assert.True(result.Success);
        Assert.Equal("onetwo", result.Stdout);
    }

    [Fact]
    public void Redirect_EmptyStdout_TruncatesExistingFile()
    {
        _shell.Execute("echo one > /out.txt");
        var redirectResult = _shell.Execute("echo > /out.txt");

        var readResult = _shell.Execute("cat /out.txt");

        Assert.True(redirectResult.Success);
        Assert.True(readResult.Success);
        Assert.Equal(string.Empty, readResult.Stdout);
    }

    [Fact]
    public void Redirect_MissingOperand_ReturnsError()
    {
        var result = _shell.Execute("echo hi >");

        Assert.False(result.Success);
        Assert.Contains("missing file operand", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Date_InvalidOption_ReturnsUsageContract()
    {
        var result = _shell.Execute("date invalid");

        Assert.False(result.Success);
        Assert.Contains("date: invalid option", result.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Usage:", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Touch_MissingFileOperand_ReturnsContractError()
    {
        var result = _shell.Execute("touch");

        Assert.False(result.Success);
        Assert.Contains("touch: missing file operand", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Redirect_ToDirectory_ReturnsError()
    {
        _fs.CreateDirectory("/dir");

        var result = _shell.Execute("echo hi > /dir");

        Assert.False(result.Success);
        Assert.Contains("redirect:", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewlineCommandSeparator_IsRejectedWithGuidance()
    {
        var result = _shell.Execute("echo one\necho two");

        Assert.False(result.Success);
        Assert.Contains("Multi-line scripts are not supported", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Find_NameFilter_ReturnsMatchingEntriesOnly()
    {
        _fs.WriteFile("/src/app.cs", "x");
        _fs.WriteFile("/src/app.txt", "x");
        _fs.WriteFile("/src/sub/lib.cs", "x");

        var result = _shell.Execute("find /src -name *.cs");

        Assert.True(result.Success);
        Assert.Contains("/src/app.cs", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("/src/sub/lib.cs", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("/src/app.txt", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Wc_ByteCount_NoTrailingNewline_IsExact()
    {
        _fs.WriteFile("/bytes.txt", "abc");

        var result = _shell.Execute("wc /bytes.txt");

        Assert.True(result.Success);
        Assert.Matches(@"\b1\s+1\s+3\s+/bytes\.txt\b", result.Stdout);
    }

    [Fact]
    public void Wc_WordCount_TreatsNewlineAsWordBoundary()
    {
        _fs.WriteFile("/words.txt", "alpha\nbeta gamma");

        var result = _shell.Execute("wc /words.txt");

        Assert.True(result.Success);
        Assert.Matches(@"\b2\s+3\s+\d+\s+/words\.txt\b", result.Stdout);
    }

    #endregion

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
    [InlineData("which")]
    [InlineData("date")]
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
        var content1Bytes = _fs.ReadFileBytes("/dest/file1.txt");
        var content1 = System.Text.Encoding.UTF8.GetString(content1Bytes);
        var content2Bytes = _fs.ReadFileBytes("/dest/sub/file2.txt");
        var content2 = System.Text.Encoding.UTF8.GetString(content2Bytes);
        Assert.Equal("content1", content1);
        Assert.Equal("content2", content2);
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

    [Fact]
    public void Grep_FilesOnly_ListsMatchingFiles()
    {
        _fs.WriteFile("/a.txt", "hello world");
        _fs.WriteFile("/b.txt", "goodbye world");
        _fs.WriteFile("/c.txt", "hello again");

        var result = _shell.Execute("grep -l hello *.txt");

        Assert.True(result.Success);
        Assert.Contains("a.txt", result.Stdout);
        Assert.Contains("c.txt", result.Stdout);
        Assert.DoesNotContain("b.txt", result.Stdout);
    }

    [Fact]
    public void Grep_Count_ShowsMatchCount()
    {
        _fs.WriteFile("/test.txt", "apple\nbanana\napple pie\ncherry");

        var result = _shell.Execute("grep -c apple /test.txt");

        Assert.True(result.Success);
        Assert.Equal("2", result.Stdout);
    }

    [Fact]
    public void Grep_InvertMatch_ShowsNonMatchingLines()
    {
        _fs.WriteFile("/test.txt", "apple\nbanana\ncherry");

        var result = _shell.Execute("grep -v apple /test.txt");

        Assert.True(result.Success);
        Assert.Contains("banana", result.Stdout);
        Assert.Contains("cherry", result.Stdout);
        Assert.DoesNotContain("apple", result.Stdout);
    }

    [Fact]
    public void Grep_WordMatch_MatchesWholeWordsOnly()
    {
        _fs.WriteFile("/test.txt", "app\napple\napplication");

        var result = _shell.Execute("grep -w app /test.txt");

        Assert.True(result.Success);
        Assert.Equal("app", result.Stdout);
    }

    [Fact]
    public void Grep_OnlyMatching_PrintsMatchedParts()
    {
        _fs.WriteFile("/test.txt", "hello world, hello there");

        var result = _shell.Execute("grep -o hello /test.txt");

        Assert.True(result.Success);
        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.Equal("hello", l.TrimEnd('\r')));
    }

    [Fact]
    public void Grep_MaxCount_StopsAfterNMatches()
    {
        _fs.WriteFile("/test.txt", "match1\nmatch2\nmatch3\nmatch4");

        var result = _shell.Execute("grep -m 2 match /test.txt");

        Assert.True(result.Success);
        var lines = result.Stdout.Split('\n');
        Assert.Equal(2, lines.Length);
    }

    [Theory]
    [InlineData("-A -1")]
    [InlineData("-B -1")]
    [InlineData("-C -1")]
    [InlineData("-m -1")]
    [InlineData("-A x")]
    [InlineData("-B x")]
    [InlineData("-C x")]
    [InlineData("-m x")]
    public void Grep_InvalidNumericOption_ReturnsContractError(string options)
    {
        _fs.WriteFile("/test.txt", "line1\nmatch\nline3");

        var result = _shell.Execute($"grep {options} match /test.txt");

        Assert.False(result.Success);
        Assert.Contains("grep: invalid numeric option", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Grep_AfterContext_ShowsLinesAfterMatch()
    {
        _fs.WriteFile("/test.txt", "line1\nmatch\nline3\nline4\nline5");

        var result = _shell.Execute("grep -A 2 match /test.txt");

        Assert.True(result.Success);
        Assert.Contains("match", result.Stdout);
        Assert.Contains("line3", result.Stdout);
        Assert.Contains("line4", result.Stdout);
        Assert.DoesNotContain("line5", result.Stdout);
    }

    [Fact]
    public void Grep_BeforeContext_ShowsLinesBeforeMatch()
    {
        _fs.WriteFile("/test.txt", "line1\nline2\nmatch\nline4");

        var result = _shell.Execute("grep -B 2 match /test.txt");

        Assert.True(result.Success);
        Assert.Contains("line1", result.Stdout);
        Assert.Contains("line2", result.Stdout);
        Assert.Contains("match", result.Stdout);
        Assert.DoesNotContain("line4", result.Stdout);
    }

    [Fact]
    public void Grep_Context_ShowsLinesAroundMatch()
    {
        _fs.WriteFile("/test.txt", "line1\nline2\nmatch\nline4\nline5");

        var result = _shell.Execute("grep -C 1 match /test.txt");

        Assert.True(result.Success);
        Assert.Contains("line2", result.Stdout);
        Assert.Contains("match", result.Stdout);
        Assert.Contains("line4", result.Stdout);
        Assert.DoesNotContain("line1", result.Stdout);
        Assert.DoesNotContain("line5", result.Stdout);
    }

    [Fact]
    public void Grep_Context_OverlappingMatches_DoesNotEmitSeparator()
    {
        _fs.WriteFile("/test.txt", "line1\nmatch1\nline3\nmatch2\nline5");

        var result = _shell.Execute("grep -C 1 match /test.txt");

        Assert.True(result.Success);
        Assert.DoesNotContain("--", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void Grep_CombinedNewFlags()
    {
        _fs.WriteFile("/test.txt", "Hello World\nhello world\nHELLO WORLD");

        var result = _shell.Execute("grep -icv hello /test.txt");

        Assert.True(result.Success);
        Assert.Equal("0", result.Stdout); // All lines match hello with -i, so inverted count is 0
    }

    [Fact]
    public void Execute_WhitespaceOnly_ReturnsSuccessWithEmptyOutput()
    {
        var result = _shell.Execute("   ");

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void RegisterCommand_WithAlias_AllowsAliasInvocation()
    {
        _shell.RegisterCommand(new AliasEchoCommand());

        var result = _shell.Execute("ae hello");

        Assert.True(result.Success);
        Assert.Equal("hello", result.Stdout);
    }

    [Fact]
    public void ExtensionCommand_Throws_ReturnsPrefixedError()
    {
        _shell.RegisterCommand(new ThrowingExtensionCommand());

        var result = _shell.Execute("boom-ext");

        Assert.False(result.Success);
        Assert.Contains("boom-ext: expected failure", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellContext_TryResolveSecret_WithMaxAgePolicyAndMissingResolvedTimestamp_ReturnsError()
    {
        var shell = new SandboxShell(
            _fs,
            new LegacyStringOnlySecretBroker(new Dictionary<string, string>
            {
                ["api-token"] = "super-secret-token"
            }),
            new SecretResolutionPolicy
            {
                MaxSecretAge = TimeSpan.FromMinutes(5)
            });
        var context = (IShellContext)shell;

        var success = context.TryResolveSecret(
            "api-token",
            new SecretAccessRequest
            {
                CommandName = "unit-test"
            },
            out _,
            out var errorMessage);

        Assert.False(success);
        Assert.Contains("missing resolved timestamp required by max-age policy", errorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellContext_TryResolveSecretReferences_EmptyInput_ReturnsUnchanged()
    {
        var context = (IShellContext)_shell;
        var resolvedSecrets = new HashSet<string>(StringComparer.Ordinal);

        var success = context.TryResolveSecretReferences(
            string.Empty,
            new SecretAccessRequest
            {
                CommandName = "unit-test"
            },
            resolvedSecrets,
            out var resolvedValue,
            out var errorMessage);

        Assert.True(success);
        Assert.Equal(string.Empty, resolvedValue);
        Assert.Null(errorMessage);
        Assert.Empty(resolvedSecrets);
    }

    [Fact]
    public void Help_WhenExtensionRegistered_ListsExtensionCommandsSection()
    {
        _shell.RegisterCommand(new AliasEchoCommand());

        var result = _shell.Execute("help");

        Assert.True(result.Success);
        Assert.Contains("Extension commands:", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("alias-echo", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobExpansion_RecursivePathPattern_MatchesNestedDirectories()
    {
        _fs.WriteFile("/project/src/a/file1.txt", "one");
        _fs.WriteFile("/project/src/b/file2.txt", "two");
        _shell.Execute("cd /project");

        var result = _shell.Execute("cat src/*/*.txt");

        Assert.True(result.Success);
        Assert.Contains("one", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("two", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectScriptExecution_WhenReadThrows_ReturnsPrefixedError()
    {
        var fs = new ThrowingReadFileSystem("/boom.sh", "read failure");
        fs.WriteFile("/boom.sh", "echo ignored");
        var shell = new SandboxShell(fs);

        var result = shell.Execute("./boom.sh");

        Assert.False(result.Success);
        Assert.Contains("./boom.sh: read failure", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ShCommand_WhenReadThrows_ReturnsShPrefixedError()
    {
        var fs = new ThrowingReadFileSystem("/boom.sh", "read failure");
        fs.WriteFile("/boom.sh", "echo ignored");
        var shell = new SandboxShell(fs);

        var result = shell.Execute("sh /boom.sh");

        Assert.False(result.Success);
        Assert.Contains("sh: read failure", result.Stderr, StringComparison.Ordinal);
    }

    private sealed class AliasEchoCommand : IShellCommand
    {
        public string Name => "alias-echo";
        public string Description => "Echoes first arg.";
        public IReadOnlyList<string> Aliases => ["ae"];

        public ShellResult Execute(string[] args, IShellContext context)
        {
            return ShellResult.Ok(args.Length > 0 ? args[0] : string.Empty);
        }
    }

    private sealed class ThrowingExtensionCommand : IShellCommand
    {
        public string Name => "boom-ext";
        public string Description => "Throws for test coverage.";

        public ShellResult Execute(string[] args, IShellContext context)
        {
            throw new InvalidOperationException("expected failure");
        }
    }

    private sealed class LegacyStringOnlySecretBroker : ISecretBroker
    {
        private readonly IReadOnlyDictionary<string, string> _secrets;

        public LegacyStringOnlySecretBroker(IReadOnlyDictionary<string, string> secrets)
        {
            _secrets = secrets;
        }

        public bool TryResolve(string secretRef, out string secretValue)
        {
            return _secrets.TryGetValue(secretRef, out secretValue!);
        }
    }

    private sealed class ThrowingReadFileSystem : IFileSystem
    {
        private readonly FileSystem _inner = new();
        private readonly string _throwPath;
        private readonly string _message;

        public ThrowingReadFileSystem(string throwPath, string message)
        {
            _throwPath = FileSystemPath.Normalize(throwPath);
            _message = message;
        }

        public bool Exists(string path) => _inner.Exists(path);
        public bool IsFile(string path) => _inner.IsFile(path);
        public bool IsDirectory(string path) => _inner.IsDirectory(path);
        public FileEntry? GetEntry(string path) => _inner.GetEntry(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public IEnumerable<string> ListDirectory(string path) => _inner.ListDirectory(path);

        public byte[] ReadFileBytes(string path)
        {
            if (FileSystemPath.Normalize(path) == _throwPath)
            {
                throw new InvalidOperationException(_message);
            }

            return _inner.ReadFileBytes(path);
        }

        public string ReadFile(string path) => _inner.ReadFile(path);
        public IEnumerable<string> ReadFileLines(string path, int? startLine = null, int? endLine = null) => _inner.ReadFileLines(path, startLine, endLine);
        public void WriteFile(string path, byte[] content) => _inner.WriteFile(path, content);
        public void WriteFile(string path, string content) => _inner.WriteFile(path, content);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void DeleteDirectory(string path, bool recursive = false) => _inner.DeleteDirectory(path, recursive);
        public void Delete(string path, bool recursive = false) => _inner.Delete(path, recursive);
        public void Copy(string source, string destination, bool overwrite = false) => _inner.Copy(source, destination, overwrite);
        public void Move(string source, string destination, bool overwrite = false) => _inner.Move(source, destination, overwrite);
    }

    #endregion
}
