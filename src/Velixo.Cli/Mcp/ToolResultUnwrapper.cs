using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Velixo.Cli.Output;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Turns a <c>CallToolResult</c> into the CLI's output document, or into the <see cref="CliException"/>
/// its error maps to. Every command that calls a tool goes through here, so a tool error means the
/// same exit code whichever command met it.
/// </summary>
public static partial class ToolResultUnwrapper
{
    private const string BuilderRefusalPrefix = "ERROR:";

    private const string BuilderRefusalNextStep =
        "Put the builder's question to the user in plain words, then build again with their answer.";

    private static readonly string[] _signInPhrases =
    [
        "not signed in",
        "sign in again",
        "sign back in",
        "could not identify you",
    ];

    private static readonly string[] _accessPhrases =
    [
        "access is not currently permitted",
        "access is not enabled",
        "not enabled for",
        "cannot use velixo yet",
        "no velixo account",
        "no account",
        "quota",
        "out of credits",
        "no credits",
        "mcpserverenabled",
    ];

    public static UnwrappedToolResult Unwrap(McpToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = result.Texts.Count > 0 ? result.Texts[0] : string.Empty;
        var warnings = result.Texts.Skip(1).Where(extra => !string.IsNullOrWhiteSpace(extra)).ToList();
        JsonNode? parsed = TryParse(text);

        if (parsed is JsonObject envelope && (string?)envelope["status"] is { } status && envelope.ContainsKey("taskId"))
        {
            return UnwrapTask(envelope, status, result.IsError);
        }

        if (result.IsError)
        {
            throw MapErrorText(text);
        }

        var body = new JsonObject();
        if (parsed is not null)
        {
            body["data"] = parsed;
        }
        else
        {
            body["text"] = text;
        }

        if (warnings.Count > 0)
        {
            body["warnings"] = new JsonArray(warnings.Select(warning => (JsonNode?)warning).ToArray());
        }

        return new UnwrappedToolResult { Document = new JsonObject { ["result"] = body } };
    }

