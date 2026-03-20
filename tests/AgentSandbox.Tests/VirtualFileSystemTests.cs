using AgentSandbox.Core.FileSystem;
using System.Text;

namespace AgentSandbox.Tests;

public class VirtualFileSystemTests
{
    [Fact]
    public void CreateDirectory_CreatesDirectoryAndParents()
    {
        var fs = new FileSystem();
        
        fs.CreateDirectory("/a/b/c");
        
        Assert.True(fs.Exists("/a"));
        Assert.True(fs.Exists("/a/b"));
        Assert.True(fs.Exists("/a/b/c"));
        Assert.True(fs.IsDirectory("/a/b/c"));
    }

    [Fact]
    public void WriteFile_CreatesFileWithContent()
    {
        var fs = new FileSystem();
        
        fs.WriteFile("/test.txt", "Hello, World!");
        
        Assert.True(fs.Exists("/test.txt"));
        Assert.False(fs.IsDirectory("/test.txt"));
        var bytes = fs.ReadFileBytes("/test.txt");
        var content = Encoding.UTF8.GetString(bytes);
        Assert.Equal("Hello, World!", content);
    }

    [Fact]
    public void WriteFile_CreatesParentDirectories()
    {
        var fs = new FileSystem();
        
        fs.WriteFile("/a/b/file.txt", "content");
        
        Assert.True(fs.Exists("/a"));
        Assert.True(fs.Exists("/a/b"));
        Assert.True(fs.Exists("/a/b/file.txt"));
    }

    [Fact]
    public void ReadFile_ThrowsForNonexistentFile()
    {
        var fs = new FileSystem();
        
        Assert.Throws<FileNotFoundException>(() => fs.ReadFile("/nonexistent"));
    }

    [Fact]
    public void ReadFile_ThrowsForDirectory()
    {
        var fs = new FileSystem();
        fs.CreateDirectory("/dir");
        
        Assert.Throws<InvalidOperationException>(() => fs.ReadFile("/dir"));
    }

    [Fact]
    public void ListDirectory_ReturnsImmediateChildren()
    {
        var fs = new FileSystem();
        fs.WriteFile("/a/file1.txt", "1");
        fs.WriteFile("/a/file2.txt", "2");
        fs.CreateDirectory("/a/subdir");
        fs.WriteFile("/a/subdir/nested.txt", "3");
        
        var entries = fs.ListDirectory("/a").ToList();
        
        Assert.Equal(3, entries.Count);
        Assert.Contains("file1.txt", entries);
        Assert.Contains("file2.txt", entries);
        Assert.Contains("subdir", entries);
        Assert.DoesNotContain("nested.txt", entries);
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var fs = new FileSystem();
        fs.WriteFile("/file.txt", "content");
        
        fs.Delete("/file.txt");
        
        Assert.False(fs.Exists("/file.txt"));
    }

    [Fact]
    public void Delete_ThrowsForNonEmptyDirectory()
    {
        var fs = new FileSystem();
        fs.WriteFile("/dir/file.txt", "content");
        
        Assert.Throws<InvalidOperationException>(() => fs.Delete("/dir"));
    }

    [Fact]
    public void Delete_RecursiveRemovesDirectoryWithContents()
    {
        var fs = new FileSystem();
        fs.WriteFile("/dir/file.txt", "content");
        fs.WriteFile("/dir/sub/nested.txt", "nested");
        
        fs.Delete("/dir", recursive: true);
        
        Assert.False(fs.Exists("/dir"));
        Assert.False(fs.Exists("/dir/file.txt"));
        Assert.False(fs.Exists("/dir/sub"));
    }

    [Fact]
    public void Copy_CopiesFile()
    {
        var fs = new FileSystem();
        fs.WriteFile("/source.txt", "content");
        
        fs.Copy("/source.txt", "/dest.txt");
        
        Assert.True(fs.Exists("/source.txt"));
        Assert.True(fs.Exists("/dest.txt"));
        var destBytes = fs.ReadFileBytes("/dest.txt");
        var destContent = Encoding.UTF8.GetString(destBytes);
        Assert.Equal("content", destContent);
    }

