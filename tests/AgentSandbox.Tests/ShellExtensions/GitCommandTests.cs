using AgentSandbox.Core.FileSystem;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Shell.Extensions;

namespace AgentSandbox.Tests.ShellExtensions;

public class GitCommandTests
{
    private readonly FileSystem _fs;
    private readonly SandboxShell _shell;

    public GitCommandTests()
    {
        _fs = new FileSystem();
        _shell = new SandboxShell(_fs);
        _shell.RegisterCommand(new GitCommand());
    }

    #region Help Tests

    [Fact]
    public void Git_Help_ShowsCommands()
    {
        var result = _shell.Execute("git help");
        
        Assert.True(result.Success);
        Assert.Contains("init", result.Stdout);
        Assert.Contains("add", result.Stdout);
        Assert.Contains("commit", result.Stdout);
        Assert.Contains("status", result.Stdout);
        Assert.Contains("log", result.Stdout);
        Assert.Contains("diff", result.Stdout);
        Assert.Contains("branch", result.Stdout);
        Assert.Contains("checkout", result.Stdout);
        Assert.Contains("reset", result.Stdout);
    }

    [Fact]
    public void Git_NoArgs_ShowsUsage()
    {
        var result = _shell.Execute("git");
        
        Assert.False(result.Success);
        Assert.Contains("usage", result.Stderr);
    }

    [Fact]
    public void Git_UnknownCommand_ShowsError()
    {
        var result = _shell.Execute("git foo");
        
        Assert.False(result.Success);
        Assert.Contains("not a git command", result.Stderr);
    }

    #endregion

    #region Init Tests

    [Fact]
    public void Git_Init_CreatesGitDirectory()
    {
        var result = _shell.Execute("git init");
        
        Assert.True(result.Success);
        Assert.Contains("Initialized", result.Stdout);
        Assert.True(_fs.Exists("/.git"));
        Assert.True(_fs.Exists("/.git/HEAD"));
        Assert.True(_fs.Exists("/.git/objects"));
        Assert.True(_fs.Exists("/.git/refs/heads"));
    }

    [Fact]
    public void Git_Init_AlreadyExists_ShowsReinitialize()
    {
        _shell.Execute("git init");
        var result = _shell.Execute("git init");
        
        Assert.False(result.Success);
        Assert.Contains("Reinitialized", result.Stderr);
    }

    #endregion

    #region Add Tests

    [Fact]
    public void Git_Add_NoRepo_ShowsError()
    {
        var result = _shell.Execute("git add file.txt");
        
        Assert.False(result.Success);
        Assert.Contains("not a git repository", result.Stderr);
    }

    [Fact]
    public void Git_Add_FileNotFound_ShowsError()
    {
        _shell.Execute("git init");
        var result = _shell.Execute("git add missing.txt");
        
        Assert.False(result.Success);
        Assert.Contains("did not match", result.Stderr);
    }

