using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentSandbox.Core.FileSystem;

namespace AgentSandbox.Core.Shell.Extensions;

/// <summary>
/// Simulated git command for version control within the sandbox.
/// Stores repository data in .git/ directory using the virtual filesystem.
/// </summary>
public class GitCommand : IShellCommand
{
    public string Name => "git";
    public IReadOnlyList<string> Aliases => Array.Empty<string>();
    public string Description => "Simulated version control system";
    public string Usage => "git <command> [options]\nRun 'git help' for available commands.";

    private const string GitDir = ".git";
    private const string HeadFile = ".git/HEAD";
    private const string IndexFile = ".git/index.json";
    private const string ConfigFile = ".git/config.json";
    private const string ObjectsDir = ".git/objects";
    private const string RefsHeadsDir = ".git/refs/heads";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ShellResult Execute(string[] args, IShellContext context)
    {
        if (args.Length == 0)
        {
            return ShellResult.Error("usage: git <command> [<args>]\n\nRun 'git help' for available commands.");
        }

        var command = args[0].ToLowerInvariant();
        var cmdArgs = args.Skip(1).ToArray();

        return command switch
        {
            "init" => CmdInit(cmdArgs, context),
            "add" => CmdAdd(cmdArgs, context),
            "status" => CmdStatus(cmdArgs, context),
            "commit" => CmdCommit(cmdArgs, context),
            "log" => CmdLog(cmdArgs, context),
            "diff" => CmdDiff(cmdArgs, context),
            "branch" => CmdBranch(cmdArgs, context),
            "checkout" => CmdCheckout(cmdArgs, context),
            "reset" => CmdReset(cmdArgs, context),
            "help" or "--help" or "-h" => CmdHelp(cmdArgs, context),
            _ => ShellResult.Error($"git: '{command}' is not a git command. See 'git help'.")
        };
    }

    #region Commands

    private ShellResult CmdHelp(string[] args, IShellContext context)
    {
        var help = """
            usage: git <command> [<args>]

            Available commands:

              init        Initialize a new git repository
                          Usage: git init

              add         Add file contents to the staging area
                          Usage: git add <file>...
                                 git add .          (add all files)
                                 git add -A         (add all files)

              status      Show the working tree status
                          Usage: git status

              commit      Record changes to the repository
                          Usage: git commit -m <message>

              log         Show commit logs
                          Usage: git log
                                 git log -n <count>  (limit entries)
                                 git log --oneline   (compact format)

              diff        Show changes between commits, staging, and working tree
                          Usage: git diff              (unstaged changes)
                                 git diff --staged     (staged changes)
                                 git diff <file>       (specific file)

              branch      List, create, or delete branches
                          Usage: git branch            (list branches)
                                 git branch <name>     (create branch)
                                 git branch -d <name>  (delete branch)

              checkout    Switch branches or restore working tree files
                          Usage: git checkout <branch>
                                 git checkout -b <new-branch>  (create and switch)

              reset       Unstage files
                          Usage: git reset <file>...
                                 git reset            (unstage all)

              help        Show this help message
                          Usage: git help
            """;
        return ShellResult.Ok(help);
    }

    private ShellResult CmdInit(string[] args, IShellContext context)
    {
        var gitPath = context.ResolvePath(GitDir);
        
        if (context.FileSystem.Exists(gitPath))
        {
            return ShellResult.Error($"Reinitialized existing Git repository in {gitPath}/");
        }

        // Create .git structure
        context.FileSystem.CreateDirectory(gitPath);
        context.FileSystem.CreateDirectory(context.ResolvePath(ObjectsDir));
        context.FileSystem.CreateDirectory(context.ResolvePath(RefsHeadsDir));

        // Initialize HEAD to point to main branch
        context.FileSystem.WriteFile(context.ResolvePath(HeadFile), "ref: refs/heads/main");

        // Create empty index
        var index = new GitIndex { Entries = new Dictionary<string, GitIndexEntry>() };
        SaveIndex(context, index);

        // Create config
        var config = new GitConfig
        {
            User = new GitUser { Name = "Sandbox User", Email = "user@sandbox.local" }
        };
        context.FileSystem.WriteFile(context.ResolvePath(ConfigFile), JsonSerializer.Serialize(config, JsonOptions));

        return ShellResult.Ok($"Initialized empty Git repository in {gitPath}/");
    }