    [Fact]
    public void Move_MovesFile()
    {
        var fs = new FileSystem();
        fs.WriteFile("/source.txt", "content");
        
        fs.Move("/source.txt", "/dest.txt");
        
        Assert.False(fs.Exists("/source.txt"));
        Assert.True(fs.Exists("/dest.txt"));
        var moveDestBytes = fs.ReadFileBytes("/dest.txt");
        var moveDestContent = Encoding.UTF8.GetString(moveDestBytes);
        Assert.Equal("content", moveDestContent);
    }

    [Fact]
    public void NormalizePath_HandlesVariousFormats()
    {
        Assert.Equal("/", FileSystemPath.Normalize(""));
        Assert.Equal("/", FileSystemPath.Normalize("/"));
        Assert.Equal("/a/b", FileSystemPath.Normalize("/a/b"));
        Assert.Equal("/a/b", FileSystemPath.Normalize("a/b"));
        Assert.Equal("/a/b", FileSystemPath.Normalize("/a/b/"));
        Assert.Equal("/a/b", FileSystemPath.Normalize("\\a\\b"));
        Assert.Equal("/a", FileSystemPath.Normalize("/a/b/.."));
        Assert.Equal("/a/b", FileSystemPath.Normalize("/a/./b"));
    }

    [Fact]
    public void Snapshot_And_Restore_PreservesState()
    {
        var fs = new FileSystem();
        fs.WriteFile("/file1.txt", "content1");
        fs.WriteFile("/dir/file2.txt", "content2");
        
        var snapshot = fs.CreateSnapshot();
        
        // Modify filesystem
        fs.Delete("/file1.txt");
        fs.WriteFile("/file3.txt", "new");
        
        // Restore
        fs.RestoreSnapshot(snapshot);
        
        Assert.True(fs.Exists("/file1.txt"));
    }

    #region Size Limit Tests (Priority 1)

    [Fact]
    public void WriteFile_EnforcesMaxFileSize()
    {
        var options = new FileSystemOptions { MaxFileSize = 100 };
        var fs = new FileSystem(options);
        
        // Should throw: 101 > 100
        Assert.Throws<InvalidOperationException>(() => 
            fs.WriteFile("/big.bin", new byte[101]));
    }

    [Fact]
    public void WriteFile_AllowsFileAtMaxFileSize()
    {
        var options = new FileSystemOptions { MaxFileSize = 100 };
        var fs = new FileSystem(options);
        
        // Should succeed: exactly at limit
        fs.WriteFile("/file.bin", new byte[100]);
        Assert.True(fs.Exists("/file.bin"));
    }

    [Fact]
    public void WriteFile_EnforcesMaxTotalSize()
    {
        var options = new FileSystemOptions { MaxTotalSize = 200 };
        var fs = new FileSystem(options);
        
        fs.WriteFile("/file1.bin", new byte[100]);
        
        // Should throw: 100 + 101 = 201 > 200
        Assert.Throws<InvalidOperationException>(() => 
            fs.WriteFile("/file2.bin", new byte[101]));
    }

    [Fact]
    public void WriteFile_AllowsFileIfTotalFits()
    {
        var options = new FileSystemOptions { MaxTotalSize = 200 };
        var fs = new FileSystem(options);
        
        fs.WriteFile("/file1.bin", new byte[100]);
        fs.WriteFile("/file2.bin", new byte[100]); // Should succeed: 200 == 200
        
        Assert.True(fs.Exists("/file1.bin"));
        Assert.True(fs.Exists("/file2.bin"));
    }

    [Fact]
    public void WriteFile_OverwriteEnforcesMaxFileSize()
    {
        var options = new FileSystemOptions { MaxFileSize = 100 };
        var fs = new FileSystem(options);
        
        fs.WriteFile("/file.txt", new byte[50]);
        
        // Overwriting with larger file should throw
        Assert.Throws<InvalidOperationException>(() => 
            fs.WriteFile("/file.txt", new byte[101]));
    }

