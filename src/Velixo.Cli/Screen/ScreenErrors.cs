using System.Text.RegularExpressions;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// Gives a failed <c>execute_commands</c> call the exit code and <c>nextStep</c> of the spec's
/// continuation table: a coded failure is exit 5 whether the server refused the call or the task
/// failed with the code, and an action the ERP refused because of the record's state is exit 6 with
/// the ERP's sentence.
/// </summary>
internal static partial class ScreenErrors
{
    public const string SessionLostNothingRanStep =
        "Start the record again from its header. If the header was saved, load it by key in a new header.";

    public const string SessionLostUnknownStep = "Read the record before doing anything else.";

    public const string MismatchStep = "Do not retry. Read what the session holds, or start again.";

    public const string UnverifiableStep = "Start again from the header.";

    public const string ActionRefusedStep =
        "Do not run the batch again: the record is not in a state that allows the action, so it would be refused the same way. "
        + "Tell the user what the ERP said.";

    private const string NothingRanPhrase = "Nothing in this call was executed";

    public static CliException Refine(CliException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.ExitCode == ExitCodes.TaskFailed)
        {
            if (CodedError().Match(error.Message) is { Success: true } coded)
            {
                var code = coded.Groups["code"].Value;
                return Rebuild(error, ExitCodes.ServerRefused, code, NextStep(code, error.Message) ?? ErrorGuidance.For(code));
            }

            if (RefusedAction().Match(error.Message) is { Success: true } refusal)
            {
                return new CliException(ExitCodes.TaskFailed, ScreenErrorCodes.ErpActionRefused, error.Message, ActionRefusedStep, error)
                {
                    TaskId = error.TaskId,
                    Detail = refusal.Value.Trim(),
                };
            }

            return error;
        }

        if (error.ExitCode == ExitCodes.ServerRefused && NextStep(error.Code, error.Message) is { } nextStep)
        {
            return Rebuild(error, error.ExitCode, error.Code, nextStep);
        }

        return error;
    }

    /// <summary>
    /// Returns whether <paramref name="error"/> means the record the last <c>--leave-open</c> left open
    /// can no longer be continued: the session lost it, holds another, or a batch ran on it since.
    /// </summary>
    public static bool EndsContinuation(CliException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.ExitCode == ExitCodes.TaskFailed
            || error.Code is ScreenErrorCodes.SessionLost or ScreenErrorCodes.ContinuationMismatch or ScreenErrorCodes.ContinuationUnverifiable;
    }

    private static string? NextStep(string code, string message) =>
        code switch
        {
            ScreenErrorCodes.SessionLost => message.Contains(NothingRanPhrase, StringComparison.OrdinalIgnoreCase)
                ? SessionLostNothingRanStep
                : SessionLostUnknownStep,
            ScreenErrorCodes.ContinuationMismatch => MismatchStep,
            ScreenErrorCodes.ContinuationUnverifiable => UnverifiableStep,
            _ => null,
        };

    private static CliException Rebuild(CliException error, int exitCode, string code, string? nextStep) =>
        new(exitCode, code, error.Message, nextStep, error)
        {
            TaskId = error.TaskId,
            Detail = error.Detail,
        };

    [GeneratedRegex(@"^\s*(?<code>(?:ATTACHMENT|SCREEN|IMPORT|REPORT|BC)_[A-Z0-9_]*[A-Z0-9])\b")]
    private static partial Regex CodedError();

    /// <summary>The ERP's sentence when it refuses an action because of the record's state, such as "The Release button is disabled."</summary>
    [GeneratedRegex(@"[^.!?\n]*(?:button is disabled|is on hold|requires approval)[^.!?\n]*[.!?]?", RegexOptions.IgnoreCase)]
    private static partial Regex RefusedAction();
}
