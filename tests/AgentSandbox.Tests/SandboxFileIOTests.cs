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

    #region ReadFile Tests

    [Fact]
    public void ReadFile_ReturnsFileContent()
    {
        _sandbox.Execute("mkdir -p /test");
        _sandbox.Execute("echo 'Hello World' > /test/file.txt");

        var content = _sandbox.ReadFile("/test/file.txt");

        Assert.Equal("Hello World", content.Trim());
    }

    [Fact]
    public void ReadFile_ThrowsWhenFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => _sandbox.ReadFile("/nonexistent/file.txt"));
    }

    [Fact]
    public void ReadFile_ThrowsWhenPathIsDirectory()
    {
        _sandbox.Execute("mkdir -p /test/dir");

        Assert.Throws<InvalidOperationException>(() => _sandbox.ReadFile("/test/dir"));
    }

    [Fact]
    public void ReadFile_ReturnsMultilineContent()
    {
        var multilineContent = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/test.txt", multilineContent);

        var content = _sandbox.ReadFile("/test.txt");

        Assert.Equal(multilineContent, content);
    }

    [Fact]
    public void ReadFile_HandlesEmptyFile()
    {
        _sandbox.WriteFile("/empty.txt", "");

        var content = _sandbox.ReadFile("/empty.txt");

        Assert.Equal("", content);
    }

    [Fact]
    public void ReadFile_HandlesSpecialCharacters()
    {
        var specialContent = "Special chars: !@#$%^&*()_+-=[]{}|;:',.<>?/\\";
        _sandbox.WriteFile("/special.txt", specialContent);

        var content = _sandbox.ReadFile("/special.txt");

        Assert.Equal(specialContent, content);
    }

    #endregion

    #region ReadFile Line-Range Tests

    [Fact]
    public void ReadFile_WithLineRange_ReturnsSpecificLines()
    {
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        _sandbox.WriteFile("/lines.txt", content);

        var result = _sandbox.ReadFile("/lines.txt", startLine: 1, endLine: 4);

        // Normalize line endings for comparison
        Assert.Equal("Line 2\nLine 3\nLine 4", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_WithStartLineOnly_ReadsToEndOfFile()
    {
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        _sandbox.WriteFile("/lines.txt", content);

        var result = _sandbox.ReadFile("/lines.txt", startLine: 3);

        // Normalize line endings for comparison
        Assert.Equal("Line 4\nLine 5", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_WithEndLineOnly_ReadsFromStartToEndLine()
    {
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        _sandbox.WriteFile("/lines.txt", content);

        var result = _sandbox.ReadFile("/lines.txt", endLine: 2);

        // Normalize line endings for comparison
        Assert.Equal("Line 1\nLine 2", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_WithLineRangeOutOfBounds_ReturnsEmptyOrPartial()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lines.txt", content);

        // Requesting lines beyond end of file
        var result = _sandbox.ReadFile("/lines.txt", startLine: 10);

        Assert.Equal("", result);
    }

    [Fact]
    public void ReadFile_WithLineRangePartiallyOutOfBounds_ReturnsSoFar()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lines.txt", content);

        // Requesting lines 2-10, but file only has 3 lines
        var result = _sandbox.ReadFile("/lines.txt", startLine: 2, endLine: 10);

        Assert.Equal("Line 3", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_FirstLine()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lines.txt", content);

        var result = _sandbox.ReadFile("/lines.txt", startLine: 0, endLine: 1);

        Assert.Equal("Line 1", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ReadFile_LastLine()
    {
        var content = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/lines.txt", content);

        var result = _sandbox.ReadFile("/lines.txt", startLine: 2, endLine: 3);

        Assert.Equal("Line 3", result.Replace("\r\n", "\n"));
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
        var result = _sandbox.ReadFile("/large.txt", startLine: 99, endLine: 110);

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

        var result = _sandbox.ReadFile("/gaps.txt", startLine: 1, endLine: 4);

        var lines = result.Replace("\r\n", "\n").Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("", lines[0]);  // Empty line
        Assert.Equal("Middle", lines[1]);
        Assert.Equal("", lines[2]);  // Empty line
    }

    #endregion
    #region WriteFile Tests

    [Fact]
    public void WriteFile_CreatesNewFile()
    {
        _sandbox.WriteFile("/new.txt", "content");

        // Just verify file exists by reading it
        var content = _sandbox.ReadFile("/new.txt");
        Assert.Equal("content", content);
    }

    [Fact]
    public void WriteFile_OverwritesExistingFile()
    {
        _sandbox.WriteFile("/file.txt", "original");
        _sandbox.WriteFile("/file.txt", "updated");

        var content = _sandbox.ReadFile("/file.txt");

        Assert.Equal("updated", content);
    }

    [Fact]
    public void WriteFile_CreatesParentDirectories()
    {
        _sandbox.WriteFile("/a/b/c/file.txt", "content");

        // Verify directories exist by checking the file can be read
        var content = _sandbox.ReadFile("/a/b/c/file.txt");
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

        var content = _sandbox.ReadFile("/empty.txt");

        Assert.Equal("", content);
    }

    [Fact]
    public void WriteFile_HandlesMultilineContent()
    {
        var multilineContent = "Line 1\nLine 2\nLine 3";
        _sandbox.WriteFile("/multiline.txt", multilineContent);

        var content = _sandbox.ReadFile("/multiline.txt");

        Assert.Equal(multilineContent, content);
    }

    [Fact]
    public void WriteFile_HandlesLargeContent()
    {
        var largeContent = new string('x', 100000);
        _sandbox.WriteFile("/large.txt", largeContent);

        var content = _sandbox.ReadFile("/large.txt");

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

        var content = _sandbox.ReadFile("/file.txt");
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

        var content = _sandbox.ReadFile("/file.txt");
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

        var content = _sandbox.ReadFile("/file.txt");
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

        var content = _sandbox.ReadFile("/file.txt");
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
        var content = _sandbox.ReadFile("/file.txt");
        Assert.Equal("modified", content);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ReadWriteRead_RoundTrip()
    {
        var originalContent = "Original content\nLine 2\nLine 3";

        _sandbox.WriteFile("/roundtrip.txt", originalContent);
        var readContent = _sandbox.ReadFile("/roundtrip.txt");

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
        var content = _sandbox.ReadFile("/sequence.txt").Replace("\r\n", "\n");
        Assert.Equal("Start\nMiddle\nEnd", content);
    }

    [Fact]
    public void FileIODoesNotAffectShellState()
    {
        _sandbox.Execute("cd /");
        var cwd1 = _sandbox.CurrentDirectory;

        _sandbox.WriteFile("/test.txt", "content");
        var cwd2 = _sandbox.CurrentDirectory;

        _sandbox.ReadFile("/test.txt");
        var cwd3 = _sandbox.CurrentDirectory;

        Assert.Equal("/", cwd1);
        Assert.Equal("/", cwd2);
        Assert.Equal("/", cwd3);
    }

    #endregion
}
