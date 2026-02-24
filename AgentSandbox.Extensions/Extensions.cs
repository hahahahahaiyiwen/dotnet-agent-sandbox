using AgentSandbox.Core;
using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

namespace AgentSandbox.Extensions;

public static class Extensions
{
    /// <summary>
    /// Retrieves all sandbox-related functions (bash, file operations, skills) as AIFunction
    /// </summary>
    public static IEnumerable<AIFunction> GetSandboxFunctions(this Sandbox sandbox)
    {
        return new List<AIFunction>
        {
            sandbox.GetBashFunction(),
            sandbox.GetReadFileFunction(),
            sandbox.GetWriteFileFunction(),
            sandbox.GetApplyPatchFunction(),
            sandbox.GetSkillFunction()
        };
    }

    /// <summary>
    /// Creates an AIFunction for sandbox bash command execution with dynamic description.
    /// </summary>
    /// <param name="sandbox">The sandbox instance.</param>
    /// <returns>An AIFunction that executes commands in the sandbox.</returns>
    public static AIFunction GetBashFunction(this Sandbox sandbox)
    {
        return AIFunctionFactory.Create(
            (string command) =>
            {
                var result = sandbox.Execute(command);
                if (result.Success)
                {
                    return new SandboxToolResponse(
                        Success: true,
                        Message: "Command completed successfully.",
                        Output: string.IsNullOrEmpty(result.Stdout) ? null : result.Stdout);
                }
                return new SandboxToolResponse(
                    Success: false,
                    Message: result.Stderr);
            },
            name: "bash_shell",
            description: sandbox.GetBashToolDescription());
    }

    /// <summary>
    /// Creates an AIFunction for reading file contents.
    /// Supports full file reads or line-range reads for large file handling.
    /// </summary>
    /// <param name="sandbox">The sandbox instance.</param>
    /// <returns>An AIFunction that reads files from the sandbox with optional line-range support.</returns>
    public static AIFunction GetReadFileFunction(this Sandbox sandbox)
    {
        return AIFunctionFactory.Create(
            (string path, int? startLine = null, int? endLine = null) =>
            {
                var lines = sandbox.ReadFileLines(path, startLine, endLine).ToList();
                return new SandboxToolResponse(
                    Success: true,
                    Message: $"Read {lines.Count} line(s) from '{path}'.",
                    Output: string.Join("\n", lines));
            },
            name: "read_file",
            description: "Read the contents of a file from the sandbox filesystem. Supports line-range reads for large files. " +
                "Parameters: path (file path), startLine (1-based, optional, inclusive), endLine (1-based, optional, exclusive). " +
                "Example: read_file('/logs.txt', 100, 120) returns lines 100-119. " +
                "Validation and other failures (such as file not found or invalid line ranges) are surfaced as tool errors rather than being returned as error messages.");
    }

    /// <summary>
    /// Creates an AIFunction for writing files.
    /// </summary>
    /// <param name="sandbox">The sandbox instance.</param>
    /// <returns>An AIFunction that writes files to the sandbox.</returns>
    public static AIFunction GetWriteFileFunction(this Sandbox sandbox)
    {
        return AIFunctionFactory.Create(
            (string path, string content) =>
            {
                sandbox.WriteFile(path, content);
                return new SandboxToolResponse(
                    Success: true,
                    Message: $"File written successfully: {path}");
            },
            name: "write_file",
            description: "Write or create a file in the sandbox filesystem. Automatically creates parent directories. " +
                "Parameters: path (file path), content (file contents as text). Validation failures are surfaced as tool errors.");
    }

    /// <summary>
    /// Creates an AIFunction for applying diff patches.
    /// </summary>
    /// <param name="sandbox">The sandbox instance.</param>
    /// <returns>An AIFunction that applies patches to files in the sandbox.</returns>
    public static AIFunction GetApplyPatchFunction(this Sandbox sandbox)
    {
        return AIFunctionFactory.Create(
            (string path, string patch) =>
            {
                sandbox.ApplyPatch(path, patch);
                return new SandboxToolResponse(
                    Success: true,
                    Message: $"Patch applied successfully to: {path}");
            },
            name: "edit_file",
            description: "Apply a unified diff patch to a file in the sandbox. Supports standard unified diff format with lines starting with '-' (remove), '+' (add), and ' ' (context). " +
                "Parameters: path (file path), patch (unified diff content). Validation failures are surfaced as tool errors.");
    }

    /// <summary>
    /// Creates an AIFunction for retrieving skill information.
    /// The function description dynamically includes all available skills.
    /// </summary>
    /// <param name="sandbox">The sandbox instance with loaded skills.</param>
    /// <returns>An AIFunction that retrieves skill information.</returns>
    public static AIFunction GetSkillFunction(this Sandbox sandbox)
    {
        return AIFunctionFactory.Create(
            (string skillName) => sandbox.GetSkillImplementation(skillName),
            name: "get_skill",
            description: sandbox.GetSkillsDescription());
    }

    private static string GetSkillImplementation(this Sandbox sandbox, string skillName)
    {
        var skill = sandbox.GetSkills()
            .FirstOrDefault(s => s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));

        if (skill?.Metadata?.Instructions == null)
        {
            return $"Skill '{skillName}' not found";
        }

        // Read SKILL.md for instructions (already parsed in metadata)
        return skill.Metadata.Instructions;
    }
}

public sealed record SandboxToolResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("output")] string? Output = null);
