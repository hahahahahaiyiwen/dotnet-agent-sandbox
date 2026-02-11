using AgentSandbox.Core;
using Microsoft.Extensions.AI;

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
                    return string.IsNullOrEmpty(result.Stdout)
                        ? "(command completed successfully)"
                        : result.Stdout;
                }
                return $"Error: {result.Stderr}";
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
            (string path, int startLine = 0, int? endLine = null) =>
            {
                try
                {
                    // ReadFileLines uses 1-indexed line numbers, so convert from 0-based
                    int? adjustedStartLine = startLine > 0 ? startLine + 1 : null;
                    int? adjustedEndLine = endLine.HasValue ? endLine + 1 : null;
                    
                    var lines = sandbox.ReadFileLines(path, adjustedStartLine, adjustedEndLine);
                    return string.Join("\n", lines);
                }
                catch (Exception ex)
                {
                    return $"Error reading file: {ex.Message}";
                }
            },
            name: "read_file",
            description: "Read the contents of a file from the sandbox filesystem. Supports line-range reads for large files. " +
                "Parameters: path (file path), startLine (0-based, default 0), endLine (0-based, exclusive, default null=EOF). " +
                "Example: read_file('/logs.txt', 100, 120) returns lines 100-119.");
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
                try
                {
                    sandbox.WriteFile(path, content);
                    return $"File written successfully: {path}";
                }
                catch (Exception ex)
                {
                    return $"Error writing file: {ex.Message}";
                }
            },
            name: "write_file",
            description: "Write or create a file in the sandbox filesystem. Automatically creates parent directories. Parameters: path (file path), content (file contents as text).");
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
                try
                {
                    sandbox.ApplyPatch(path, patch);
                    return $"Patch applied successfully to: {path}";
                }
                catch (Exception ex)
                {
                    return $"Error applying patch: {ex.Message}";
                }
            },
            name: "edit_file",
            description: "Apply a unified diff patch to a file in the sandbox. Supports standard unified diff format with lines starting with '-' (remove), '+' (add), and ' ' (context). Parameters: path (file path), patch (unified diff content).");
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