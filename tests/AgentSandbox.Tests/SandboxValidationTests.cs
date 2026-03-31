using AgentSandbox.Core;
using AgentSandbox.Core.Validation;

namespace AgentSandbox.Tests;

public class SandboxValidationTests
{
    [Fact]
    public void Execute_ThrowsArgumentNullException_WhenCommandIsNull()
    {
        using var sandbox = new Sandbox();

        Assert.Throws<ArgumentNullException>(() => sandbox.Execute(null!));
    }

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
    public void Execute_ThrowsDeterministicErrorCode_WhenCommandTooLong_WithMultiByteUtf8()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            MaxCommandLength = 8
        });

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.Execute("echo 😀"));

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
    public void WriteFile_ThrowsDeterministicErrorCode_WhenPayloadTooLarge_WithMultiByteUtf8()
    {
        using var sandbox = new Sandbox(options: new SandboxOptions
        {
            MaxWritePayloadBytes = 4
        });

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.WriteFile("/a.txt", "😀😀"));

        Assert.Equal(CoreValidationErrorCodes.WritePayloadTooLarge, ex.ErrorCode);
    }

    [Fact]
    public void WriteFile_ThrowsArgumentNullException_WhenContentIsNull()
    {
        using var sandbox = new Sandbox();

        Assert.Throws<ArgumentNullException>(() => sandbox.WriteFile("/a.txt", null!));
    }

    [Fact]
    public void ReadFile_ThrowsDeterministicErrorCode_WhenPathHasTraversal()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.ReadFileLines("../secret.txt").ToList());

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void ReadFile_ThrowsArgumentNullException_WhenPathIsNull()
    {
        using var sandbox = new Sandbox();

        Assert.Throws<ArgumentNullException>(() => sandbox.ReadFileLines(null!).ToList());
    }

    [Fact]
    public void ReadFile_ThrowsDeterministicErrorCode_WhenPathHasMultipleParentTraversals()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.ReadFileLines("../../secret.txt").ToList());

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void ReadFile_ThrowsDeterministicErrorCode_WhenPathHasParentTraversalInMiddle()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.ReadFileLines("/a/b/../../../secret.txt").ToList());

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void ReadFile_AllowsPathWhenDotDotIsPartOfFilename()
    {
        using var sandbox = new Sandbox();
        sandbox.WriteFile("/file..txt", "ok");

        var content = string.Join("\n", sandbox.ReadFileLines("/file..txt"));

        Assert.Equal("ok", content);
    }

    [Fact]
    public void WriteFile_ThrowsDeterministicErrorCode_WhenPathHasTraversal()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.WriteFile("/safe/../secret.txt", "value"));

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void WriteFile_ThrowsArgumentNullException_WhenPathIsNull()
    {
        using var sandbox = new Sandbox();

        Assert.Throws<ArgumentNullException>(() => sandbox.WriteFile(null!, "value"));
    }

    [Fact]
    public void WriteFile_ThrowsDeterministicErrorCode_WhenPathHasMultipleParentTraversals()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.WriteFile("../../secret.txt", "value"));

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void WriteFile_ThrowsDeterministicErrorCode_WhenPathHasParentTraversalInMiddle()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.WriteFile("/a/b/../../../secret.txt", "value"));

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void WriteFile_AllowsPathWhenDotDotIsPartOfFilename()
    {
        using var sandbox = new Sandbox();

        var exception = Record.Exception(() => sandbox.WriteFile("/file..txt", "value"));

        Assert.Null(exception);
    }

    [Fact]
    public void ReadFileLines_ThrowsDeterministicErrorCode_WhenPathHasTraversal()
    {
        using var sandbox = new Sandbox();

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.ReadFileLines("../secret.txt").ToList());

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void ApplyPatch_ThrowsDeterministicErrorCode_WhenPathHasTraversal()
    {
        using var sandbox = new Sandbox();

        var patch = """
        @@ -0,0 +1,1 @@
        +test
        """;

        var ex = Assert.Throws<CoreValidationException>(() => sandbox.ApplyPatch("/safe/../secret.txt", patch));

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void Ctor_ThrowsDeterministicErrorCode_WhenWorkingDirectoryHasTraversal()
    {
        var ex = Assert.Throws<CoreValidationException>(() => new Sandbox(options: new SandboxOptions
        {
            WorkingDirectory = "/safe/../secret"
        }));

        Assert.Equal(CoreValidationErrorCodes.PathTraversalDetected, ex.ErrorCode);
    }

    [Fact]
    public void SandboxOptions_ThrowsArgumentOutOfRangeException_WhenMaxCommandLengthNotPositive()
    {
        var options = new SandboxOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxCommandLength = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxCommandLength = -1);
    }

    [Fact]
    public void SandboxOptions_ThrowsArgumentOutOfRangeException_WhenMaxWritePayloadBytesNotPositive()
    {
        var options = new SandboxOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxWritePayloadBytes = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxWritePayloadBytes = -1);
    }
}
