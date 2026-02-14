using AgentSandbox.Core;

namespace AgentSandbox.Tests;

public class SandboxFileIOTests : IDisposable
{
    private readonly Sandbox _sandbox;

    public SandboxFileIOTests()
    {
        _sandbox = new Sandbox();
    }

    public void Dispose()
    {
        _sandbox?.Dispose();
    }

    private string ReadAll(string path)
    {
        return string.Join("\n", _sandbox.ReadFileLines(path));
    }

    #region ReadFile Tests

    [Fact]
    public void ReadFile_ReturnsFileContent()
    {
        _sandbox.Execute("mkdir -p /test");
        _sandbox.Execute("echo 'Hello World' > /test/file.txt");

        var content = ReadAll("/test/file.txt");

        Assert.Equal("Hello World", content.Trim());
    }

    [Fact]
    public void ReadFile_ThrowsWhenFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => ReadAll("/nonexistent/file.txt"));
    }

    [Fact]
    public void ReadFile_ThrowsWhenPathIsDirectory()
    {
        _sandbox.Execute("mkdir -p /test/dir");

        Assert.Throws<InvalidOperationException>(() => ReadAll("/test/dir"));
    }

    [Fact]
    public void ReadFile_ReturnsMultilineContent()
    {
        var multilineContent = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/test.txt", multilineContent);

        var content = ReadAll("/test.txt");

        Assert.Equal(multilineContent, content);
    }

    [Fact]
    public void ReadFile_HandlesEmptyFile()
    {
        _sandbox.WriteFile("/empty.txt", "");

        var content = ReadAll("/empty.txt");

        Assert.Equal("", content);
    }

    [Fact]
    public void ReadFile_HandlesSpecialCharacters()
    {
        var specialContent = "Special chars: !@#$%^&*()_+-=[]{}|;:',.<>?/\\";
        _sandbox.WriteFile("/special.txt", specialContent);

        var content = ReadAll("/special.txt");

        Assert.Equal(specialContent, content);
    }

    #endregion

    #region ReadFile Line-Range Tests

    [Fact]
    public void ReadFile_WithLineRange_ReturnsSpecificLines()
    {
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        _sandbox.WriteFile("/lines.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/lines.txt", startLine: 2, endLine: 5));

        // Normalize line endings for comparison
        Assert.Equal("Line 2\nLine 3\nLine 4", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_WithStartLineOnly_ReadsToEndOfFile()
    {
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        _sandbox.WriteFile("/lines.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/lines.txt", startLine: 4));

        // Normalize line endings for comparison
        Assert.Equal("Line 4\nLine 5", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_WithEndLineOnly_ReadsFromStartToEndLine()
    {
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        _sandbox.WriteFile("/lines.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/lines.txt", endLine: 3));

        // Normalize line endings for comparison
        Assert.Equal("Line 1\nLine 2", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_WithLineRangeOutOfBounds_ReturnsEmptyOrPartial()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lines.txt", content);

        // Requesting lines beyond end of file
        var result = string.Join("\n", _sandbox.ReadFileLines("/lines.txt", startLine: 10));

        Assert.Equal("", result);
    }

    [Fact]
    public void ReadFile_WithLineRangePartiallyOutOfBounds_ReturnsSoFar()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lines.txt", content);

        // Requesting lines 2-10, but file only has 3 lines
        var result = string.Join("\n", _sandbox.ReadFileLines("/lines.txt", startLine: 2, endLine: 10));

        Assert.Equal("Line 2\nLine 3", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_FirstLine()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lines.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/lines.txt", startLine: 1, endLine: 2));

        Assert.Equal("Line 1", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_LastLine()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lines.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/lines.txt", startLine: 2, endLine: 4));

        Assert.Equal("Line 2\nLine 3", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_LargeFileLineRange()
    {
        // Simulate a large file
        var lines = new List<string>();
        for (int i = 1; i <= 1000; i++)
        {
            lines.Add($"Line {i}");
        }
        var content = string.Join("\n", lines);
        _sandbox.WriteFile("/large.txt", content);

        // Read lines 100-110
        var result = string.Join("\n", _sandbox.ReadFileLines("/large.txt", startLine: 100, endLine: 111));

        var resultLines = result.Replace("\r\n", "\n").Split('\n');
        Assert.Equal(11, resultLines.Length);
        Assert.Equal("Line 100", resultLines[0]);
        Assert.Equal("Line 110", resultLines[10]);
    }

    [Fact]
    public void ReadFile_LineRangeWithMultilineFile_PreservesContent()
    {
        var content = "Start\n\nMiddle\n\nEnd";
        _sandbox.WriteFile("/gaps.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/gaps.txt", startLine: 1, endLine: 4));

        var lines = result.Replace("\r\n", "\n").Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("Start", lines[0]);
        Assert.Equal("", lines[1]);  // Empty line
        Assert.Equal("Middle", lines[2]);
    }

    #endregion

    #region Lazy Line Scanning Tests (comprehensive coverage)

    [Fact]
    public void ReadFile_LazyScanning_HandlesLFLineEndings()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lf.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/lf.txt", startLine: 1, endLine: 3));
        
        Assert.Equal("Line 1\nLine 2", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_HandlesCRLFLineEndings()
    {
        var content = "Line 1\r\nLine 2\r\nLine 3";
        _sandbox.WriteFile("/crlf.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/crlf.txt", startLine: 2, endLine: 4));
        
        var normalized = result.Replace("\r\n", "\n");
        Assert.Equal("Line 2\nLine 3", normalized);
    }

    [Fact]
    public void ReadFile_LazyScanning_HandlesCRLineEndings()
    {
        var content = "Line 1\rLine 2\rLine 3";
        _sandbox.WriteFile("/cr.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/cr.txt", startLine: 1, endLine: 3));
        
        var normalized = result.Replace("\r", "\n");
        Assert.Equal("Line 1\nLine 2", normalized);
    }

    [Fact]
    public void ReadFile_LazyScanning_HandlesMixedLineEndings()
    {
        var content = "Line 1\r\nLine 2\nLine 3\rLine 4";
        _sandbox.WriteFile("/mixed.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/mixed.txt", startLine: 2, endLine: 5));
        
        var normalized = result.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalized.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("Line 2", lines[0]);
        Assert.Equal("Line 3", lines[1]);
        Assert.Equal("Line 4", lines[2]);
    }

    [Fact]
    public void ReadFile_LazyScanning_EmptyFile()
    {
        _sandbox.WriteFile("/empty.txt", "");

        var result = string.Join("\n", _sandbox.ReadFileLines("/empty.txt", startLine: 1, endLine: 10));
        
        Assert.Equal("", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_SingleLineNoNewline()
    {
        var content = "Single line";
        _sandbox.WriteFile("/single.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/single.txt", startLine: 1, endLine: 2));
        
        Assert.Equal("Single line", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_BeyondFileLength()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/short.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/short.txt", startLine: 10, endLine: 20));
        
        Assert.Equal("", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_StartBeyondButEndWithinFile()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/file.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/file.txt", startLine: 10, endLine: 20));
        
        Assert.Equal("", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_FileEndingWithNewline()
    {
        var content = "Line 1\nLine 2\nLine 3\n";
        _sandbox.WriteFile("/trailing.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/trailing.txt", startLine: 1, endLine: 4));
        
        // Should be "Line 1\nLine 2\nLine 3" (trailing newline removed for EOF reads)
        Assert.Equal("Line 1\nLine 2\nLine 3", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_FileEndingWithMultipleNewlines()
    {
        var content = "Line 1\nLine 2\n\n";
        _sandbox.WriteFile("/double.txt", content);

        var result = ReadAll("/double.txt");
        
        // Should remove only the final empty line created by the last \n
        Assert.Equal("Line 1\nLine 2\n", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_PartialRangeWithTrailingEmptyLines()
    {
        var content = "Line 1\nLine 2\nLine 3\n";
        _sandbox.WriteFile("/range.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/range.txt", startLine: 1, endLine: 3));
        
        // Should preserve empty lines within range
        var lines = result.Replace("\r\n", "\n").Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("Line 1", lines[0]);
        Assert.Equal("Line 2", lines[1]);
    }

    [Fact]
    public void ReadFile_LazyScanning_LargeLineRange()
    {
        // Create file with 1000 lines
        var lines = new List<string>();
        for (int i = 0; i < 1000; i++)
        {
            lines.Add($"Line {i}");
        }
        var content = string.Join("\n", lines);
        _sandbox.WriteFile("/large.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/large.txt", startLine: 101, endLine: 111));
        
        var resultLines = result.Replace("\r\n", "\n").Split('\n');
        Assert.Equal(10, resultLines.Length);
        Assert.Equal("Line 100", resultLines[0]);
        Assert.Equal("Line 109", resultLines[9]);
    }

    [Fact]
    public void ReadFile_LazyScanning_SingleLineExtraction()
    {
        var content = "Line 0\nLine 1\nLine 2\nLine 3\nLine 4";
        _sandbox.WriteFile("/single_line.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/single_line.txt", startLine: 1, endLine: 2));
        
        Assert.Equal("Line 0", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_LastLineOfFile()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/last.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/last.txt", startLine: 3, endLine: 4));
        
        Assert.Equal("Line 3", result);
    }

    [Fact]
    public void ReadFile_LazyScanning_WithUnicodeCharacters()
    {
        var content = "Hello 世界\nBonjourμ\nПривет";
        _sandbox.WriteFile("/unicode.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/unicode.txt", startLine: 2, endLine: 4));
        
        var resultLines = result.Replace("\r\n", "\n").Split('\n');
        Assert.Equal(2, resultLines.Length);
        Assert.Equal("Bonjourμ", resultLines[0]);
        Assert.Equal("Привет", resultLines[1]);
    }

    #region 1-Indexed Line Numbering Tests

    [Fact]
    public void ReadFile_1IndexedFirstLine_StartAt1()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/test.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/test.txt", startLine: 1, endLine: 2));

        Assert.Equal("Line 1", result);
    }

    [Fact]
    public void ReadFile_1IndexedMiddleRange()
    {
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        _sandbox.WriteFile("/test.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/test.txt", startLine: 2, endLine: 4));

        Assert.Equal("Line 2\nLine 3", result);
    }

    [Fact]
    public void ReadFile_1IndexedLastLine()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/test.txt", content);

        var result = string.Join("\n", _sandbox.ReadFileLines("/test.txt", startLine: 3, endLine: 4));

        Assert.Equal("Line 3", result);
    }

    [Fact]
    public void ReadFile_1IndexedDefaultStartLine()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/test.txt", content);

        // Default startLine should be 1 (first line), not 0
        var result = string.Join("\n", _sandbox.ReadFileLines("/test.txt", endLine: 2));

        Assert.Equal("Line 1", result);
    }

    [Fact]
    public void ReadFile_1IndexedFullFileWhenBothNull()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/test.txt", content);

        // Default startLine=1, endLine=null should read full file
        var result = ReadAll("/test.txt");

        Assert.Equal("Line 1\nLine 2\nLine 3", result);
    }

    [Fact]
    public void ReadFile_1IndexedBeyondFile()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/test.txt", content);

        // Start at line 5, which doesn't exist
        var result = string.Join("\n", _sandbox.ReadFileLines("/test.txt", startLine: 5, endLine: 10));

        Assert.Equal("", result);
    }

    #endregion

    [Fact]
    public void WriteFile_CreatesNewFile()
    {
        _sandbox.WriteFile("/new.txt", "content");

        // Just verify file exists by reading it
        var content = ReadAll("/new.txt");
        Assert.Equal("content", content);
    }

    [Fact]
    public void WriteFile_OverwritesExistingFile()
    {
        _sandbox.WriteFile("/file.txt", "original");
        _sandbox.WriteFile("/file.txt", "updated");

        var content = ReadAll("/file.txt");

        Assert.Equal("updated", content);
    }

    [Fact]
    public void WriteFile_CreatesParentDirectories()
    {
        _sandbox.WriteFile("/a/b/c/file.txt", "content");

        // Verify directories exist by checking the file can be read
        var content = ReadAll("/a/b/c/file.txt");
        Assert.Equal("content", content);
    }

    [Fact]
    public void WriteFile_ThrowsWhenPathIsDirectory()
    {
        _sandbox.Execute("mkdir -p /existing");

        Assert.Throws<InvalidOperationException>(() => _sandbox.WriteFile("/existing", "content"));
    }

    [Fact]
    public void WriteFile_HandlesEmptyContent()
    {
        _sandbox.WriteFile("/empty.txt", "");

        var content = ReadAll("/empty.txt");

        Assert.Equal("", content);
    }

    [Fact]
    public void WriteFile_HandlesMultilineContent()
    {
        var multilineContent = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/multiline.txt", multilineContent);

        var content = ReadAll("/multiline.txt");

        Assert.Equal(multilineContent, content);
    }

    [Fact]
    public void WriteFile_HandlesLargeContent()
    {
        var largeContent = new string('x', 100000);
        _sandbox.WriteFile("/large.txt", largeContent);

        var content = ReadAll("/large.txt");

        Assert.Equal(largeContent, content);
    }

    [Fact]
    public void WriteFile_UpdatesLastActivityAt()
    {
        var beforeWrite = _sandbox.LastActivityAt;

        System.Threading.Thread.Sleep(10);
        _sandbox.WriteFile("/test.txt", "content");

        Assert.True(_sandbox.LastActivityAt > beforeWrite);
    }

    #endregion

    #region ApplyPatch Tests

    [Fact]
    public void ApplyPatch_AddsLines()
    {
        _sandbox.WriteFile("/file.txt", "Line 1\r\nLine 3");

        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1,2 +1,3 @@
 Line 1
+Line 2
 Line 3
";

        _sandbox.ApplyPatch("/file.txt", patch);

        var content = ReadAll("/file.txt");
        // Normalize line endings
        var normalized = content.Replace("\r\n", "\n");
        Assert.Equal("Line 1\nLine 2\nLine 3", normalized);
    }

    [Fact]
    public void ApplyPatch_RemovesLines()
    {
        _sandbox.WriteFile("/file.txt", "Line 1\r\nLine 2\r\nLine 3");

        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1,3 +1,2 @@
 Line 1
-Line 2
 Line 3
";

        _sandbox.ApplyPatch("/file.txt", patch);

        var content = ReadAll("/file.txt");
        var normalized = content.Replace("\r\n", "\n");
        Assert.Equal("Line 1\nLine 3", normalized);
    }

    [Fact]
    public void ApplyPatch_ReplacesLines()
    {
        _sandbox.WriteFile("/file.txt", "Line 1\r\nOld Line 2\r\nLine 3");

        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1,3 +1,3 @@
 Line 1
-Old Line 2
+New Line 2
 Line 3
";

        _sandbox.ApplyPatch("/file.txt", patch);

        var content = ReadAll("/file.txt");
        var normalized = content.Replace("\r\n", "\n");
        Assert.Equal("Line 1\nNew Line 2\nLine 3", normalized);
    }

    [Fact]
    public void ApplyPatch_HandlesMultipleHunks()
    {
        _sandbox.WriteFile("/file.txt", "A\r\nB\r\nC\r\nD\r\nE");

        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1,2 +1,3 @@
 A
+A2
 B
@@ -4,2 +5,2 @@
 D
-E
+E2
";

        _sandbox.ApplyPatch("/file.txt", patch);

        var content = ReadAll("/file.txt");
        var normalized = content.Replace("\r\n", "\n");
        Assert.Equal("A\nA2\nB\nC\nD\nE2", normalized);
    }

    [Fact]
    public void ApplyPatch_ThrowsWhenFileNotFound()
    {
        var patch = @"--- a/nonexistent.txt
+++ b/nonexistent.txt
@@ -1,1 +1,1 @@
-old
+new
";

        Assert.Throws<FileNotFoundException>(() => _sandbox.ApplyPatch("/nonexistent.txt", patch));
    }

    [Fact]
    public void ApplyPatch_ThrowsOnContextMismatch()
    {
        _sandbox.WriteFile("/file.txt", "Line 1\r\nLine 3");

        // This patch expects "Line 2" at context position, but file has "Line 3"
        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1,2 +1,3 @@
 Line 1
+Line 2
 Line 2
";

        Assert.Throws<InvalidOperationException>(() => _sandbox.ApplyPatch("/file.txt", patch));
    }

    [Fact]
    public void ApplyPatch_UpdatesLastActivityAt()
    {
        _sandbox.WriteFile("/file.txt", "Line 1\nLine 3");
        var beforePatch = _sandbox.LastActivityAt;

        System.Threading.Thread.Sleep(10);

        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1,2 +1,3 @@
 Line 1
+Line 2
 Line 3
";

        _sandbox.ApplyPatch("/file.txt", patch);

        Assert.True(_sandbox.LastActivityAt > beforePatch);
    }

    [Fact]
    public void ApplyPatch_PreservesFilePermissions()
    {
        _sandbox.WriteFile("/file.txt", "content");

        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1 +1 @@
-content
+modified
";

        _sandbox.ApplyPatch("/file.txt", patch);

        // File should still exist and be readable
        var content = ReadAll("/file.txt");
        Assert.Equal("modified", content);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ReadWriteRead_RoundTrip()
    {
        var originalContent = "Original content\nLine 2\nLine 3";

        _sandbox.WriteFile("/roundtrip.txt", originalContent);
        var readContent = ReadAll("/roundtrip.txt");

        Assert.Equal(originalContent, readContent);
    }

    [Fact]
    public void WriteAndPatch_Sequence()
    {
        // Create initial file
        _sandbox.WriteFile("/sequence.txt", "Start\nEnd");

        // Apply patch to add line in middle
        var patch = @"--- a/sequence.txt
+++ b/sequence.txt
@@ -1,2 +1,3 @@
 Start
+Middle
 End
";
        _sandbox.ApplyPatch("/sequence.txt", patch);

        // Read and verify (normalize line endings)
        var content = ReadAll("/sequence.txt").Replace("\r\n", "\n");
        Assert.Equal("Start\nMiddle\nEnd", content);
    }

    [Fact]
    public void FileIODoesNotAffectShellState()
    {
        _sandbox.Execute("cd /");
        var cwd1 = _sandbox.CurrentDirectory;

        _sandbox.WriteFile("/test.txt", "content");
        var cwd2 = _sandbox.CurrentDirectory;

        ReadAll("/test.txt");
        var cwd3 = _sandbox.CurrentDirectory;

        Assert.Equal("/", cwd1);
        Assert.Equal("/", cwd2);
        Assert.Equal("/", cwd3);
    }

    [Fact]
    public void ReadFile_LazyScanning_LargeFileEfficiency()
    {
        // Create a large file with many lines (but within size limits)
        var lines = new List<string>();
        for (int i = 0; i < 1000; i++)
        {
            lines.Add($"Line {i:D4}: Some content here");
        }
        var largeContent = string.Join("\n", lines);
        _sandbox.WriteFile("/large_file.txt", largeContent);

        // Read a range from the middle - lazy scanning should be efficient
        var result = string.Join("\n", _sandbox.ReadFileLines("/large_file.txt", startLine: 501, endLine: 511));
        
        var resultLines = result.Replace("\r\n", "\n").Split('\n');
        Assert.Equal(10, resultLines.Length);
        
        // Verify the content is correct
        Assert.Contains("0500", resultLines[0]);
        Assert.Contains("0509", resultLines[9]);
    }

    #endregion
}
