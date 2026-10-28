using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo tools list</c>: every tool the server offers, from <c>tools/list</c>.</summary>
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

        return new Command("tools", "The raw tool surface of the Velixo MCP server.") { list };
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
