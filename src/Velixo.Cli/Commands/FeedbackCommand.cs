using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo feedback</c>: <c>submit_feedback</c>.</summary>
public static class FeedbackCommand
{
    /// <summary>The <c>--tool</c> value for a capability no command offers yet, as the server spells it.</summary>
    public const string NoTool = "none";

    /// <summary>The categories <c>submit_feedback</c> accepts.</summary>
    public static readonly string[] Categories =
    [
        "missing-capability",
        "missing-filter",
        "misleading-description",
        "wrong-result",
        "unclear-error",
        "slow",
        "other",
    ];

    /// <summary>The severities <c>submit_feedback</c> accepts.</summary>
    public static readonly string[] Severities = ["blocked", "worked-around", "minor"];

    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var tool = new Option<string>("--tool")
        {
            Description = $"The command or tool the problem is about, such as 'query run' or execute_query; '{NoTool}' when no command does it.",
            HelpName = "command|tool",
            Required = true,
        };
        var category = new Option<string>("--category")
        {
            Description = "What went wrong: " + string.Join(", ", Categories) + ".",
            HelpName = "category",
            Required = true,
        };
        category.AcceptOnlyFromAmong(Categories);
        var severity = new Option<string>("--severity")
        {
            Description = "blocked (you could not do what the user asked), worked-around (you got there another way) or minor.",
            HelpName = "level",
            Required = true,
        };
        severity.AcceptOnlyFromAmong(Severities);
        var attempted = new Option<string>("--attempted") { Description = "What the user wanted, and what you tried.", HelpName = "text", Required = true };
        var problem = new Option<string>("--problem") { Description = "What the command did or lacked, and its error text.", HelpName = "text", Required = true };
        var proposedChange = new Option<string>("--proposed-change")
        {
            Description = "The change you propose: the flag, filter or wording to add, and what you would then have run.",
            HelpName = "text",
            Required = true,
        };
        var workaround = new Option<string?>("--workaround") { Description = "How you got the answer anyway, when you did.", HelpName = "text" };

        var command = new Command(
            "feedback",
            "Report to Velixo R&D what kept a command from doing what the user needed, and the change you propose. "
            + "Example: velixo feedback --tool \"query run\" --category missing-filter --severity worked-around --attempted \"...\" --problem \"...\" --proposed-change \"...\"")
        {
            tool,
            category,
            severity,
            attempted,
            problem,
            proposedChange,
            workaround,
        };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var arguments = new JsonObject
            {
                ["tool"] = ToToolName(parse.GetValue(tool)!),
                ["category"] = parse.GetValue(category),
                ["severity"] = parse.GetValue(severity),
                ["attempted"] = Required(parse.GetValue(attempted), "--attempted"),
                ["problem"] = Required(parse.GetValue(problem), "--problem"),
                ["proposedChange"] = Required(parse.GetValue(proposedChange), "--proposed-change"),
            };

            if (parse.GetValue(workaround) is { } text && !string.IsNullOrWhiteSpace(text))
            {
                arguments["workaround"] = text;
            }

            McpToolResult called = await services.Mcp.CallToolAsync(McpToolNames.SubmitFeedback, arguments, cancellationToken).ConfigureAwait(false);
            UnwrappedToolResult submitted = ToolResultUnwrapper.Unwrap(called);
            CommandText.RelayWarnings(services.Output, submitted.Document);
            services.Output.WriteResult(submitted.Data ?? submitted.Document);
            return ExitCodes.Success;
        });
        return command;
    }

    /// <summary>
    /// Returns the server's tool name for <paramref name="value"/>: a command such as <c>query run</c> as the
    /// tool it calls, anything else (a tool name, <c>none</c>) as given.
    /// </summary>
    public static string ToToolName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw CliException.Usage($"--tool is empty. Name the command, such as 'query run', or '{NoTool}' when no command does it.");
        }

        var words = string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (words.StartsWith("velixo ", StringComparison.Ordinal))
        {
            words = words["velixo ".Length..];
        }

        return CommandMap.ToolByCommand.TryGetValue(words, out var mapped) ? mapped : trimmed;
    }

    private static string Required(string? value, string option) =>
        string.IsNullOrWhiteSpace(value) ? throw CliException.Usage($"{option} is empty. Say it in a few sentences.") : value;
}
