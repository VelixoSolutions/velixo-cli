using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// The batch <c>velixo screen build</c> printed: the target, the header set and, for a document with
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

    /// <summary>Gets the build's note on how the record's key gets its value, or null when it gave none.</summary>
    public JsonNode? KeyNumbering { get; init; }

    public static ScreenBatch Read(string path)
    {
        if (!File.Exists(path))
        {
            throw CliException.Usage(
                $"The batch file '{path}' does not exist.",
                "Save the output of 'velixo screen build' to a file and pass it with --batch.");
        }

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"The batch file '{path}' is not valid JSON: {exception.Message}");
        }

        return From(document, path);
    }

    /// <summary>
    /// Reads a batch from the whole document <c>screen build</c> printed, from its <c>result.data</c>,
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
                "Pass the output of 'velixo screen build', or its result.data, with --batch.");
        }

        return new ScreenBatch
        {
            Target = target,
            Commands = commands,
            LineCommands = Set(body, "lineCommands", source),
            SplitCommands = Set(body, "splitCommands", source),
            FinishCommands = Set(body, "finishCommands", source),
            KeyNumbering = body["keyNumbering"],
        };
    }

    /// <summary>Returns whether every command of <paramref name="sets"/> is a <c>Get</c> or a <c>Clear</c>, neither of which writes.</summary>
    public static bool OnlyReads(IEnumerable<JsonArray?> sets) =>
        sets.Where(set => set is not null).SelectMany(set => set!).All(IsRead);

    private static bool IsRead(JsonNode? command) =>
        CommandText.String(command?["type"]) is { } type
        && (type.Equals("Get", StringComparison.OrdinalIgnoreCase) || type.Equals("Clear", StringComparison.OrdinalIgnoreCase));

    private static JsonArray? Set(JsonObject body, string name, string source) =>
        body[name] switch
        {
            null => null,
            JsonArray set => set,
            _ => throw CliException.Usage($"'{name}' in '{source}' is not an array of commands."),
        };
}