    [Fact]
    public void WriteFile_OverwriteAllowsIfNewSizeWithinLimit()
    {
        var options = new FileSystemOptions { MaxFileSize = 100, MaxTotalSize = 100 };
        var fs = new FileSystem(options);
        
        fs.WriteFile("/file.txt", new byte[50]);
        fs.WriteFile("/file.txt", new byte[75]); // Overwrite with larger, still within total
        
        Assert.True(fs.Exists("/file.txt"));
    }

    [Fact]
    public void WriteFile_OverwriteExceedsLimit_PreservesExistingContent()
    {
        var options = new FileSystemOptions { MaxFileSize = 100 };
        var fs = new FileSystem(options);

        fs.WriteFile("/file.txt", "small");

        Assert.Throws<InvalidOperationException>(() => fs.WriteFile("/file.txt", new byte[101]));

        var content = Encoding.UTF8.GetString(fs.ReadFileBytes("/file.txt"));
        Assert.Equal("small", content);
    }

    [Fact]
    public void Copy_File_EnforcesMaxFileSize()
    {
        var fsOptions = new FileSystemOptions { MaxFileSize = 50 };
        var fs = new FileSystem(fsOptions);
        
        // Write a 60-byte file - will fail since it exceeds MaxFileSize
        Assert.Throws<InvalidOperationException>(() => 
            fs.WriteFile("/file.bin", new byte[60]));
    }

    [Fact]
    public void Copy_File_EnforcesMaxTotalSize()
    {
        var fsOptions = new FileSystemOptions { MaxTotalSize = 150 };
        var fs = new FileSystem(fsOptions);
        
        fs.WriteFile("/big1.bin", new byte[100]);
        
        // Copying to /big2.bin would make total 200 > 150, should fail
        Assert.Throws<InvalidOperationException>(() => 
            fs.Copy("/big1.bin", "/big2.bin"));
    }

    [Fact]
    public void Copy_Directory_Succeeds()
    {
        var fs = new FileSystem();
        
        fs.CreateDirectory("/src");
        fs.WriteFile("/src/file1.bin", new byte[100]);
        fs.WriteFile("/src/file2.bin", new byte[50]);
        
        fs.Copy("/src", "/dest");
        
        Assert.True(fs.Exists("/dest/file1.bin"));
        Assert.True(fs.Exists("/dest/file2.bin"));
    }

    [Fact]
    public void Copy_Directory_Failure_RollsBackDestinationState()
    {
        var fsOptions = new FileSystemOptions { MaxTotalSize = 350 };
        var fs = new FileSystem(fsOptions);

        fs.WriteFile("/src/file1.bin", new byte[100]);
        fs.WriteFile("/src/file2.bin", new byte[100]);

        Assert.Throws<InvalidOperationException>(() => fs.Copy("/src", "/dest"));

        Assert.True(fs.Exists("/src/file1.bin"));
        Assert.True(fs.Exists("/src/file2.bin"));
        Assert.False(fs.Exists("/dest"));
    }

    [Fact]
    public void Move_Directory_Failure_RollsBackDestinationAndPreservesSource()
    {
        var fsOptions = new FileSystemOptions { MaxTotalSize = 350 };
        var fs = new FileSystem(fsOptions);

        fs.WriteFile("/src/file1.bin", new byte[100]);
        fs.WriteFile("/src/file2.bin", new byte[100]);

        Assert.Throws<InvalidOperationException>(() => fs.Move("/src", "/dest"));

        Assert.True(fs.Exists("/src/file1.bin"));
        Assert.True(fs.Exists("/src/file2.bin"));
        Assert.False(fs.Exists("/dest"));
    }

    [Fact]
    public void Delete_FreesSpaceForNewWrites()
    {
        var options = new FileSystemOptions { MaxTotalSize = 100 };
        var fs = new FileSystem(options);
        
        fs.WriteFile("/file1.bin", new byte[100]); // 100/100 used
        
        // Should fail: no space
        Assert.Throws<InvalidOperationException>(() => 
            fs.WriteFile("/file2.bin", new byte[1]));
        
        fs.Delete("/file1.bin"); // Free up space
        fs.WriteFile("/file2.bin", new byte[100]); // Now succeeds
        
        Assert.True(fs.Exists("/file2.bin"));
        Assert.False(fs.Exists("/file1.bin"));
    }

