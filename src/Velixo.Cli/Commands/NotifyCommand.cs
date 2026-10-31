using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary><c>velixo notify</c>: <c>send_notification</c>.</summary>
public static class NotifyCommand
{
    private const string DefaultSourceName = "velixo-cli";

    public static Command Build(CliServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var subject = new Option<string>("--subject") { Description = "The notification's subject.", HelpName = "text", Required = true };
        var body = new Option<string>("--body") { Description = "The notification's text.", HelpName = "text", Required = true };
        var target = new Option<string?>("--target") { Description = "An email address or a Teams channel link; the signed-in user by default.", HelpName = "email|teams-link" };
        var payload = new Option<string?>("--payload") { Description = "A saved 'query run' output whose result.data is sent as the table.", HelpName = "file" };
        var link = new Option<string?>("--link") { Description = "A link to show with the notification.", HelpName = "url" };
        var linkLabel = new Option<string?>("--link-label") { Description = "The link's label.", HelpName = "text" };
        var severity = new Option<string?>("--severity") { Description = "Info, Warning or Error.", HelpName = "level" };
        severity.AcceptOnlyFromAmong("Info", "Warning", "Error");
        var sourceName = new Option<string?>("--source-name") { Description = $"Who sent it; {DefaultSourceName} by default.", HelpName = "name" };

        var command = new Command(
            "notify",
            "Send a notification to the portal, email, Teams, Slack or any other channel Velixo supports. Example: velixo notify --subject \"AR ready\" --body \"...\" --intent \"...\"")
        {
            subject,
            body,
            target,
            payload,
            link,
            linkLabel,
            severity,
            sourceName,
        };
        command.SetAction(async (parse, cancellationToken) =>
        {
            var intent = services.IntentOrDefault();
            var arguments = new JsonObject
            {
                [McpToolNames.IntentArgument] = intent,
                ["subject"] = parse.GetValue(subject),
                ["body"] = parse.GetValue(body),
            };

            Add(arguments, "target", parse.GetValue(target));
            if (parse.GetValue(payload) is { Length: > 0 } payloadFile)
            {
                arguments["payload"] = ReadPayload(Path.GetFullPath(payloadFile, services.Environment.CurrentDirectory));
            }

            Add(arguments, "link", parse.GetValue(link));
            Add(arguments, "linkLabel", parse.GetValue(linkLabel));
            Add(arguments, "severity", parse.GetValue(severity));
            arguments["sourceName"] = parse.GetValue(sourceName) is { Length: > 0 } name ? name : DefaultSourceName;

            McpToolResult called = await services.Mcp.CallToolAsync(McpToolNames.SendNotification, arguments, cancellationToken).ConfigureAwait(false);
            UnwrappedToolResult sent = ToolResultUnwrapper.Unwrap(called);
            CommandText.RelayWarnings(services.Output, sent.Document);

            if (sent.Data is JsonObject { } result && result["reachedNobody"] is JsonValue nobody && nobody.TryGetValue(out bool reachedNobody) && reachedNobody)
            {
                services.Output.Warn(
                    "The notification is recorded, but nothing pushed it to anyone: it waits in the portal until the recipient opens it. "
                    + "Do not report the recipient as notified.");
            }

            services.Output.WriteResult(sent.Data ?? sent.Document);
            return ExitCodes.Success;
        });
        return command;
    }

    private static void Add(JsonObject arguments, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            arguments[name] = value;
        }
    }

    /// <summary>
    /// Reads a saved <c>query run</c> output and returns what the notification's <c>payload</c> carries:
    /// its <c>result.data</c> as one JSON string, or the whole document when it is a query result itself.
    /// </summary>
    private static string ReadPayload(string path)
    {
        if (!File.Exists(path))
        {
            throw CliException.Usage($"The payload file '{path}' does not exist.");
        }

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"The payload file '{path}' is not valid JSON: {exception.Message}");
        }

        JsonNode? data = document is JsonObject root
            ? root["result"] is JsonObject result && result["data"] is { } found
                ? found
                : root.ContainsKey("columns") && root.ContainsKey("rows") ? root : null
            : null;

        return data is null
            ? throw CliException.Usage(
                $"The payload file '{path}' is not a saved 'query run' output: it has no result.data, and it is not a result with columns and rows.",
                "Save a query result with: velixo query run ... > result.json")
            : JsonText.Compact(data);
    }
}