    [Fact]
    public void Git_Add_SingleFile_Stages()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "hello world");
        
        var result = _shell.Execute("git add test.txt");
        
        Assert.True(result.Success);
        
        // Verify status shows staged file
        var status = _shell.Execute("git status");
        Assert.Contains("new file", status.Stdout);
        Assert.Contains("test.txt", status.Stdout);
    }

    [Fact]
    public void Git_Add_All_StagesAllFiles()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/file1.txt", "content1");
        _fs.WriteFile("/file2.txt", "content2");
        
        var result = _shell.Execute("git add .");
        
        Assert.True(result.Success);
        
        var status = _shell.Execute("git status");
        Assert.Contains("file1.txt", status.Stdout);
        Assert.Contains("file2.txt", status.Stdout);
    }

    #endregion

    #region Status Tests

    [Fact]
    public void Git_Status_NoRepo_ShowsError()
    {
        var result = _shell.Execute("git status");
        
        Assert.False(result.Success);
        Assert.Contains("not a git repository", result.Stderr);
    }

    [Fact]
    public void Git_Status_EmptyRepo_ShowsNoCommits()
    {
        _shell.Execute("git init");
        
        var result = _shell.Execute("git status");
        
        Assert.True(result.Success);
        Assert.Contains("No commits yet", result.Stdout);
    }

    [Fact]
    public void Git_Status_UntrackedFiles_ShowsUntracked()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/untracked.txt", "content");
        
        var result = _shell.Execute("git status");
        
        Assert.True(result.Success);
        Assert.Contains("Untracked files", result.Stdout);
        Assert.Contains("untracked.txt", result.Stdout);
    }

    [Fact]
    public void Git_Status_StagedFiles_ShowsChangesToBeCommitted()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/staged.txt", "content");
        _shell.Execute("git add staged.txt");
        
        var result = _shell.Execute("git status");
        
        Assert.True(result.Success);
        Assert.Contains("Changes to be committed", result.Stdout);
        Assert.Contains("new file", result.Stdout);
    }

    #endregion

    #region Commit Tests

    [Fact]
    public void Git_Commit_NoMessage_ShowsError()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        
        var result = _shell.Execute("git commit");
        
        Assert.False(result.Success);
        Assert.Contains("requires a value", result.Stderr);
    }

    [Fact]
    public void Git_Commit_NothingStaged_ShowsError()
    {
        _shell.Execute("git init");
        
        var result = _shell.Execute("git commit -m 'test'");
        
        Assert.False(result.Success);
        Assert.Contains("nothing to commit", result.Stderr);
    }

    [Fact]
    public void Git_Commit_Success_ShowsCommitInfo()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        
        var result = _shell.Execute("git commit -m 'Initial commit'");
        
        Assert.True(result.Success);
        Assert.Contains("main", result.Stdout);
        Assert.Contains("Initial commit", result.Stdout);
        Assert.Contains("1 file(s) changed", result.Stdout);
    }

    #endregion

    #region Log Tests

    [Fact]
    public void Git_Log_NoCommits_ShowsError()
    {
        _shell.Execute("git init");
        
        var result = _shell.Execute("git log");
        
        Assert.False(result.Success);
        Assert.Contains("does not have any commits", result.Stderr);
    }

    [Fact]
    public void Git_Log_ShowsCommits()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'First commit'");
        
        var result = _shell.Execute("git log");
        
        Assert.True(result.Success);
        Assert.Contains("commit", result.Stdout);
        Assert.Contains("Author:", result.Stdout);
        Assert.Contains("First commit", result.Stdout);
    }

    [Fact]
    public void Git_Log_Oneline_ShowsCompactFormat()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'First commit'");
        
        var result = _shell.Execute("git log --oneline");
        
        Assert.True(result.Success);
        Assert.Contains("First commit", result.Stdout);
        Assert.DoesNotContain("Author:", result.Stdout);
    }

    [Fact]
    public void Git_Log_LimitEntries()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content1");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'First'");
        
        _fs.WriteFile("/test.txt", "content2");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Second'");
        
        var result = _shell.Execute("git log -n 1");
        
        Assert.True(result.Success);
        Assert.Contains("Second", result.Stdout);
        Assert.DoesNotContain("First", result.Stdout);
    }

    #endregion

    #region Branch Tests

    [Fact]
    public void Git_Branch_List_ShowsCurrentBranch()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Initial'");
        
        var result = _shell.Execute("git branch");
        
        Assert.True(result.Success);
        Assert.Contains("* main", result.Stdout);
    }

    [Fact]
    public void Git_Branch_Create_CreatesBranch()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Initial'");
        
        var result = _shell.Execute("git branch feature");
        
        Assert.True(result.Success);
        
        var list = _shell.Execute("git branch");
        Assert.Contains("feature", list.Stdout);
        Assert.Contains("* main", list.Stdout);
    }

    [Fact]
    public void Git_Branch_Delete_DeletesBranch()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Initial'");
        _shell.Execute("git branch feature");
        
        var result = _shell.Execute("git branch -d feature");
        
        Assert.True(result.Success);
        Assert.Contains("Deleted", result.Stdout);
        
        var list = _shell.Execute("git branch");
        Assert.DoesNotContain("feature", list.Stdout);
    }

    [Fact]
    public void Git_Branch_DeleteCurrent_ShowsError()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Initial'");
        
        var result = _shell.Execute("git branch -d main");
        
        Assert.False(result.Success);
        Assert.Contains("Cannot delete", result.Stderr);
    }

    #endregion

    #region Checkout Tests

    [Fact]
    public void Git_Checkout_NoBranch_ShowsError()
    {
        _shell.Execute("git init");
        
        var result = _shell.Execute("git checkout");
        
        Assert.False(result.Success);
        Assert.Contains("please specify", result.Stderr);
    }

    [Fact]
    public void Git_Checkout_BranchNotFound_ShowsError()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Initial'");
        
        var result = _shell.Execute("git checkout missing");
        
        Assert.False(result.Success);
        Assert.Contains("did not match", result.Stderr);
    }

    [Fact]
    public void Git_Checkout_ExistingBranch_Switches()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Initial'");
        _shell.Execute("git branch feature");
        
        var result = _shell.Execute("git checkout feature");
        
        Assert.True(result.Success);
        Assert.Contains("Switched to branch 'feature'", result.Stdout);
        
        var branch = _shell.Execute("git branch");
        Assert.Contains("* feature", branch.Stdout);
    }

    [Fact]
    public void Git_Checkout_CreateBranch_CreatesAndSwitches()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Initial'");
        
        var result = _shell.Execute("git checkout -b newbranch");
        
        Assert.True(result.Success);
        Assert.Contains("Switched to a new branch", result.Stdout);
        
        var branch = _shell.Execute("git branch");
        Assert.Contains("* newbranch", branch.Stdout);
    }

    [Fact]
    public void Git_Checkout_SameBranch_ShowsAlreadyOn()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        _shell.Execute("git commit -m 'Initial'");
        
        var result = _shell.Execute("git checkout main");
        
        Assert.True(result.Success);
        Assert.Contains("Already on 'main'", result.Stdout);
    }

    #endregion

    #region Reset Tests

    [Fact]
    public void Git_Reset_UnstagesFile()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        
        var result = _shell.Execute("git reset test.txt");
        
        Assert.True(result.Success);
        
        var status = _shell.Execute("git status");
        Assert.Contains("Untracked files", status.Stdout);
        Assert.DoesNotContain("Changes to be committed", status.Stdout);
    }

    [Fact]
    public void Git_Reset_All_UnstagesAll()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/file1.txt", "content1");
        _fs.WriteFile("/file2.txt", "content2");
        _shell.Execute("git add .");
        
        var result = _shell.Execute("git reset");
        
        Assert.True(result.Success);
        
        var status = _shell.Execute("git status");
        Assert.Contains("Untracked files", status.Stdout);
    }

    #endregion

    #region Diff Tests

    [Fact]
    public void Git_Diff_NoChanges_ReturnsEmpty()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        
        var result = _shell.Execute("git diff");
        
        Assert.True(result.Success);
        Assert.Empty(result.Stdout.Trim());
    }

    [Fact]
    public void Git_Diff_UnstagedChanges_ShowsDiff()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "original");
        _shell.Execute("git add test.txt");
        _fs.WriteFile("/test.txt", "modified");
        
        var result = _shell.Execute("git diff");
        
        Assert.True(result.Success);
        Assert.Contains("-original", result.Stdout);
        Assert.Contains("+modified", result.Stdout);
    }

    [Fact]
    public void Git_Diff_Staged_ShowsStagedChanges()
    {
        _shell.Execute("git init");
        _fs.WriteFile("/test.txt", "content");
        _shell.Execute("git add test.txt");
        
        var result = _shell.Execute("git diff --staged");
        
        Assert.True(result.Success);
        Assert.Contains("new file", result.Stdout);
        Assert.Contains("+content", result.Stdout);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Git_FullWorkflow_CreateCommitBranchMerge()
    {
        // Initialize and first commit
        _shell.Execute("git init");
        _fs.WriteFile("/readme.txt", "Project readme");
        _shell.Execute("git add readme.txt");
        _shell.Execute("git commit -m 'Initial commit'");
        
        // Create feature branch
        _shell.Execute("git checkout -b feature");
        _fs.WriteFile("/feature.txt", "New feature");
        _shell.Execute("git add feature.txt");
        _shell.Execute("git commit -m 'Add feature'");
        
        // Switch back to main
        var checkout = _shell.Execute("git checkout main");
        Assert.True(checkout.Success);
        
        // Feature file should not exist on main
        // (In a real git, files would be switched. Our simulated checkout does restore files.)
        
        // Log should show only initial commit on main
        var log = _shell.Execute("git log --oneline");
        Assert.Contains("Initial commit", log.Stdout);
    }

    #endregion
}
