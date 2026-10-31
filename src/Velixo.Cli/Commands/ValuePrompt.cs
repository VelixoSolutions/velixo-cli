using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>An answer to <see cref="ValuePrompt.Ask"/>: skipped, an explicit null, or a value.</summary>
internal readonly record struct PromptAnswer(bool Skipped, string? Value)
{
    public static PromptAnswer Skip => new(true, null);

    public static PromptAnswer Null => new(false, null);
}

/// <summary>
/// Asks a person at a terminal for the values a run still needs, one line each, on stderr, so stdout
/// keeps only the result. A script, a pipe or an agent is never asked: it passes the values as flags.
/// </summary>
internal static class ValuePrompt
{
    /// <summary>The answer that asks for an explicit null, so the ERP sets the field to its default.</summary>
    public const string NullAnswer = "-";

    /// <summary>Gets whether the run can ask: stdin and stderr are a terminal, and no agent runs the CLI.</summary>
    public static bool CanAsk(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.Settings.IsInteractive && !services.Console.IsErrorRedirected;
    }

    /// <summary>
    /// Asks for one value. A required value is asked again until it gets an answer; an optional one is
    /// skipped by Enter. With <paramref name="allowNull"/>, <see cref="NullAnswer"/> is an explicit null.
    /// </summary>
    /// <exception cref="CliException">Exit 130 when stdin ends before an answer.</exception>
    public static PromptAnswer Ask(CliServices services, string name, string? about, bool required, bool allowNull = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        var label = string.IsNullOrWhiteSpace(about) ? name : $"{name} ({about})";
        while (true)
        {
            services.Console.Error.Write(required ? $"{label}: " : $"{label} [Enter to skip]: ");
            services.Console.Error.Flush();
            var line = services.Console.In.ReadLine() ?? throw Ended();
            var answer = line.Trim();

            if (allowNull && answer == NullAnswer)
            {
                return PromptAnswer.Null;
            }

            if (answer.Length > 0)
            {
                return new PromptAnswer(false, answer);
            }

            if (!required)
            {
                return PromptAnswer.Skip;
            }
        }
    }

    /// <summary>Asks a yes or no question; Enter takes <paramref name="defaultYes"/>.</summary>
    /// <exception cref="CliException">Exit 130 when stdin ends before an answer.</exception>
    public static bool YesNo(CliServices services, string question, bool defaultYes)
    {
        ArgumentNullException.ThrowIfNull(services);

        while (true)
        {
            services.Console.Error.Write($"{question} {(defaultYes ? "[Y/n]" : "[y/N]")} ");
            services.Console.Error.Flush();
            var answer = (services.Console.In.ReadLine() ?? throw Ended()).Trim();
            if (answer.Length == 0)
            {
                return defaultYes;
            }

            if (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase) || answer.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
    }

    private static CliException Ended() =>
        new(ExitCodes.Interrupted, ErrorCodes.Interrupted, "The input ended before every value was given; nothing ran.");
}