    private ShellResult CmdAdd(string[] args, IShellContext context)
    {
        if (!IsGitRepo(context))
            return NotARepoError();

        if (args.Length == 0)
            return ShellResult.Error("Nothing specified, nothing added.");

        var index = LoadIndex(context);
        var addedFiles = new List<string>();

        foreach (var pattern in args)
        {
            if (pattern == "." || pattern == "-A" || pattern == "--all")
            {
                // Add all files recursively
                AddAllFiles(context, context.CurrentDirectory, index, addedFiles);
            }
            else
            {
                var path = context.ResolvePath(pattern);
                if (!context.FileSystem.Exists(path))
                {
                    return ShellResult.Error($"fatal: pathspec '{pattern}' did not match any files");
                }

                if (context.FileSystem.IsDirectory(path))
                {
                    AddAllFiles(context, path, index, addedFiles);
                }
                else
                {
                    AddFile(context, path, index, addedFiles);
                }
            }
        }

        SaveIndex(context, index);

        return ShellResult.Ok();
    }

    private ShellResult CmdStatus(string[] args, IShellContext context)
    {
        if (!IsGitRepo(context))
            return NotARepoError();

        var sb = new StringBuilder();
        var branch = GetCurrentBranch(context);
        sb.AppendLine($"On branch {branch}");

        var index = LoadIndex(context);
        var headCommit = GetHeadCommit(context);

        // Get files in HEAD commit
        var headFiles = new Dictionary<string, string>();
        if (headCommit != null)
        {
            foreach (var entry in headCommit.Files)
            {
                headFiles[entry.Key] = entry.Value;
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("No commits yet");
        }

        // Staged changes (index vs HEAD)
        var stagedNew = new List<string>();
        var stagedModified = new List<string>();
        var stagedDeleted = new List<string>();

        foreach (var entry in index.Entries)
        {
            if (!headFiles.ContainsKey(entry.Key))
                stagedNew.Add(entry.Key);
            else if (headFiles[entry.Key] != entry.Value.Hash)
                stagedModified.Add(entry.Key);
        }

        foreach (var file in headFiles.Keys)
        {
            if (!index.Entries.ContainsKey(file))
                stagedDeleted.Add(file);
        }

        // Unstaged changes (working tree vs index)
        var unstagedModified = new List<string>();
        var unstagedDeleted = new List<string>();
        var untracked = new List<string>();

        var allFiles = GetAllFiles(context, context.ResolvePath("/"));
        foreach (var file in allFiles)
        {
            if (file.StartsWith("/.git/")) continue;

            if (index.Entries.TryGetValue(file, out var entry))
            {
                var currentHash = HashFile(context, file);
                if (currentHash != entry.Hash)
                    unstagedModified.Add(file);
            }
            else if (!headFiles.ContainsKey(file))
            {
                untracked.Add(file);
            }
        }

        foreach (var file in index.Entries.Keys)
        {
            var path = context.ResolvePath(file);
            if (!context.FileSystem.Exists(path))
                unstagedDeleted.Add(file);
        }

        // Output
        if (stagedNew.Count > 0 || stagedModified.Count > 0 || stagedDeleted.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Changes to be committed:");
            sb.AppendLine("  (use \"git reset <file>...\" to unstage)");
            sb.AppendLine();
            foreach (var f in stagedNew) sb.AppendLine($"\tnew file:   {f}");
            foreach (var f in stagedModified) sb.AppendLine($"\tmodified:   {f}");
            foreach (var f in stagedDeleted) sb.AppendLine($"\tdeleted:    {f}");
        }

        if (unstagedModified.Count > 0 || unstagedDeleted.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Changes not staged for commit:");
            sb.AppendLine("  (use \"git add <file>...\" to update what will be committed)");
            sb.AppendLine();
            foreach (var f in unstagedModified) sb.AppendLine($"\tmodified:   {f}");
            foreach (var f in unstagedDeleted) sb.AppendLine($"\tdeleted:    {f}");
        }

        if (untracked.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Untracked files:");
            sb.AppendLine("  (use \"git add <file>...\" to include in what will be committed)");
            sb.AppendLine();
            foreach (var f in untracked) sb.AppendLine($"\t{f}");
        }

        if (stagedNew.Count == 0 && stagedModified.Count == 0 && stagedDeleted.Count == 0 &&
            unstagedModified.Count == 0 && unstagedDeleted.Count == 0 && untracked.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("nothing to commit, working tree clean");
        }

        return ShellResult.Ok(sb.ToString().TrimEnd());
    }

    private ShellResult CmdCommit(string[] args, IShellContext context)
    {
        if (!IsGitRepo(context))
            return NotARepoError();

        string? message = null;
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-m" || args[i] == "--message") && i + 1 < args.Length)
            {
                message = args[++i];
            }
        }

        if (string.IsNullOrEmpty(message))
            return ShellResult.Error("error: switch 'm' requires a value");

        var index = LoadIndex(context);
        var headCommit = GetHeadCommit(context);

        // Check if there are staged changes
        var headFiles = headCommit?.Files ?? new Dictionary<string, string>();
        var hasChanges = false;

        foreach (var entry in index.Entries)
        {
            if (!headFiles.TryGetValue(entry.Key, out var hash) || hash != entry.Value.Hash)
            {
                hasChanges = true;
                break;
            }
        }

        if (!hasChanges && headFiles.Count == index.Entries.Count)
        {
            return ShellResult.Error("nothing to commit, working tree clean");
        }

        // Create commit
        var config = LoadConfig(context);
        var commit = new GitCommit
        {
            Message = message,
            Author = $"{config.User.Name} <{config.User.Email}>",
            Timestamp = DateTime.UtcNow,
            Parent = headCommit?.Hash,
            Files = index.Entries.ToDictionary(e => e.Key, e => e.Value.Hash)
        };

        // Generate commit hash
        var commitJson = JsonSerializer.Serialize(commit, JsonOptions);
        commit.Hash = ComputeHash(commitJson);

        // Save commit object
        var commitPath = context.ResolvePath($"{ObjectsDir}/{commit.Hash}");
        context.FileSystem.WriteFile(commitPath, JsonSerializer.Serialize(commit, JsonOptions));

        // Update branch ref
        var branch = GetCurrentBranch(context);
        var refPath = context.ResolvePath($"{RefsHeadsDir}/{branch}");
        context.FileSystem.WriteFile(refPath, commit.Hash);

        var shortHash = commit.Hash[..7];
        var filesChanged = index.Entries.Count;
        return ShellResult.Ok($"[{branch} {shortHash}] {message}\n {filesChanged} file(s) changed");
    }

