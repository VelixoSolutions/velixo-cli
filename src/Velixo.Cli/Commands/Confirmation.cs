using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// The go-ahead a command needs before it writes to the ERP: <c>--yes</c> gives it, a terminal asks the
/// question with <c>[y/N]</c>, and without either the command stops before it writes anything.
/// </summary>
internal static class Confirmation
{
    private const string DeclinedNextStep = "Nothing was written. Ask the user what to change before running the command again.";

    /// <summary>
    /// Returns once the write may go ahead. The <paramref name="summary"/> goes to stderr in every case: as
    /// progress when nothing is asked, and in full before the question, so <c>--quiet</c> cannot hide what
    /// the person is agreeing to.
    /// </summary>
    /// <exception cref="CliException">
    /// Exit 2 <see cref="ErrorCodes.ConfirmationRequired"/> without <c>--yes</c> and without a terminal, with
    /// <paramref name="notAskedNextStep"/>, which names <c>--yes</c>; exit 2
    /// <see cref="ErrorCodes.ConfirmationDeclined"/> when the answer on the terminal is not yes.
    /// </exception>
    public static void Require(
        CliServices services,
        IReadOnlyList<string> summary,
        string question,
        string notAsked,
        string notAskedNextStep,
        string declined)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(summary);

        if (services.Settings.AssumeYes || !services.Settings.IsInteractive)
        {
            foreach (var line in summary)
            {
                services.Output.Progress(line);
            }

            if (services.Settings.AssumeYes)
            {
                return;
            }

            throw new CliException(ExitCodes.Usage, ErrorCodes.ConfirmationRequired, notAsked, notAskedNextStep);
        }

        foreach (var line in summary)
        {
            services.Console.Error.WriteLine(line);
        }

        services.Console.Error.Write($"{question} [y/N] ");
        services.Console.Error.Flush();
        var answer = services.Console.In.ReadLine()?.Trim();
        if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) || string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new CliException(ExitCodes.Usage, ErrorCodes.ConfirmationDeclined, declined, DeclinedNextStep);
    }
}
