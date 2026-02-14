using AgentSandbox.Api.Models;
using AgentSandbox.Core;
using AgentSandbox.Core.Validation;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace AgentSandbox.Api.Endpoints;

public static class SandboxEndpoints
{
    private static readonly ConcurrentDictionary<string, Sandbox> _activeSandboxes = new();

    public static void MapSandboxEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sandbox")
            .WithTags("Sandbox")
            .WithOpenApi();

        // Create a new sandbox
        group.MapPost("/", CreateSandbox)
            .WithName("CreateSandbox")
            .WithDescription("Creates a new sandbox instance");

        // List all sandboxes
        group.MapGet("/", ListSandboxes)
            .WithName("ListSandboxes")
            .WithDescription("Lists all active sandbox instances");

        // Get sandbox info
        group.MapGet("/{id}", GetSandbox)
            .WithName("GetSandbox")
            .WithDescription("Gets information about a specific sandbox");

        // Delete a sandbox
        group.MapDelete("/{id}", DeleteSandbox)
            .WithName("DeleteSandbox")
            .WithDescription("Destroys a sandbox and releases its resources");

        // Execute a command
        group.MapPost("/{id}/exec", ExecuteCommand)
            .WithName("ExecuteCommand")
            .WithDescription("Executes a shell command in the sandbox");

        // Get command history
        group.MapGet("/{id}/history", GetHistory)
            .WithName("GetCommandHistory")
            .WithDescription("Gets the command execution history");

        // Snapshot operations
        group.MapPost("/{id}/snapshot", CreateSnapshot)
            .WithName("CreateSnapshot")
            .WithDescription("Creates a snapshot of the sandbox state");

        group.MapPost("/{id}/restore", RestoreSnapshot)
            .WithName("RestoreSnapshot")
            .WithDescription("Restores sandbox state from a snapshot");

