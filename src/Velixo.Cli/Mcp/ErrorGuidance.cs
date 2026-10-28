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
        [ErrorCodes.ScreenVariableMissing] = "Bind every name the message lists with --param, or per row in --rows. Use --param-null only when the user asked to leave the field to the ERP's default.",
        [ErrorCodes.ScreenVariableUnknown] = "Remove the --param the batch does not declare; 'variables' in the build's output lists the names.",
        [ErrorCodes.AttachmentTokenInvalid] = "The link or the staged file expired or was spent. For a link, run the command that issued it again. For a file: with --attach or --file, run the command again, which stages a fresh copy; with --attach-token or --file-token, stage the file again with 'velixo files stage' and pass the new token.",
        [ErrorCodes.AttachmentTokenAlreadyUsed] =
            "This file was already filed and nothing was sent again. Read the ERP before anything else, and do not run the command again: "
            + "it would run the batch or the import a second time.",
        [ErrorCodes.BcRecordSessionUnsupported] = "Business Central keeps no record open between calls. Send the whole batch in one call, without --leave-open or --continue.",
    };

    /// <summary>The recovery per code family, the more specific prefix first.</summary>
    private static readonly (string Prefix, string NextStep)[] _byFamily =
    [
        (ErrorCodes.ScreenSetsPrefix, "The message names the set to remove or add."),
        ("ATTACHMENT_", "Read the message; 'velixo guide command run' explains each attachment code."),
        ("SCREEN_", "Read the message; 'velixo guide command run' says whether the ERP was touched for this code."),
        ("IMPORT_", "Read the message: it names what to change in the file. 'velixo guide import' covers the import errors."),
        ("REPORT_", "Read the message; 'velixo guide command run' explains each report code."),
        ("BC_", "Read the message: it names what Business Central refused. 'velixo guide command run' covers the other ERPs."),
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
