using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>
/// <c>velixo call</c>: calls any tool with the arguments as given and prints the result unwrapped
/// like the commands do. A tool the server adds today is reachable through it before it has a command.
/// </summary>
public static class CallCommand
{
    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var tool = new Argument<string>("tool") { Description = "The tool name, as 'velixo tools list --names' prints it." };
        var args = new Option<string?>("--args")
        {
            Description = "The arguments as a JSON object, or @FILE to read them from a file.",
            HelpName = "json|@file",
        };
        var stdin = new Option<bool>("--stdin") { Description = "Read the arguments as a JSON object from stdin." };

        var command = new Command(
            "call",
            "Call any tool with raw arguments. --intent fills the intent a working tool requires. "
            + "Example: velixo call list_import_scenarios --args '{\"connectionName\":\"gab-dev\"}'")
        {
            tool,
            args,
            stdin,
        };

        command.SetAction(async (parse, cancellationToken) =>
        {
            var name = parse.GetValue(tool)!;
            JsonObject arguments = ReadArguments(services, parse.GetValue(args), parse.GetValue(stdin));

            if (McpToolNames.RequiringIntent.Contains(name) && !HasIntent(arguments))
            {
                arguments[McpToolNames.IntentArgument] = services.RequireIntent();
            }

            UnwrappedToolResult result = await services.Tasks
                .RunAsync(token => services.Mcp.CallToolAsync(name, arguments, token), services.FollowOptions(), cancellationToken)
                .ConfigureAwait(false);

            CommandText.RelayWarnings(services.Output, result.Document);
            services.Output.WriteResult(result.Document);
            return ExitCodes.Success;
        });

        return command;
    }

    private static bool HasIntent(JsonObject arguments) =>
        arguments[McpToolNames.IntentArgument] is JsonValue intent
        && intent.TryGetValue(out string? text)
        && !string.IsNullOrWhiteSpace(text);

    private static JsonObject ReadArguments(CliServices services, string? args, bool fromStdin)
    {
        if (args is not null && fromStdin)
        {
            throw CliException.Usage("Give the arguments with --args or --stdin, not both.");
        }

        string? json;
        string source;
        if (fromStdin)
        {
            json = services.Console.In.ReadToEnd();
            source = "stdin";
        }
        else if (args is { Length: > 0 } && args[0] == '@')
        {
            var path = Path.GetFullPath(args[1..], services.Environment.CurrentDirectory);
            json = File.Exists(path) ? File.ReadAllText(path) : throw CliException.Usage($"The arguments file '{path}' does not exist.");
            source = path;
        }
        else
        {
            json = args;
            source = "--args";
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject
                ?? throw CliException.Usage($"The arguments in {source} must be a JSON object.");
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"The arguments in {source} are not valid JSON: {exception.Message}");
        }
    }
}
