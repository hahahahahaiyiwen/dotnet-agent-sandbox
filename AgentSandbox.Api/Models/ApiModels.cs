namespace AgentSandbox.Api.Models;

// Request DTOs
public record CreateSandboxRequest(
    string? Id = null,
    long? MaxTotalSize = null,
    long? MaxFileSize = null,
    int? MaxNodeCount = null,
    string? WorkingDirectory = null,
    Dictionary<string, string>? Environment = null
);

public record ExecuteCommandRequest(string Command);

public record WriteFileRequest(string Path, string Content);

public record ReadFileRequest(string Path);

// Response DTOs
public record SandboxResponse(
    string Id,
    string CurrentDirectory,
    int FileCount,
    long TotalSize,
    DateTime CreatedAt
);

public record CommandResponse(
    string Command,
    string Stdout,
    string Stderr,
    int ExitCode,
    bool Success,
    double DurationMs
);

public record FileContentResponse(
    string Path,
    string Content,
    long Size
);

public record DirectoryListingResponse(
    string Path,
    IEnumerable<DirectoryEntry> Entries
);

public record DirectoryEntry(
    string Name,
    bool IsDirectory,
    long Size,
    DateTime ModifiedAt
);

public record SnapshotResponse(
    string Id,
    string SandboxId,
    DateTime CreatedAt,
    long Size
);

public record StatsResponse(
    string Id,
    int FileCount,
    long TotalSize,
    int CommandCount,
    int CapabilityOperationCount,
    string CurrentDirectory,
    DateTime CreatedAt,
    DateTime LastActivityAt
);

public record ErrorResponse(string Error, int StatusCode, string? ErrorCode = null);
