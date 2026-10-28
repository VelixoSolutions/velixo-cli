namespace Velixo.Cli.Output;

/// <summary>
/// Every error code the CLI raises or reacts to. The server's own codes pass through verbatim, so only
/// the ones the CLI raises itself or treats specially are named here. <c>docs/commands.md</c> lists each
/// with its exit code.
/// </summary>
public static class ErrorCodes
{
    public const string Usage = "USAGE";
    public const string IntentRequired = "INTENT_REQUIRED";
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
    public const string ConfirmationDeclined = "CONFIRMATION_DECLINED";
    public const string DuplicateRun = "DUPLICATE_RUN";
    public const string NoContinuation = "NO_CONTINUATION";
    public const string NotImplemented = "NOT_IMPLEMENTED";
    public const string ConfigInvalid = "CONFIG_INVALID";
    public const string CredentialsInvalid = "CREDENTIALS_INVALID";
    public const string NotSignedIn = "NOT_SIGNED_IN";
    public const string SessionEnded = "SESSION_ENDED";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string SignInFailed = "SIGN_IN_FAILED";
    public const string BuilderRefusal = "BUILDER_REFUSAL";
    public const string ToolError = "TOOL_ERROR";
    public const string TaskFailed = "TASK_FAILED";
    public const string TaskExpired = "TASK_EXPIRED";
    public const string ErpActionRefused = "ERP_ACTION_REFUSED";
    public const string Timeout = "TIMEOUT";
    public const string Interrupted = "INTERRUPTED";
    public const string Network = "NETWORK_ERROR";
    public const string Http = "HTTP_ERROR";
    public const string Protocol = "MCP_PROTOCOL_ERROR";
    public const string Internal = "INTERNAL_ERROR";

    /// <summary>The upload endpoint refused a file's bytes. The CLI raises it; the server's upload endpoint answers only with an HTTP status.</summary>
    public const string AttachmentUploadFailed = "ATTACHMENT_UPLOAD_FAILED";

    public const string AttachmentTokenInvalid = "ATTACHMENT_TOKEN_INVALID";
    public const string AttachmentTokenAlreadyUsed = "ATTACHMENT_TOKEN_ALREADY_USED";
    public const string ScreenVariableMissing = "SCREEN_VARIABLE_MISSING";
    public const string ScreenVariableUnknown = "SCREEN_VARIABLE_UNKNOWN";
    public const string ScreenSessionLost = "SCREEN_SESSION_LOST";
    public const string ScreenContinuationMismatch = "SCREEN_CONTINUATION_MISMATCH";
    public const string ScreenContinuationUnverifiable = "SCREEN_CONTINUATION_UNVERIFIABLE";
    public const string ScreenSetsRowsWithoutLine = "SCREEN_SETS_ROWS_WITHOUT_LINE";
    public const string ScreenSetsLineWithoutRows = "SCREEN_SETS_LINE_WITHOUT_ROWS";
    public const string ScreenSetsSplitRowsWithoutSplit = "SCREEN_SETS_SPLIT_ROWS_WITHOUT_SPLIT";
    public const string BcRecordSessionUnsupported = "BC_RECORD_SESSION_UNSUPPORTED";

    /// <summary>The prefix every <c>SCREEN_SETS_*</c> code shares: the call's command sets do not fit together.</summary>
    public const string ScreenSetsPrefix = "SCREEN_SETS_";
}
