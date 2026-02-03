using AgentSandbox.Core.FileSystem;

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
        Assert.Equal("Hello, World!", fs.ReadFile("/test.txt", System.Text.Encoding.UTF8));
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
        
        Assert.Throws<FileNotFoundException>(() => fs.ReadFile("/nonexistent", System.Text.Encoding.UTF8));
    }

    [Fact]
    public void ReadFile_ThrowsForDirectory()
    {
        var fs = new FileSystem();
        fs.CreateDirectory("/dir");
        
        Assert.Throws<InvalidOperationException>(() => fs.ReadFile("/dir", System.Text.Encoding.UTF8));
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
        Assert.Equal("content", fs.ReadFile("/dest.txt", System.Text.Encoding.UTF8));
    }

    [Fact]
    public void Move_MovesFile()
    {
        var fs = new FileSystem();
        fs.WriteFile("/source.txt", "content");
        
        fs.Move("/source.txt", "/dest.txt");
        
        Assert.False(fs.Exists("/source.txt"));
        Assert.True(fs.Exists("/dest.txt"));
        Assert.Equal("content", fs.ReadFile("/dest.txt", System.Text.Encoding.UTF8));
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
        Assert.True(fs.Exists("/dir/file2.txt"));
        Assert.False(fs.Exists("/file3.txt"));
        Assert.Equal("content1", fs.ReadFile("/file1.txt", System.Text.Encoding.UTF8));
    }

    [Fact]
    public void GetTotalSize_ReturnsCorrectSize()
    {
        var fs = new FileSystem();
        fs.WriteFile("/a.txt", "12345");
        fs.WriteFile("/b.txt", "123");
        
        Assert.Equal(8, fs.TotalSize);
    }
}
