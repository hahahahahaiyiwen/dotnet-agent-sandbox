namespace AgentSandbox.Capabilities.SQL;

public static class SqlCapabilityErrorCodes
{
    public const string AuthDenied = "AUTH_DENIED";
    public const string Timeout = "TIMEOUT";
    public const string ResourceLimit = "RESOURCE_LIMIT";
    public const string SyntaxError = "SYNTAX_ERROR";
    public const string BackendUnavailable = "BACKEND_UNAVAILABLE";
}

public sealed class SqlCapabilityException : InvalidOperationException
{
    public string ErrorCode { get; }

    public SqlCapabilityException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}


