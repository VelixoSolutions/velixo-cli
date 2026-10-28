using Velixo.Cli.Output;

namespace Velixo.Cli.Mcp;

/// <summary>
/// The one-line recovery the CLI prints as <c>nextStep</c> for each server error code, summarising
/// what the server's tool guidance prescribes for it. A code means the same recovery whether the server
/// raised it or the CLI raised it locally before a call.
/// </summary>
public static class ErrorGuidance
{
    public const string SessionLostNothingRanStep =
        "Start the record again from its header. If the header was saved, load it by key in a new header.";

    public const string SessionLostUnknownStep = "Read the record before doing anything else.";

    public const string ContinuationMismatchStep = "Do not retry. Read what the session holds, or start again.";

    public const string ContinuationUnverifiableStep = "Start again from the header.";

    /// <summary>The sentence the server puts in a <c>SCREEN_SESSION_LOST</c> message when the call did not run at all.</summary>
    private const string NothingRanPhrase = "Nothing in this call was executed";

    private static readonly Dictionary<string, string> _byCode = new(StringComparer.Ordinal)
    {
        [ErrorCodes.ScreenContinuationMismatch] = ContinuationMismatchStep,
        [ErrorCodes.ScreenContinuationUnverifiable] = ContinuationUnverifiableStep,
        [ErrorCodes.ScreenVariableMissing] = "Bind every placeholder the batch names with --var, or pass --var-null to let the ERP default it.",
        [ErrorCodes.ScreenVariableUnknown] = "Remove the --var the batch does not declare; 'variables' in the batch file lists the names.",
        [ErrorCodes.AttachmentTokenInvalid] = "The link or upload token expired or was spent. Upload the file again, or run the command that issued the link again.",
        [ErrorCodes.AttachmentTokenAlreadyUsed] =
            "This upload was already spent and nothing was sent again. Read the ERP before anything else. "
            + "To send the file again, upload it again: run the command again, which stages the file anew.",
        [ErrorCodes.BcRecordSessionUnsupported] = "Business Central keeps no record open between calls. Send the whole batch in one call, without --leave-open or --continue.",
    };

    /// <summary>The recovery per code family, the more specific prefix first.</summary>
    private static readonly (string Prefix, string NextStep)[] _byFamily =
    [
        (ErrorCodes.ScreenSetsPrefix, "The message names the set to remove or add."),
        ("ATTACHMENT_", "Read the message; 'velixo guide execute_commands' explains each attachment code."),
        ("SCREEN_", "Read the message; 'velixo guide execute_commands' says whether the ERP was touched for this code."),
        ("IMPORT_", "Read the message; 'velixo guide execute_import_scenario' says whether any row was imported for this code."),
        ("REPORT_", "Read the message; 'velixo guide execute_commands' explains each report code."),
        ("BC_", "Read the message; 'velixo guide execute_commands' explains each Business Central code."),
    ];

    /// <summary>
    /// Returns the recovery for <paramref name="code"/>, or null for a code outside the server's families.
    /// <c>SCREEN_SESSION_LOST</c> has two, and <paramref name="message"/> says which: the server states in it
    /// when nothing in the call ran.
    /// </summary>
    public static string? For(string code, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);

        if (code == ErrorCodes.ScreenSessionLost)
        {
            return message.Contains(NothingRanPhrase, StringComparison.OrdinalIgnoreCase) ? SessionLostNothingRanStep : SessionLostUnknownStep;
        }

        if (_byCode.TryGetValue(code, out var nextStep))
        {
            return nextStep;
        }

        return _byFamily.FirstOrDefault(family => code.StartsWith(family.Prefix, StringComparison.Ordinal)).NextStep;
    }
}
