namespace AgentSandbox.Core.Validation;

/// <summary>
/// Validation failure with deterministic error code.
/// </summary>
public sealed class CoreValidationException : ArgumentException
{
    public string ErrorCode { get; }

    public CoreValidationException(string errorCode, string message, string? paramName = null)
        : base(message, paramName)
    {
        ErrorCode = errorCode;
    }

    public static CoreValidationException CommandTooLong(int actualBytes, int maxBytes)
        => new(
            CoreValidationErrorCodes.CommandTooLong,
            $"Command input exceeds max bytes ({actualBytes} > {maxBytes}).",
            "command");

    public static CoreValidationException WritePayloadTooLarge(int actualBytes, int maxBytes)
        => new(
            CoreValidationErrorCodes.WritePayloadTooLarge,
            $"Write payload exceeds max bytes ({actualBytes} > {maxBytes}).",
            "content");

    public static CoreValidationException PathTraversalDetected(string? paramName = "path")
        => new(
            CoreValidationErrorCodes.PathTraversalDetected,
            "Path traversal segment '..' is not allowed in API path input.",
            paramName);
}

public static class CoreValidationErrorCodes
{
    public const string CommandTooLong = "SBX_VAL_001_COMMAND_TOO_LONG";
    public const string WritePayloadTooLarge = "SBX_VAL_002_WRITE_PAYLOAD_TOO_LARGE";
    public const string PathTraversalDetected = "SBX_VAL_003_PATH_TRAVERSAL_DETECTED";
}