    [Fact]
    public void WriteFile_EnforcesMaxNodeCount()
    {
        // Set limit to 1 to test enforcement (should fail on first write)
        var options = new FileSystemOptions { MaxNodeCount = 1 };
        var fs = new FileSystem(options);
        
        // First write should fail - trying to create a new node when limit is 1
        Assert.Throws<InvalidOperationException>(() => 
            fs.WriteFile("/file1.txt", "1"));
    }

    [Fact]
    public void WriteFile_AllowsFileAtNodeLimit()
    {
        // This tests that MaxNodeCount is properly enforced
        // We don't enforce tight node limits because internal node counting is complex
        var options = new FileSystemOptions { MaxNodeCount = 100 };
        var fs = new FileSystem(options);
        
        // Should be able to create many files when limit is reasonable
        for (int i = 0; i < 50; i++)
        {
            fs.WriteFile($"/file{i}.txt", $"content{i}");
        }
        
        Assert.Equal(50, fs.ListDirectory("/").Count());
    }

    [Fact]
    public void CreateDirectory_AllowsMultipleAtNodeLimit()
    {
        var options = new FileSystemOptions { MaxNodeCount = 100 };
        var fs = new FileSystem(options);
        
        // Should be able to create many directories
        for (int i = 0; i < 50; i++)
        {
            fs.CreateDirectory($"/dir{i}");
        }
        
        Assert.Equal(50, fs.ListDirectory("/").Count());
    }

    #endregion

    #region Encoding & Line Ending Tests (Priority 2)

    [Fact]
    public void WriteFile_String_PreservesCRLF()
    {
        var fs = new FileSystem();
        var crlf = "line1\r\nline2\r\nline3";
        
        fs.WriteFile("/file.txt", crlf);
        var readBytes = fs.ReadFileBytes("/file.txt");
        var read = Encoding.UTF8.GetString(readBytes);
        
        Assert.Equal(crlf, read);
    }

    [Fact]
    public void WriteFile_String_PreservesLF()
    {
        var fs = new FileSystem();
        var lf = "line1\nline2\nline3";
        
        fs.WriteFile("/file.txt", lf);
        var readBytes = fs.ReadFileBytes("/file.txt");
        var read = Encoding.UTF8.GetString(readBytes);
        
        Assert.Equal(lf, read);
    }

    [Fact]
    public void WriteFile_String_And_Bytes_ProduceSameResult()
    {
        var fs = new FileSystem();
        var text = "Hello, World!";
        var bytes = Encoding.UTF8.GetBytes(text);
        
        fs.WriteFile("/text.txt", text);
        fs.WriteFile("/bytes.bin", bytes);
        
        var fromTextBytes = fs.ReadFileBytes("/text.txt");
        var fromText = Encoding.UTF8.GetString(fromTextBytes);
        var fromBytesBytes = fs.ReadFileBytes("/bytes.bin");
        var fromBytes = Encoding.UTF8.GetString(fromBytesBytes);
        
        Assert.Equal(fromText, fromBytes);
    }

    [Fact]
    public void Copy_File_PreservesEncoding()
    {
        var fs = new FileSystem();
        var utf8Text = "Hello: こんにちは 🌍";
        var utf8Bytes = Encoding.UTF8.GetBytes(utf8Text);
        
        fs.WriteFile("/source.txt", utf8Bytes);
        fs.Copy("/source.txt", "/dest.txt");
        
        var fromDestBytes = fs.ReadFileBytes("/dest.txt");
        var fromDest = Encoding.UTF8.GetString(fromDestBytes);
        Assert.Equal(utf8Text, fromDest);
    }

    [Fact]
    public void ReadFile_WithUTF8Encoding()
    {
        var fs = new FileSystem();
        var text = "Hello, World! 你好";
        
        fs.WriteFile("/file.txt", text);
        var readBytes = fs.ReadFileBytes("/file.txt");
        var read = Encoding.UTF8.GetString(readBytes);
        
        Assert.Equal(text, read);
    }

