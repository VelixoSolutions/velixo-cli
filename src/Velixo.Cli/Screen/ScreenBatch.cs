using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// The batch <c>velixo command build</c> printed: the target, the header set and, for a document with
/// any number of lines, the detail, sub-detail and finish sets.
/// </summary>
internal sealed class ScreenBatch
{
    required public string Target { get; init; }

    /// <summary>Gets the header set, or the whole batch when the build returned no command sets.</summary>
    required public JsonArray Commands { get; init; }

    public JsonArray? LineCommands { get; init; }

    public JsonArray? SplitCommands { get; init; }

    public JsonArray? FinishCommands { get; init; }

    /// <summary>Gets the build's description of each placeholder, <c>[{ name, type, description }]</c>, or null when it gave none.</summary>
    public JsonArray? Variables { get; init; }

    /// <summary>Gets the build's note on how the record's key gets its value, or null when it gave none.</summary>
    public JsonNode? KeyNumbering { get; init; }

    /// <summary>
    /// Gets the server's answer to whether the batch writes, the build's <c>writes</c>, or null when the batch
    /// came from a server that does not give one or was written by hand.
    /// </summary>
    public bool? ServerSaysWrites { get; init; }

    public static ScreenBatch Read(string path)
    {
        if (!File.Exists(path))
        {
            throw CliException.Usage(
                $"The batch file '{path}' does not exist.",
                "Save the output of 'velixo command build' to a file and pass it with --batch.");
        }

        return Parse(File.ReadAllText(path), $"the batch file '{path}'", path);
    }

    /// <summary>Reads a batch given inline, as <c>--batch -</c> takes it from stdin.</summary>
    public static ScreenBatch Parse(string json, string described, string source)
    {
        JsonNode? document;
        try
        {
            document = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"{char.ToUpperInvariant(described[0])}{described[1..]} is not valid JSON: {exception.Message}");
        }

        return From(document, source);
    }

    /// <summary>
    /// Reads a batch from the whole document <c>command build</c> printed, from its <c>result.data</c>,
    /// or from <c>{ data }</c>.
    /// </summary>
    /// <exception cref="CliException">Exit 2 when the document holds no target and header set.</exception>
    public static ScreenBatch From(JsonNode? document, string source)
    {
        JsonNode? batch = document switch
        {
            JsonObject whole when whole["result"] is JsonObject result => result["data"],
            JsonObject wrapped when !wrapped.ContainsKey("commands") && wrapped["data"] is JsonObject data => data,
            _ => document,
        };

        if (batch is not JsonObject body
            || CommandText.String(body["target"]) is not { Length: > 0 } target
            || body["commands"] is not JsonArray commands)
        {
            throw CliException.Usage(
                $"'{source}' holds no batch: a batch has a 'target' and a 'commands' array.",
                "Pass the output of 'velixo command build', or its result.data, with --batch.");
        }

        return new ScreenBatch
        {
            Target = target,
            Commands = commands,
            LineCommands = Set(body, "lineCommands", source),
            SplitCommands = Set(body, "splitCommands", source),
            FinishCommands = Set(body, "finishCommands", source),
            KeyNumbering = body["keyNumbering"],
            ServerSaysWrites = body["writes"] is JsonValue writes && writes.TryGetValue(out bool says) ? says : null,
            Variables = body["variables"] as JsonArray,
        };
    }

    /// <summary>
    /// Returns whether no command of <paramref name="sets"/> can leave a change in the ERP. A screen keeps a
    /// change only when an action such as Save or Release runs or a file is uploaded: a <c>Set</c> without one
    /// is dropped with the session. The actions that persist nothing are the ones the MCP server treats as
    /// reads: record navigation, Cancel, moving an inquiry's period, Refresh and RunReport. A command with
    /// <c>commit</c> and a <c>Set</c> on a note still count as writes, because Acumatica saves a note on a
    /// locked record without a Save. A command type the CLI does not know is a write.
    /// </summary>
    /// <remarks>
    /// The server decides whether a batch writes and sends the answer with the build, as <see cref="ServerSaysWrites"/>.
    /// This check stays beside it because a batch file can be edited after its build: <see cref="Writes"/> counts the
    /// batch as a write when either says so.
    /// </remarks>
    public static bool OnlyReads(IEnumerable<JsonArray?> sets) =>
        sets.Where(set => set is not null).SelectMany(set => set!).All(IsRead);

    /// <summary>
    /// Returns whether running <paramref name="sets"/> of this batch can leave a change in the ERP: when the server
    /// said so, or when <see cref="OnlyReads"/> finds a command that can persist. Without the server's answer, the
    /// check alone decides.
    /// </summary>
    public bool Writes(IEnumerable<JsonArray?> sets) => ServerSaysWrites == true || !OnlyReads(sets);

    private static readonly string[] _readTypes = ["Get", "Clear", "Key", "Set", "ListAttachments", "DownloadAttachments"];

    private static readonly string[] _noteFields = ["NoteText", "NotePopupText"];

    /// <summary>
    /// Mirrors <c>ScreenRecordCheck.ReadOnlyActions</c> in the MCP server, which takes it from the Acumatica
    /// customization. The server's <c>writes</c> is the answer; this list only keeps an edited batch honest.
    /// </summary>
    private static readonly string[] _readOnlyActions = ["First", "Previous", "Next", "Last", "Cancel", "PreviousPeriod", "NextPeriod", "Refresh", "RunReport"];

    private static bool IsRead(JsonNode? command)
    {
        if (CommandText.String(command?["type"]) is not { } type
            || (command?["commit"] is JsonValue commit && commit.TryGetValue(out bool commits) && commits))
        {
            return false;
        }

        if (type.Equals("Action", StringComparison.OrdinalIgnoreCase))
        {
            return CommandText.String(command?["name"])?.Trim() is { } action
                && _readOnlyActions.Contains(action, StringComparer.OrdinalIgnoreCase);
        }

        if (!_readTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return !type.Equals("Set", StringComparison.OrdinalIgnoreCase)
            || CommandText.String(command?["name"]) is not { } name
            || !_noteFields.Any(note => name.Equals(note, StringComparison.OrdinalIgnoreCase) || name.EndsWith("." + note, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonArray? Set(JsonObject body, string name, string source) =>
        body[name] switch
        {
            null => null,
            JsonArray set => set,
            _ => throw CliException.Usage($"'{name}' in '{source}' is not an array of commands."),
        };
}
