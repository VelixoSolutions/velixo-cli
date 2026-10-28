namespace Velixo.Cli.Screen;

/// <summary>
/// The codes <c>velixo screen</c> raises. The <c>SCREEN_</c> ones are the server's own codes, raised
/// locally when the CLI can tell without a round trip that the server would refuse the call.
/// </summary>
internal static class ScreenErrorCodes
{
    public const string ConfirmationDeclined = "CONFIRMATION_DECLINED";
    public const string NoContinuation = "NO_CONTINUATION";
    public const string ErpActionRefused = "ERP_ACTION_REFUSED";
    public const string VariableMissing = "SCREEN_VARIABLE_MISSING";
    public const string VariableUnknown = "SCREEN_VARIABLE_UNKNOWN";
    public const string SessionLost = "SCREEN_SESSION_LOST";
    public const string ContinuationMismatch = "SCREEN_CONTINUATION_MISMATCH";
    public const string ContinuationUnverifiable = "SCREEN_CONTINUATION_UNVERIFIABLE";
    public const string RowsWithoutLine = "SCREEN_SETS_ROWS_WITHOUT_LINE";
    public const string LineWithoutRows = "SCREEN_SETS_LINE_WITHOUT_ROWS";
    public const string SplitRowsWithoutSplit = "SCREEN_SETS_SPLIT_ROWS_WITHOUT_SPLIT";
    public const string SetsPrefix = "SCREEN_SETS_";
}
