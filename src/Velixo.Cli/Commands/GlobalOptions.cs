using System.CommandLine;
using Velixo.Cli.Config;

namespace Velixo.Cli.Commands;

/// <summary>
/// The options every command accepts. They are recursive, so <c>velixo --env nx-dev status</c> and
/// <c>velixo status --env nx-dev</c> mean the same.
/// </summary>
public sealed class GlobalOptions
{
    public Option<string?> Profile { get; } = new("--profile")
    {
        Description = "The profile to use: an environment plus its sign-in. Defaults to the environment name.",
        HelpName = "name",
        Recursive = true,
    };

    public Option<string?> Environment { get; } = new("--env")
    {
        Description = "A built-in environment: prod, nx-staging, nx-stage-zero, nx-dev or local.",
        HelpName = "environment",
        Recursive = true,
    };

    public Option<string?> Url { get; } = new("--url")
    {
        Description = "The address of any other Velixo server, such as https://feature-x.velixo.com.",
        HelpName = "url",
        Recursive = true,
    };

    public Option<string?> Connection { get; } = new("--connection")
    {
        Description = "The ERP connection to use, as 'velixo connections list' names it.",
        HelpName = "name",
        Recursive = true,
    };

    public Option<string?> Intent { get; } = new("--intent")
    {
        Description = "Why this call is made: the user's goal in one or two sentences, which an administrator reads. "
            + "Optional: a command that does work in the ERP sends 'Run from the Velixo CLI: <command>' without it.",
        HelpName = "text",
        Recursive = true,
    };

    public Option<string?> Output { get; } = CreateOutputOption();

    public Option<bool> Quiet { get; } = new("--quiet", "-q")
    {
        Description = "Print no progress on stderr; warnings and errors still print.",
        Recursive = true,
    };

    public Option<bool> NoColor { get; } = new("--no-color")
    {
        Description = "Never colour the output. NO_COLOR does the same.",
        Recursive = true,
    };

    public Option<bool> NonInteractive { get; } = new("--non-interactive")
    {
        Description = "Never prompt and never open a browser. On by default when stdin is not a terminal or an AI agent is detected.",
        Recursive = true,
    };

    public Option<bool> Yes { get; } = new("--yes", "-y")
    {
        Description = "Answer yes to the confirmation a write asks for.",
        Recursive = true,
    };

    public Option<int?> Timeout { get; } = new("--timeout")
    {
        Description = "Stop waiting for a task after this many seconds and exit 124; the task keeps running.",
        HelpName = "seconds",
        Recursive = true,
    };

    public Option<bool> NoWait { get; } = new("--no-wait")
    {
        Description = "Do not wait for a task: print { taskId, status: \"running\" } and exit 0.",
        Recursive = true,
    };

    public Option<bool> Compact { get; } = new("--compact")
    {
        Description = "Print JSON on one line.",
        Recursive = true,
    };

    public IReadOnlyList<Option> All =>
        [Profile, Environment, Url, Connection, Intent, Output, Quiet, NoColor, NonInteractive, Yes, Timeout, NoWait, Compact];

    /// <summary>Reads the options from a parse, leaving out any value that failed to parse.</summary>
    public CommandLineValues Read(ParseResult parse)
    {
        ArgumentNullException.ThrowIfNull(parse);

        T? Value<T>(Option<T> option)
        {
            try
            {
                return parse.GetValue(option);
            }
            catch (InvalidOperationException)
            {
                return default;
            }
        }

        return new CommandLineValues
        {
            Profile = Value(Profile),
            Environment = Value(Environment),
            Url = Value(Url),
            Connection = Value(Connection),
            Intent = Value(Intent),
            Output = Value(Output),
            Quiet = Value(Quiet),
            NoColor = Value(NoColor),
            NonInteractive = Value(NonInteractive),
            Yes = Value(Yes),
            TimeoutSeconds = Value(Timeout),
            NoWait = Value(NoWait),
            Compact = Value(Compact),
        };
    }

    private static Option<string?> CreateOutputOption()
    {
        var option = new Option<string?>("--output", "-o")
        {
            Description = "json, table or csv. Defaults to json when stdout is not a terminal or an AI agent is detected, else table.",
            HelpName = "format",
            Recursive = true,
        };
        option.AcceptOnlyFromAmong("json", "table", "csv");
        return option;
    }
}