        // Stats
        group.MapGet("/{id}/stats", GetStats)
            .WithName("GetStats")
            .WithDescription("Gets runtime statistics for the sandbox");
    }

    private static IResult CreateSandbox(
        [FromBody] CreateSandboxRequest? request,
        [FromServices] SandboxManager manager)
    {
        try
        {
            var options = new SandboxOptions();
            
            if (request != null)
            {
                if (request.MaxTotalSize.HasValue)
                    options.MaxTotalSize = request.MaxTotalSize.Value;
                if (request.MaxFileSize.HasValue)
                    options.MaxFileSize = request.MaxFileSize.Value;
                if (request.MaxNodeCount.HasValue)
                    options.MaxNodeCount = request.MaxNodeCount.Value;
                if (!string.IsNullOrEmpty(request.WorkingDirectory))
                    options.WorkingDirectory = request.WorkingDirectory;
                if (request.Environment != null)
                    options.Environment = request.Environment;
            }

            var sandbox = manager.Get(options);
            _activeSandboxes[sandbox.Id] = sandbox;
            var stats = sandbox.GetStats();

            return Results.Created($"/api/sandbox/{sandbox.Id}", new SandboxResponse(
                stats.Id,
                stats.CurrentDirectory,
                stats.FileCount,
                stats.TotalSize,
                stats.CreatedAt
            ));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorResponse(ex.Message, 409));
        }
    }

    private static IResult ListSandboxes([FromServices] SandboxManager manager)
    {
        var sandboxes = GetTrackedSandboxes()
            .Select(s => s.GetStats())
            .Select(s => new SandboxResponse(
                s.Id,
                s.CurrentDirectory,
                s.FileCount,
                s.TotalSize,
                s.CreatedAt
            ));

        return Results.Ok(sandboxes);
    }

    private static IResult GetSandbox(string id, [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        var stats = sandbox.GetStats();
        return Results.Ok(new SandboxResponse(
            stats.Id,
            stats.CurrentDirectory,
            stats.FileCount,
            stats.TotalSize,
            stats.CreatedAt
        ));
    }

    private static IResult DeleteSandbox(string id, [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        _activeSandboxes.TryRemove(id, out _);
        sandbox.Dispose();
        return Results.NoContent();
    }

    private static IResult ExecuteCommand(
        string id,
        [FromBody] ExecuteCommandRequest request,
        [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        try
        {
            var result = sandbox.Execute(request.Command);

            return Results.Ok(new CommandResponse(
                result.Command,
                result.Stdout,
                result.Stderr,
                result.ExitCode,
                result.Success,
                result.Duration.TotalMilliseconds
            ));
        }
        catch (CoreValidationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, 400, ex.ErrorCode));
        }
    }

    private static IResult GetHistory(string id, [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        var history = sandbox.GetHistory()
            .Select(r => new CommandResponse(
                r.Command,
                r.Stdout,
                r.Stderr,
                r.ExitCode,
                r.Success,
                r.Duration.TotalMilliseconds
            ));

        return Results.Ok(history);
    }

    private static IResult ReadFile(
        string id,
        [FromQuery] string path,
        [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        try
        {
            var content = sandbox.ReadFile(path);

            return Results.Ok(new FileContentResponse(
                path,
                content,
                System.Text.Encoding.UTF8.GetByteCount(content)
            ));
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound(new ErrorResponse($"File '{path}' not found", 404));
        }
        catch (CoreValidationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, 400, ex.ErrorCode));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, 400));
        }
    }

    private static IResult WriteFile(
        string id,
        [FromBody] WriteFileRequest request,
        [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        try
        {
            sandbox.WriteFile(request.Path, request.Content);
            return Results.Ok(new { path = request.Path, success = true });
        }
        catch (CoreValidationException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, 400, ex.ErrorCode));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, 400));
        }
    }

    private static IResult ListDirectory(
        string id,
        [FromQuery] string? path,
        [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        try
        {
            var pwdResult = sandbox.Execute("pwd");
            var targetPath = path ?? pwdResult.Stdout.Trim();
            
            var result = sandbox.Execute($"ls -l \"{targetPath}\"");
            if (!result.Success)
            {
                return Results.NotFound(new ErrorResponse($"Directory '{path}' not found", 404));
            }

            // Parse ls output into entries
            var entries = result.Stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        var isDir = parts[0].StartsWith("d");
                        var size = long.TryParse(parts[1], out var s) ? s : 0;
                        var name = parts[^1];
                        return new DirectoryEntry(name, isDir, size, DateTime.UtcNow);
                    }
                    return new DirectoryEntry(line, false, 0, DateTime.UtcNow);
                });

            return Results.Ok(new DirectoryListingResponse(targetPath, entries));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message, 400));
        }
    }

    // In-memory snapshot storage (for demo; use persistent storage in production)
    private static readonly Dictionary<string, SandboxSnapshot> _snapshots = new();

    private static IResult CreateSnapshot(string id, [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        var snapshot = sandbox.CreateSnapshot();
        var snapshotId = Guid.NewGuid().ToString("N")[..12];
        _snapshots[snapshotId] = snapshot;

        return Results.Ok(new SnapshotResponse(
            snapshotId,
            sandbox.Id,
            snapshot.CreatedAt,
            snapshot.FileSystemData.Length
        ));
    }

    private static IResult RestoreSnapshot(
        string id,
        [FromQuery] string snapshotId,
        [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        if (!_snapshots.TryGetValue(snapshotId, out var snapshot))
            return Results.NotFound(new ErrorResponse($"Snapshot '{snapshotId}' not found", 404));

        sandbox.RestoreSnapshot(snapshot);
        return Results.Ok(new { restored = true, snapshotId });
    }

    private static IResult GetStats(string id, [FromServices] SandboxManager manager)
    {
        var sandbox = FindSandbox(id);
        if (sandbox == null)
            return Results.NotFound(new ErrorResponse($"Sandbox '{id}' not found", 404));

        var stats = sandbox.GetStats();
        return Results.Ok(new StatsResponse(
            stats.Id,
            stats.FileCount,
            stats.TotalSize,
            stats.CommandCount,
            stats.CurrentDirectory,
            stats.CreatedAt,
            stats.LastActivityAt
        ));
    }

    private static Sandbox? FindSandbox(string id)
    {
        if (!_activeSandboxes.TryGetValue(id, out var sandbox))
        {
            return null;
        }

        if (IsDisposed(sandbox))
        {
            _activeSandboxes.TryRemove(id, out _);
            return null;
        }

        return sandbox;
    }

    private static IEnumerable<Sandbox> GetTrackedSandboxes()
    {
        foreach (var (id, sandbox) in _activeSandboxes.ToArray())
        {
            if (IsDisposed(sandbox))
            {
                _activeSandboxes.TryRemove(id, out _);
                continue;
            }

            yield return sandbox;
        }
    }

    private static bool IsDisposed(Sandbox sandbox)
    {
        try
        {
            _ = sandbox.GetStats();
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
