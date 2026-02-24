using AgentSandbox.Core;
using AgentSandbox.Extensions;
using Microsoft.Extensions.AI;

namespace AgentSandbox.Tests;

public class ExtensionsFunctionTests
{
    [Fact]
    public async Task GetBashFunction_ReturnsStructuredSuccessResult()
    {
        using var sandbox = new Sandbox();
        var function = sandbox.GetBashFunction();

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["command"] = "echo hello" }),
            default);

        var response = DeserializeResponse(result);
        Assert.True(response.Success);
        Assert.Equal("Command completed successfully.", response.Message);
        Assert.Equal("hello", response.Output?.Trim());
    }

    [Fact]
    public async Task GetBashFunction_ReturnsStructuredFailureResult()
    {
        using var sandbox = new Sandbox();
        var function = sandbox.GetBashFunction();

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["command"] = "command-that-does-not-exist" }),
            default);

        var response = DeserializeResponse(result);
        Assert.False(response.Success);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    [Fact]
    public async Task GetReadFileFunction_UsesOneBasedExclusiveEndLineNumbers()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteFile("/logs.txt", "line1\nline2\nline3");

        var function = sandbox.GetReadFileFunction();
        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "/logs.txt", ["startLine"] = 2, ["endLine"] = 3 }),
            default);

        var response = DeserializeResponse(result);
        Assert.True(response.Success);
        Assert.Equal("line2", response.Output);
    }

    [Fact]
    public async Task GetReadFileFunction_SurfacesValidationErrors()
    {
        using var sandbox = new Sandbox();
        var function = sandbox.GetReadFileFunction();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await function.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "/missing.txt" }),
                default));
    }

    [Fact]
    public async Task GetWriteFileFunction_ReturnsStructuredSuccessResult()
    {
        using var sandbox = new Sandbox();
        var function = sandbox.GetWriteFileFunction();

        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "/created.txt", ["content"] = "abc" }),
            default);

        var response = DeserializeResponse(result);
        Assert.True(response.Success);
        Assert.Equal("File written successfully: /created.txt", response.Message);
        Assert.Equal("abc", string.Join("\n", sandbox.ReadFileLines("/created.txt")));
    }

    [Fact]
    public async Task GetWriteFileFunction_SurfacesValidationErrors()
    {
        using var sandbox = new Sandbox();
        var function = sandbox.GetWriteFileFunction();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await function.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "/safe/../blocked.txt", ["content"] = "abc" }),
                default));
    }

    [Fact]
    public async Task GetApplyPatchFunction_ReturnsStructuredSuccessResult()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteFile("/file.txt", "Line 1\r\nLine 3");
        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1,2 +1,3 @@
 Line 1
+Line 2
 Line 3
";

        var function = sandbox.GetApplyPatchFunction();
        var result = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "/file.txt", ["patch"] = patch }),
            default);

        var response = DeserializeResponse(result);
        Assert.True(response.Success);
        Assert.Equal("Patch applied successfully to: /file.txt", response.Message);
        Assert.Equal("Line 1\nLine 2\nLine 3", string.Join("\n", sandbox.ReadFileLines("/file.txt")));
    }

    [Fact]
    public async Task GetApplyPatchFunction_SurfacesValidationErrors()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteFile("/file.txt", "Line 1");
        var patch = @"--- a/file.txt
+++ b/file.txt
@@ -1,1 +1,1 @@
-Line 1
+Line 1 updated
";

        var function = sandbox.GetApplyPatchFunction();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await function.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?> { ["path"] = "/safe/../file.txt", ["patch"] = patch }),
                default));
    }

    private static SandboxToolResponse DeserializeResponse(object? result)
    {
        var json = Assert.IsType<System.Text.Json.JsonElement>(result);

        var success = TryGetProperty(json, "success", out var successProp)
            ? successProp.GetBoolean()
            : json.GetProperty("Success").GetBoolean();
        var message = TryGetProperty(json, "message", out var messageProp)
            ? messageProp.GetString() ?? string.Empty
            : json.GetProperty("Message").GetString() ?? string.Empty;
        string? output = null;
        if (TryGetProperty(json, "output", out var outputProp))
        {
            output = outputProp.ValueKind == System.Text.Json.JsonValueKind.Null ? null : outputProp.GetString();
        }
        else if (TryGetProperty(json, "Output", out var outputPascal))
        {
            output = outputPascal.ValueKind == System.Text.Json.JsonValueKind.Null ? null : outputPascal.GetString();
        }

        return new SandboxToolResponse(success, message, output);
    }

    private static bool TryGetProperty(System.Text.Json.JsonElement element, string propertyName, out System.Text.Json.JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
