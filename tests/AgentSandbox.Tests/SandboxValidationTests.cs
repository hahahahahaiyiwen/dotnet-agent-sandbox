using AgentSandbox.Core;
using AgentSandbox.Core.Validation;

namespace AgentSandbox.Tests;

public class SandboxValidationTests
{
    [Fact]
    public void Execute_ThrowsDeterministicErrorCode_WhenCommandTooLong()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            MaxCommandLength = 8
        });

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.Execute("echo 123456789"));

        Assert.Equal(CoreValidationErrorCodes.CommandTooLong, ex.ErrorCode);
    }

    [Fact]
    public void WriteFile_ThrowsDeterministicErrorCode_WhenPayloadTooLarge()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            MaxWritePayloadBytes = 4
        });

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.WriteFile("/a.txt", "hello"));

        Assert.Equal(CoreValidationErrorCodes.WritePayloadTooLarge, ex.ErrorCode);
    }

    [Fact]
    public void ReadFile_ThrowsDeterministicErrorCode_WhenPathHasTraversal()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.ReadFile("../secret.txt"));

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void WriteFile_ThrowsDeterministicErrorCode_WhenPathHasTraversal()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.WriteFile("/safe/../secret.txt", "value"));

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }
}
