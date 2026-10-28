namespace Velixo.Cli.Mcp;

/// <summary>
/// The one-line recovery the CLI prints as <c>nextStep</c> for each server error code, summarising
/// what the server's tool guidance prescribes for it.
/// </summary>
public static class ErrorGuidance
{
    private static readonly Dictionary<string, string> _byCode = new(StringComparer.Ordinal)
    {
        ["SCREEN_SESSION_LOST"] = "Read the record before doing anything else. If nothing ran, start it again from its header; if the header was saved, load it by key in a new header.",
        ["SCREEN_CONTINUATION_MISMATCH"] = "Do not retry. Read what the session holds, or start the record again.",
        ["SCREEN_CONTINUATION_UNVERIFIABLE"] = "Start the record again from its header.",
        ["SCREEN_VARIABLE_MISSING"] = "Bind every placeholder the batch names with --var, or pass --var-null to let the ERP default it.",
        ["SCREEN_VARIABLE_UNKNOWN"] = "Remove the --var the batch does not declare; 'variables' in the batch file lists the names.",
        ["ATTACHMENT_TOKEN_INVALID"] = "The link or upload token expired or was spent. Upload the file again, or run the command that issued the link again.",
        ["BC_RECORD_SESSION_UNSUPPORTED"] = "Business Central keeps no record open between calls. Send the whole batch in one call, without --leave-open or --continue.",
    };

    private static readonly Dictionary<string, string> _byFamily = new(StringComparer.Ordinal)
    {
        ["SCREEN_SETS_"] = "The message names the command set to remove or add.",
        ["ATTACHMENT_"] = "Read the message; 'velixo guide execute_commands' explains each attachment code.",
        ["SCREEN_"] = "Read the message; 'velixo guide execute_commands' says whether the ERP was touched for this code.",
        ["IMPORT_"] = "Read the message; 'velixo guide execute_import_scenario' says whether any row was imported for this code.",
        ["REPORT_"] = "Read the message; 'velixo guide execute_commands' explains each report code.",
        ["BC_"] = "Read the message; 'velixo guide execute_commands' explains each Business Central code.",
    };

    /// <summary>The server's error code families: a text that starts with one of these is a coded refusal.</summary>
    public static readonly IReadOnlyList<string> Families = ["ATTACHMENT_", "SCREEN_", "IMPORT_", "REPORT_", "BC_"];

    public static string? For(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (_byCode.TryGetValue(code, out var nextStep))
        {
            return nextStep;
        }

        return _byFamily.FirstOrDefault(family => code.StartsWith(family.Key, StringComparison.Ordinal)).Value;
    }
}
