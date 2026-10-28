using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo guide</c>: the server's tool guidance, fetched with <c>tool_guidance</c> so the rules keep
/// one home on the server, and <c>velixo guide --cli</c>: the CLI's own agent guide.
/// </summary>
public static class GuideCommand
{
    public const string AgentGuideResource = "Velixo.Cli.AgentGuide.md";

    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var topics = new Argument<string[]>("topics")
        {
            Description = "get_started, tool names, or command names such as 'screen run'. None prints get_started.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var cli = new Option<bool>("--cli") { Description = "Print the CLI's own agent guide: commands, exit codes, confirmations and continuations." };

        var command = new Command(
            "guide",
            "Read the rules for using the Velixo tools. Example: velixo guide get_started screen run")
        {
            topics,
            cli,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            if (parse.GetValue(cli))
            {
                services.Output.WriteText(ReadAgentGuide());
                return ExitCodes.Success;
            }

            var words = parse.GetValue(topics) ?? [];
            IReadOnlyList<string> names = words.Length == 0 ? [CommandMap.GetStarted] : CommandMap.ToToolNames(words);

            var arguments = new JsonObject
            {
                ["toolNames"] = new JsonArray(names.Select(name => (JsonNode?)name).ToArray()),
            };
            UnwrappedToolResult guidance = ToolResultUnwrapper.Unwrap(
                await services.Mcp.CallToolAsync(McpToolNames.ToolGuidance, arguments, cancellationToken).ConfigureAwait(false));
            var markdown = guidance.Text ?? guidance.Data?.ToJsonString() ?? string.Empty;

            if (services.Output.Format == OutputFormat.Json && services.Output.Settings.IsFormatExplicit)
            {
                services.Output.WriteJson(new JsonObject { ["sections"] = Split(markdown, names) });
            }
            else
            {
                services.Output.WriteText(markdown);
            }

            return ExitCodes.Success;
        });

        return command;
    }

    /// <summary>
    /// Splits the guidance into its sections. The server sends the get-started recipe first when it was
    /// asked for (or when no name was recognised), then one <c>## tool_name</c> section per tool.
    /// </summary>
    public static JsonArray Split(string markdown, IReadOnlyList<string> requested)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(requested);

        var starts = requested
            .Where(name => name != CommandMap.GetStarted)
            .Select(name => (Name: name, Index: HeadingIndex(markdown, name)))
            .Where(section => section.Index >= 0)
            .OrderBy(section => section.Index)
            .ToList();

        var sections = new JsonArray();
        var firstStart = starts.Count > 0 ? starts[0].Index : markdown.Length;
        if (firstStart > 0 && markdown[..firstStart].Trim().Length > 0)
        {
            sections.Add((JsonNode)Section(CommandMap.GetStarted, markdown[..firstStart]));
        }

        for (var index = 0; index < starts.Count; index++)
        {
            var end = index + 1 < starts.Count ? starts[index + 1].Index : markdown.Length;
            sections.Add((JsonNode)Section(starts[index].Name, markdown[starts[index].Index..end]));
        }

        return sections;
    }

    public static string ReadAgentGuide()
    {
        using Stream? stream = typeof(GuideCommand).Assembly.GetManifestResourceStream(AgentGuideResource);
        if (stream is null)
        {
            return "The agent guide is missing from this build. Run 'velixo help --all --output json' for the command tree.";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonObject Section(string name, string markdown) =>
        new() { ["name"] = name, ["markdown"] = markdown.Trim() };

    private static int HeadingIndex(string markdown, string name)
    {
        var heading = $"## {name}\n";
        if (markdown.StartsWith(heading, StringComparison.Ordinal))
        {
            return 0;
        }

        var index = markdown.IndexOf("\n" + heading, StringComparison.Ordinal);
        return index < 0 ? -1 : index + 1;
    }
}
