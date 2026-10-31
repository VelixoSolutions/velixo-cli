using System.CommandLine;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo guide</c>: how to use the commands, in the CLI's terms. The pages are embedded, so the guide
/// matches the flags of this build and needs neither a sign-in nor the network. The server's own guidance,
/// written for MCP clients, is <c>velixo tools guidance</c>.
/// </summary>
public static class GuideCommand
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var topics = new Argument<string[]>("topics")
        {
            Description = "Commands such as 'query run', a group such as 'command', or a page name. None prints the get-started page.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var command = new Command(
            "guide",
            "How to use the commands: the workflow, the rules, and what to do when a command fails. Example: velixo guide query run")
        {
            topics,
        };

        command.SetAction(parse =>
        {
            var words = parse.GetValue(topics) ?? [];
            IReadOnlyList<GuidePage> pages = words.Length == 0
                ? [.. GuidePages.All.Where(page => page.Name == GuidePages.GetStarted)]
                : GuidePages.Resolve(words);
            if (pages.Count == 0)
            {
                throw new CliException(
                    ExitCodes.Failure,
                    ErrorCodes.Internal,
                    "The guide is missing from this build.",
                    "Run 'velixo help --all' for the commands and their flags.");
            }

            if (services.Output.Format == OutputFormat.Json && services.Output.Settings.IsFormatExplicit)
            {
                services.Output.WriteJson(new JsonObject
                {
                    ["sections"] = new JsonArray(pages
                        .Select(page => (JsonNode?)new JsonObject
                        {
                            ["name"] = page.Name,
                            ["commands"] = new JsonArray(page.Commands.Select(name => (JsonNode?)name).ToArray()),
                            ["markdown"] = page.Markdown,
                        })
                        .ToArray()),
                });
            }
            else
            {
                services.Output.WriteText(string.Join("\n\n", pages.Select(page => page.Markdown)) + "\n");
            }

            return ExitCodes.Success;
        });

        return command;
    }
}