    private ShellResult CmdLog(string[] args, IShellContext context)
    {
        if (!IsGitRepo(context))
            return NotARepoError();

        var oneline = args.Contains("--oneline");
        var limit = int.MaxValue;
        
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-n" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
            {
                limit = n;
            }
        }

        var commit = GetHeadCommit(context);
        if (commit == null)
        {
            return ShellResult.Error("fatal: your current branch does not have any commits yet");
        }

        var sb = new StringBuilder();
        var count = 0;

        while (commit != null && count < limit)
        {
            if (oneline)
            {
                sb.AppendLine($"{commit.Hash[..7]} {commit.Message}");
            }
            else
            {
                sb.AppendLine($"commit {commit.Hash}");
                sb.AppendLine($"Author: {commit.Author}");
                sb.AppendLine($"Date:   {commit.Timestamp:ddd MMM dd HH:mm:ss yyyy} +0000");
                sb.AppendLine();
                sb.AppendLine($"    {commit.Message}");
                sb.AppendLine();
            }

            count++;

            if (!string.IsNullOrEmpty(commit.Parent))
            {
                commit = LoadCommit(context, commit.Parent);
            }
            else
            {
                commit = null;
            }
        }

        return ShellResult.Ok(sb.ToString().TrimEnd());
    }

    private ShellResult CmdDiff(string[] args, IShellContext context)
    {
        if (!IsGitRepo(context))
            return NotARepoError();

        var staged = args.Contains("--staged") || args.Contains("--cached");
        var specificFile = args.FirstOrDefault(a => !a.StartsWith("-"));

        var index = LoadIndex(context);
        var headCommit = GetHeadCommit(context);
        var headFiles = headCommit?.Files ?? new Dictionary<string, string>();

        var sb = new StringBuilder();

        if (staged)
        {
            // Compare index to HEAD
            foreach (var entry in index.Entries)
            {
                if (specificFile != null && !entry.Key.EndsWith(specificFile)) continue;

                if (!headFiles.TryGetValue(entry.Key, out var headHash))
                {
                    sb.AppendLine($"diff --git a{entry.Key} b{entry.Key}");
                    sb.AppendLine("new file");
                    sb.AppendLine($"+++ b{entry.Key}");
                    var content = GetObjectContent(context, entry.Value.Hash);
                    foreach (var line in content.Split('\n'))
                        sb.AppendLine($"+{line}");
                    sb.AppendLine();
                }
                else if (headHash != entry.Value.Hash)
                {
                    AppendFileDiff(context, sb, entry.Key, headHash, entry.Value.Hash);
                }
            }
        }
        else
        {
            // Compare working tree to index
            foreach (var entry in index.Entries)
            {
                if (specificFile != null && !entry.Key.EndsWith(specificFile)) continue;

                var path = context.ResolvePath(entry.Key);
                if (!context.FileSystem.Exists(path))
                {
                    sb.AppendLine($"diff --git a{entry.Key} b{entry.Key}");
                    sb.AppendLine("deleted file");
                    continue;
                }

                var currentHash = HashFile(context, entry.Key);
                if (currentHash != entry.Value.Hash)
                {
                    AppendFileDiff(context, sb, entry.Key, entry.Value.Hash, currentHash, readNewFromFile: true);
                }
            }
        }

        if (sb.Length == 0)
        {
            return ShellResult.Ok();
        }

        return ShellResult.Ok(sb.ToString().TrimEnd());
    }

    private ShellResult CmdBranch(string[] args, IShellContext context)
    {
        if (!IsGitRepo(context))
            return NotARepoError();

        var delete = args.Contains("-d") || args.Contains("-D") || args.Contains("--delete");
        var branchName = args.FirstOrDefault(a => !a.StartsWith("-"));

        if (branchName == null)
        {
            // List branches
            var refsPath = context.ResolvePath(RefsHeadsDir);
            var currentBranch = GetCurrentBranch(context);
            var sb = new StringBuilder();

            if (context.FileSystem.Exists(refsPath))
            {
                var entries = context.FileSystem.ListDirectory(refsPath);
                var branches = entries
                    .Where(name => !context.FileSystem.IsDirectory(FileSystemPath.Combine(refsPath, name)))
                    .OrderBy(b => b);

                foreach (var branch in branches)
                {
                    if (branch == currentBranch)
                        sb.AppendLine($"* {branch}");
                    else
                        sb.AppendLine($"  {branch}");
                }
            }

            return ShellResult.Ok(sb.ToString().TrimEnd());
        }

        if (delete)
        {
            // Delete branch
            var currentBranch = GetCurrentBranch(context);
            if (branchName == currentBranch)
            {
                return ShellResult.Error($"error: Cannot delete branch '{branchName}' checked out at '{context.CurrentDirectory}'");
            }

            var refPath = context.ResolvePath($"{RefsHeadsDir}/{branchName}");
            if (!context.FileSystem.Exists(refPath))
            {
                return ShellResult.Error($"error: branch '{branchName}' not found.");
            }

            context.FileSystem.Delete(refPath);
            return ShellResult.Ok($"Deleted branch {branchName}.");
        }
        else
        {
            // Create branch
            var refPath = context.ResolvePath($"{RefsHeadsDir}/{branchName}");
            if (context.FileSystem.Exists(refPath))
            {
                return ShellResult.Error($"fatal: A branch named '{branchName}' already exists.");
            }

            var headCommit = GetHeadCommit(context);
            if (headCommit == null)
            {
                return ShellResult.Error("fatal: Not a valid object name: 'HEAD'.");
            }

            context.FileSystem.WriteFile(refPath, headCommit.Hash);
            return ShellResult.Ok();
        }
    }

    private ShellResult CmdCheckout(string[] args, IShellContext context)
    {
        if (!IsGitRepo(context))
            return NotARepoError();

        var createBranch = args.Contains("-b");
        var branchName = args.FirstOrDefault(a => !a.StartsWith("-"));

        if (string.IsNullOrEmpty(branchName))
        {
            return ShellResult.Error("error: please specify a branch to checkout");
        }

        if (createBranch)
        {
            // Create and checkout
            var currentCommit = GetHeadCommit(context);
            if (currentCommit == null)
            {
                return ShellResult.Error("fatal: Not a valid object name: 'HEAD'.");
            }

            var refPath = context.ResolvePath($"{RefsHeadsDir}/{branchName}");
            if (context.FileSystem.Exists(refPath))
            {
                return ShellResult.Error($"fatal: A branch named '{branchName}' already exists.");
            }

            context.FileSystem.WriteFile(refPath, currentCommit.Hash);
            context.FileSystem.WriteFile(context.ResolvePath(HeadFile), $"ref: refs/heads/{branchName}");
            return ShellResult.Ok($"Switched to a new branch '{branchName}'");
        }
        else
        {
            // Checkout existing branch
            var refPath = context.ResolvePath($"{RefsHeadsDir}/{branchName}");
            if (!context.FileSystem.Exists(refPath))
            {
                return ShellResult.Error($"error: pathspec '{branchName}' did not match any file(s) known to git");
            }

            var currentBranch = GetCurrentBranch(context);
            if (branchName == currentBranch)
            {
                return ShellResult.Ok($"Already on '{branchName}'");
            }

            context.FileSystem.WriteFile(context.ResolvePath(HeadFile), $"ref: refs/heads/{branchName}");

            // Update working tree to match branch
            var commit = GetHeadCommit(context);
            if (commit != null)
            {
                // Update index to match commit
                var index = new GitIndex { Entries = new Dictionary<string, GitIndexEntry>() };
                foreach (var file in commit.Files)
                {
                    index.Entries[file.Key] = new GitIndexEntry { Hash = file.Value };
                    
                    // Restore file content
                    var content = GetObjectContent(context, file.Value);
                    var filePath = context.ResolvePath(file.Key);
                    var dir = FileSystemPath.GetParent(filePath);
                    if (!string.IsNullOrEmpty(dir) && dir != "/" && !context.FileSystem.Exists(dir))
                    {
                        context.FileSystem.CreateDirectory(dir);
                    }
                    context.FileSystem.WriteFile(filePath, content);
                }
                SaveIndex(context, index);
            }

            return ShellResult.Ok($"Switched to branch '{branchName}'");
        }
    }

    private ShellResult CmdReset(string[] args, IShellContext context)
    {
        if (!IsGitRepo(context))
            return NotARepoError();

        var index = LoadIndex(context);
        var headCommit = GetHeadCommit(context);
        var headFiles = headCommit?.Files ?? new Dictionary<string, string>();

        if (args.Length == 0)
        {
            // Reset all staged files to HEAD
            index.Entries.Clear();
            foreach (var file in headFiles)
            {
                index.Entries[file.Key] = new GitIndexEntry { Hash = file.Value };
            }
            SaveIndex(context, index);
            return ShellResult.Ok("Unstaged changes after reset:");
        }

        // Reset specific files
        foreach (var pattern in args)
        {
            if (pattern.StartsWith("-")) continue;

            var path = context.ResolvePath(pattern);
            var relativePath = path;

            if (index.Entries.ContainsKey(relativePath))
            {
                if (headFiles.TryGetValue(relativePath, out var hash))
                {
                    index.Entries[relativePath] = new GitIndexEntry { Hash = hash };
                }
                else
                {
                    index.Entries.Remove(relativePath);
                }
            }
        }

        SaveIndex(context, index);
        return ShellResult.Ok();
    }

    #endregion

    #region Helpers

    private bool IsGitRepo(IShellContext context)
    {
        var gitPath = context.ResolvePath(GitDir);
        return context.FileSystem.Exists(gitPath);
    }

    private ShellResult NotARepoError()
    {
        return ShellResult.Error("fatal: not a git repository (or any of the parent directories): .git");
    }

    private string GetCurrentBranch(IShellContext context)
    {
        var headPath = context.ResolvePath(HeadFile);
        var headBytes = context.FileSystem.ReadFileBytes(headPath);
        var head = Encoding.UTF8.GetString(headBytes).Trim();
        
        if (head.StartsWith("ref: refs/heads/"))
        {
            return head["ref: refs/heads/".Length..];
        }
        return head[..7]; // Detached HEAD, return short hash
    }

    private GitCommit? GetHeadCommit(IShellContext context)
    {
        var branch = GetCurrentBranch(context);
        var refPath = context.ResolvePath($"{RefsHeadsDir}/{branch}");
        
        if (!context.FileSystem.Exists(refPath))
            return null;

        var hashBytes = context.FileSystem.ReadFileBytes(refPath);
        var hash = Encoding.UTF8.GetString(hashBytes).Trim();
        return LoadCommit(context, hash);
    }

    private GitCommit? LoadCommit(IShellContext context, string hash)
    {
        var commitPath = context.ResolvePath($"{ObjectsDir}/{hash}");
        if (!context.FileSystem.Exists(commitPath))
            return null;

        var commitBytes = context.FileSystem.ReadFileBytes(commitPath);
        var json = Encoding.UTF8.GetString(commitBytes);
        return JsonSerializer.Deserialize<GitCommit>(json, JsonOptions);
    }

    private GitIndex LoadIndex(IShellContext context)
    {
        var indexPath = context.ResolvePath(IndexFile);
        if (!context.FileSystem.Exists(indexPath))
            return new GitIndex { Entries = new Dictionary<string, GitIndexEntry>() };

        var indexBytes = context.FileSystem.ReadFileBytes(indexPath);
        var json = Encoding.UTF8.GetString(indexBytes);
        return JsonSerializer.Deserialize<GitIndex>(json, JsonOptions) 
            ?? new GitIndex { Entries = new Dictionary<string, GitIndexEntry>() };
    }

    private void SaveIndex(IShellContext context, GitIndex index)
    {
        var indexPath = context.ResolvePath(IndexFile);
        context.FileSystem.WriteFile(indexPath, JsonSerializer.Serialize(index, JsonOptions));
    }

    private GitConfig LoadConfig(IShellContext context)
    {
        var configPath = context.ResolvePath(ConfigFile);
        if (!context.FileSystem.Exists(configPath))
            return new GitConfig { User = new GitUser { Name = "Sandbox User", Email = "user@sandbox.local" } };

        var configBytes = context.FileSystem.ReadFileBytes(configPath);
        var json = Encoding.UTF8.GetString(configBytes);
        return JsonSerializer.Deserialize<GitConfig>(json, JsonOptions) 
            ?? new GitConfig { User = new GitUser { Name = "Sandbox User", Email = "user@sandbox.local" } };
    }

    private void AddFile(IShellContext context, string absolutePath, GitIndex index, List<string> addedFiles)
    {
        if (absolutePath.Contains("/.git/")) return;

        var contentBytes = context.FileSystem.ReadFileBytes(absolutePath);
        var content = Encoding.UTF8.GetString(contentBytes);
        var hash = ComputeHash(content);

        // Store blob
        var blobPath = context.ResolvePath($"{ObjectsDir}/{hash}");
        if (!context.FileSystem.Exists(blobPath))
        {
            context.FileSystem.WriteFile(blobPath, content);
        }

        index.Entries[absolutePath] = new GitIndexEntry { Hash = hash };
        addedFiles.Add(absolutePath);
    }

    private void AddAllFiles(IShellContext context, string directory, GitIndex index, List<string> addedFiles)
    {
        var files = GetAllFiles(context, directory);
        foreach (var file in files)
        {
            if (!file.Contains("/.git/"))
            {
                AddFile(context, file, index, addedFiles);
            }
        }
    }

    private List<string> GetAllFiles(IShellContext context, string directory)
    {
        var files = new List<string>();
        
        if (!context.FileSystem.Exists(directory))
            return files;

        var entries = context.FileSystem.ListDirectory(directory);
        foreach (var name in entries)
        {
            var fullPath = FileSystemPath.Combine(directory, name);
            if (context.FileSystem.IsDirectory(fullPath))
            {
                if (!name.StartsWith(".git"))
                {
                    files.AddRange(GetAllFiles(context, fullPath));
                }
            }
            else
            {
                files.Add(fullPath);
            }
        }

        return files;
    }

    private string HashFile(IShellContext context, string path)
    {
        var fullPath = context.ResolvePath(path);
        if (fullPath == path)
        {
            // Already absolute
        }
        var contentBytes = context.FileSystem.ReadFileBytes(fullPath);
        var content = Encoding.UTF8.GetString(contentBytes);
        return ComputeHash(content);
    }

    private string GetObjectContent(IShellContext context, string hash)
    {
        var path = context.ResolvePath($"{ObjectsDir}/{hash}");
        var bytes = context.FileSystem.ReadFileBytes(path);
        return Encoding.UTF8.GetString(bytes);
    }

    private void AppendFileDiff(IShellContext context, StringBuilder sb, string filePath, string oldHash, string newHash, bool readNewFromFile = false)
    {
        sb.AppendLine($"diff --git a{filePath} b{filePath}");
        sb.AppendLine($"index {oldHash[..7]}..{newHash[..7]}");
        sb.AppendLine($"--- a{filePath}");
        sb.AppendLine($"+++ b{filePath}");

        var oldContent = GetObjectContent(context, oldHash);
        var newContent = readNewFromFile 
            ? (context.FileSystem.ReadFileBytes(context.ResolvePath(filePath)) is byte[] b ? Encoding.UTF8.GetString(b) : string.Empty)
            : GetObjectContent(context, newHash);

        var oldLines = oldContent.Split('\n');
        var newLines = newContent.Split('\n');

        // Simple line-by-line diff
        sb.AppendLine($"@@ -1,{oldLines.Length} +1,{newLines.Length} @@");
        
        var maxLines = Math.Max(oldLines.Length, newLines.Length);
        for (int i = 0; i < maxLines; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i] : null;
            var newLine = i < newLines.Length ? newLines[i] : null;

            if (oldLine == newLine)
            {
                sb.AppendLine($" {oldLine}");
            }
            else
            {
                if (oldLine != null) sb.AppendLine($"-{oldLine}");
                if (newLine != null) sb.AppendLine($"+{newLine}");
            }
        }
        sb.AppendLine();
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    #endregion

    #region Data Models

    private class GitIndex
    {
        public Dictionary<string, GitIndexEntry> Entries { get; set; } = new();
    }

    private class GitIndexEntry
    {
        public string Hash { get; set; } = string.Empty;
    }

    private class GitCommit
    {
        public string Hash { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string? Parent { get; set; }
        public Dictionary<string, string> Files { get; set; } = new();
    }

    private class GitConfig
    {
        public GitUser User { get; set; } = new();
    }

    private class GitUser
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    #endregion
}