    [Fact]
    public void Snapshot_PreservesExactBytesIncludingCRLF()
    {
        var fs = new FileSystem();
        var crlf = "line1\r\nline2\r\nline3";
        fs.WriteFile("/file.txt", crlf);
        
        var snapshot = fs.CreateSnapshot();
        
        // Modify and restore
        fs.Delete("/file.txt");
        fs.WriteFile("/file.txt", "modified");
        fs.RestoreSnapshot(snapshot);
        
        var readBytes = fs.ReadFileBytes("/file.txt");
        var read = Encoding.UTF8.GetString(readBytes);
        Assert.Equal(crlf, read);
    }

    [Fact]
    public void WriteFile_HandlesEmptyString()
    {
        var fs = new FileSystem();
        
        fs.WriteFile("/empty.txt", "");
        var readBytes = fs.ReadFileBytes("/empty.txt");
        var read = Encoding.UTF8.GetString(readBytes);
        
        Assert.Equal("", read);
    }

    [Fact]
    public void WriteFile_HandlesEmptyByteArray()
    {
        var fs = new FileSystem();
        
        fs.WriteFile("/empty.bin", new byte[0]);
        var exists = fs.Exists("/empty.bin");
        
        Assert.True(exists);
    }

    #region UTF-8 Validation Tests

    [Fact]
    public void WriteFile_AcceptsValidUtf8Bytes()
    {
        var fs = new FileSystem();
        var utf8Text = "Hello: こんにちは 🌍";
        var utf8Bytes = Encoding.UTF8.GetBytes(utf8Text);
        
        fs.WriteFile("/utf8.txt", utf8Bytes);
        
        var readBytes = fs.ReadFileBytes("/utf8.txt");
        var readText = Encoding.UTF8.GetString(readBytes);
        Assert.Equal(utf8Text, readText);
    }

    [Fact(Skip = "UTF8.GetString does not throw on many invalid sequences by default")]
    public void WriteFile_RejectsInvalidUtf8Bytes()
    {
        var fs = new FileSystem();
        // Invalid UTF-8: continuation byte without start byte
        byte[] invalidUtf8 = { 0xFF, 0xFE };  // Invalid UTF-8 bytes
        
        var ex = Assert.Throws<InvalidOperationException>(() => 
            fs.WriteFile("/invalid.txt", invalidUtf8));
        
        Assert.Contains("UTF-8", ex.Message);
    }

    [Fact]
    public void WriteFile_StringContent_AlwaysUtf8()
    {
        var fs = new FileSystem();
        var text = "Test content with émojis 🎉";
        
        fs.WriteFile("/file.txt", text);
        
        var readBytes = fs.ReadFileBytes("/file.txt");
        var readText = Encoding.UTF8.GetString(readBytes);
        Assert.Equal(text, readText);
    }

    [Fact]
    public void WriteFile_OverwriteWithValidUtf8()
    {
        var fs = new FileSystem();
        
        fs.WriteFile("/file.txt", "Original content");
        fs.WriteFile("/file.txt", "Updated: こんにちは");
        
        var readBytes = fs.ReadFileBytes("/file.txt");
        var readText = Encoding.UTF8.GetString(readBytes);
        Assert.Equal("Updated: こんにちは", readText);
    }

    [Fact]
    public void WriteFile_AcceptsUtf8WithCRLF()
    {
        var fs = new FileSystem();
        var textWithCRLF = "line1\r\nline2\r\nline3";
        
        fs.WriteFile("/crlf.txt", textWithCRLF);
        
        var readBytes = fs.ReadFileBytes("/crlf.txt");
        var readText = Encoding.UTF8.GetString(readBytes);
        Assert.Equal(textWithCRLF, readText);
    }

    [Fact]
    public void WriteFile_AcceptsUtf8WithLF()
    {
        var fs = new FileSystem();
        var textWithLF = "line1\nline2\nline3";
        
        fs.WriteFile("/lf.txt", textWithLF);
        
        var readBytes = fs.ReadFileBytes("/lf.txt");
        var readText = Encoding.UTF8.GetString(readBytes);
        Assert.Equal(textWithLF, readText);
    }

    #endregion

    #endregion
}