    /// <summary>
    /// Maps a tool's error text to an exit code: a coded server refusal is 5 with that code, a sign-in
    /// or access refusal is 4, a missing intent is 2, and anything else is 5 with <c>TOOL_ERROR</c>.
    /// </summary>
    public static CliException MapErrorText(string text, string? taskId = null)
    {
        var message = string.IsNullOrWhiteSpace(text) ? "The tool reported an error and said nothing more." : text.Trim();

        Match coded = CodedError().Match(message);
        if (coded.Success)
        {
            var code = coded.Groups["code"].Value;
            return new CliException(ExitCodes.ServerRefused, code, message, ErrorGuidance.For(code, message)) { TaskId = taskId };
        }

        if (message.Contains("`intent` is required", StringComparison.Ordinal))
        {
            return new CliException(ExitCodes.Usage, ErrorCodes.IntentRequired, message, IntentExample) { TaskId = taskId };
        }

        if (_signInPhrases.Any(phrase => message.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return new CliException(ExitCodes.AuthenticationRequired, ErrorCodes.NotSignedIn, message, "velixo login") { TaskId = taskId };
        }

        if (_accessPhrases.Any(phrase => message.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
        {
            return new CliException(
                ExitCodes.AuthenticationRequired,
                ErrorCodes.AccessDenied,
                message,
                "Relay the message to the user; a Velixo administrator grants access.")
            { TaskId = taskId };
        }

        return new CliException(ExitCodes.ServerRefused, ErrorCodes.ToolError, message) { TaskId = taskId };
    }

    /// <summary>Returns whether <paramref name="text"/> starts with one of the server's error codes, such as <c>ATTACHMENT_TOKEN_INVALID</c>.</summary>
    public static bool IsCodedError(string text) => CodedError().IsMatch(text.Trim());

    /// <summary>The example the CLI shows whenever a working tool is called without an intent.</summary>
    public const string IntentExample =
        "Add --intent \"<the user's goal in one or two sentences>\", for example --intent \"Find the overdue invoices to chase before month-end\", or set VELIXO_INTENT.";

    private static UnwrappedToolResult UnwrapTask(JsonObject envelope, string status, bool isError)
    {
        var taskId = (string?)envelope["taskId"];

        switch (status)
        {
            case TaskStatuses.Failed:
                throw MapFailedTask((string?)envelope["error"] ?? (string?)envelope["message"] ?? "The task failed.", taskId);

            case TaskStatuses.Expired:
                throw new CliException(
                    ExitCodes.TaskFailed,
                    ErrorCodes.TaskExpired,
                    (string?)envelope["message"] ?? "The task is not known to the server.",
                    "Start the command again; a task's result is kept only a few minutes after its last poll.")
                {
                    TaskId = taskId,
                };

            case TaskStatuses.Done:
                return new UnwrappedToolResult
                {
                    Document = UnwrapDone(envelope, taskId),
                    TaskId = taskId,
                    Status = status,
                };

            default:
                if (isError)
                {
                    throw MapErrorText((string?)envelope["error"] ?? (string?)envelope["message"] ?? string.Empty, taskId);
                }

                return new UnwrappedToolResult
                {
                    Document = (JsonObject)envelope.DeepClone(),
                    TaskId = taskId,
                    Status = status,
                };
        }
    }

    private static JsonObject UnwrapDone(JsonObject envelope, string? taskId)
    {
        var document = new JsonObject
        {
            ["taskId"] = taskId,
            ["status"] = TaskStatuses.Done,
        };

        var unwrapped = new JsonObject();
        if (envelope["result"] is JsonObject result)
        {
            if (result["textualResponse"] is JsonValue textual && textual.TryGetValue(out string? response))
            {
                if (IsBuilderRefusal(response))
                {
                    throw BuilderRefusal(response, taskId);
                }

                if (TryParse(response) is { } data)
                {
                    unwrapped["data"] = data;
                }
                else
                {
                    unwrapped["text"] = response;
                }
            }
            else if (result.ContainsKey("textualResponse"))
            {
                unwrapped["data"] = result["textualResponse"]?.DeepClone();
            }

            foreach ((var key, JsonNode? value) in result)
            {
                if (key != "textualResponse")
                {
                    unwrapped[key] = value?.DeepClone();
                }
            }
        }

        foreach ((var key, JsonNode? value) in envelope)
        {
            if (key is not ("taskId" or "status" or "result"))
            {
                document[key] = value?.DeepClone();
            }
        }

        document["result"] = unwrapped;
        return document;
    }

    /// <summary>
    /// Maps a failed task's error text. The server fails a build that ends in an <c>ERROR:</c> question,
    /// so that text is a builder refusal, exit 3. A text that starts with a server code is that code's
    /// refusal, exit 5, as when the tool returns it as an error. Any other failure is the ERP's, exit 6.
    /// </summary>
    private static CliException MapFailedTask(string error, string? taskId)
    {
        if (IsBuilderRefusal(error))
        {
            return BuilderRefusal(error, taskId);
        }

        if (IsCodedError(error))
        {
            return MapErrorText(error, taskId);
        }

        return new CliException(ExitCodes.TaskFailed, ErrorCodes.TaskFailed, error, TaskFailedNextStep(error))
        {
            TaskId = taskId,
            Detail = error,
        };
    }

    private static bool IsBuilderRefusal(string text) => text.TrimStart().StartsWith(BuilderRefusalPrefix, StringComparison.Ordinal);

    private static CliException BuilderRefusal(string text, string? taskId) =>
        new(ExitCodes.BuilderRefusal, ErrorCodes.BuilderRefusal, text.Trim(), BuilderRefusalNextStep) { TaskId = taskId };

    private static string TaskFailedNextStep(string error) =>
        error.Contains("400", StringComparison.Ordinal) || error.Contains("Bad Request", StringComparison.OrdinalIgnoreCase)
            ? "The ERP rejected the request. For a query, build it again quoting this error."
            : "Read the error; do not re-run a write before reading the record back.";

    private static JsonNode? TryParse(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is not ('{' or '['))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^(?<code>(?:ATTACHMENT|SCREEN|IMPORT|REPORT|BC)_[A-Z0-9_]*[A-Z0-9])\b")]
    private static partial Regex CodedError();
}
