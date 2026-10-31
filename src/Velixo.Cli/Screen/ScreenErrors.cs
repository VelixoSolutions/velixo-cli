using System.Text.RegularExpressions;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// What a failed <c>execute_commands</c> call means for a command run beyond its code: an action the ERP
/// refused because of the record's state, and whether the record left open can still be continued.
/// </summary>
internal static partial class ScreenErrors
{
    public const string ActionRefusedStep =
        "Do not run the batch again: the record is not in a state that allows the action, so it would be refused the same way. "
        + "Tell the user what the ERP said.";

    /// <summary>
    /// Gives a failed task whose error is the ERP refusing an action, such as "The Release button is
    /// disabled.", the code <see cref="ErrorCodes.ErpActionRefused"/> and the ERP's sentence as its detail.
    /// It stays exit 6: the refusal is about the record, not the batch.
    /// </summary>
    public static CliException Refine(CliException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Code == ErrorCodes.TaskFailed && RefusedAction().Match(error.Message) is { Success: true } refusal)
        {
            return new CliException(ExitCodes.TaskFailed, ErrorCodes.ErpActionRefused, error.Message, ActionRefusedStep, error)
            {
                TaskId = error.TaskId,
                Detail = refusal.Value.Trim(),
            };
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
            || error.Code is ErrorCodes.ScreenSessionLost or ErrorCodes.ScreenContinuationMismatch or ErrorCodes.ScreenContinuationUnverifiable;
    }

    /// <summary>The ERP's sentence when it refuses an action because of the record's state, such as "The Release button is disabled."</summary>
    [GeneratedRegex(@"[^.!?\n]*(?:button is disabled|is on hold|requires approval)[^.!?\n]*[.!?]?", RegexOptions.IgnoreCase)]
    private static partial Regex RefusedAction();
}
