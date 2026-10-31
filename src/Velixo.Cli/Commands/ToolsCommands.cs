using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo tools</c>: <c>list</c> prints every tool the server offers, from <c>tools/list</c>, and
/// <c>guidance</c> prints the server's own tool guidance, from <c>tool_guidance</c>, in the server's MCP
/// terms. <c>velixo guide</c> holds the same rules in the CLI's terms.
/// </summary>
public static class ToolsCommands
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var names = new Option<bool>("--names") { Description = "Print only the tool names." };
        var list = new Command("list", "List the server's tools with their input schemas. Example: velixo tools list --names") { names };

        list.SetAction(async (parse, cancellationToken) =>
        {
            JsonArray tools = await services.Mcp.ListToolsAsync(cancellationToken).ConfigureAwait(false);

            if (parse.GetValue(names))
            {
                services.Output.WriteResult(new JsonArray(tools.Select(tool => (JsonNode?)CommandText.String(tool?["name"])).ToArray()));
            }
            else if (services.Output.Format == OutputFormat.Json)
            {
                services.Output.WriteJson(tools);
            }
            else
            {
                services.Output.WriteResult(new JsonArray(tools
                    .Select(tool => (JsonNode?)new JsonObject
                    {
                        ["name"] = CommandText.String(tool?["name"]),
                        ["description"] = FirstSentence(CommandText.String(tool?["description"])),
                    })
                    .ToArray()));
            }

            return ExitCodes.Success;
        });

        return new Command("tools", "The Velixo server's tools and its own guidance.") { list, BuildGuidance(services) };
    }

    private static Command BuildGuidance(CliServices services)
    {
        var topics = new Argument<string[]>("tools")
        {
            Description = "get_started, tool names, or command names such as 'command run'. None prints get_started.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var command = new Command(
            "guidance",
            "Print the server's tool guidance as the server writes it for its other clients. For the CLI's own guide, run 'velixo guide'. "
            + "Example: velixo tools guidance execute_commands")
        {
            topics,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
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
                services.Output.WriteJson(new JsonObject { ["sections"] = SplitGuidance(markdown, names) });
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
    /// Splits the server's guidance into its sections. The server sends the get-started recipe first when
    /// it was asked for (or when no name was recognised), then one <c>## tool_name</c> section per tool.
    /// </summary>
    public static JsonArray SplitGuidance(string markdown, IReadOnlyList<string> requested)
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

    private static string FirstSentence(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var flat = description.ReplaceLineEndings(" ");
        var end = flat.IndexOf(". ", StringComparison.Ordinal);
        return end < 0 ? flat : flat[..(end + 1)];
    }
}
