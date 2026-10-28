namespace Velixo.Cli.Output;

/// <summary>The error codes the CLI raises itself, beside the codes the server returns verbatim.</summary>
public static class ErrorCodes
{
    public const string Usage = "USAGE";
    public const string IntentRequired = "INTENT_REQUIRED";
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
    public const string DuplicateRun = "DUPLICATE_RUN";
    public const string NotImplemented = "NOT_IMPLEMENTED";
    public const string NotSignedIn = "NOT_SIGNED_IN";
    public const string SessionEnded = "SESSION_ENDED";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string SignInFailed = "SIGN_IN_FAILED";
    public const string BuilderRefusal = "BUILDER_REFUSAL";
    public const string ToolError = "TOOL_ERROR";
    public const string TaskFailed = "TASK_FAILED";
    public const string TaskExpired = "TASK_EXPIRED";
    public const string Timeout = "TIMEOUT";
    public const string Interrupted = "INTERRUPTED";
    public const string Network = "NETWORK_ERROR";
    public const string Http = "HTTP_ERROR";
    public const string Protocol = "MCP_PROTOCOL_ERROR";
    public const string Internal = "INTERNAL_ERROR";
}
