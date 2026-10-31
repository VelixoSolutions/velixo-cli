using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Commands;

/// <summary>Small helpers the command files share for reading a document and printing it.</summary>
internal static class CommandText
{
    public static string? String(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    /// <summary>Prints the warnings a tool result carries beside its data on stderr.</summary>
    public static void RelayWarnings(OutputWriter output, JsonNode? document)
    {
        if (document?["result"]?["warnings"] is JsonArray warnings)
        {
            foreach (JsonNode? warning in warnings)
            {
                output.Warn(String(warning) ?? warning?.ToJsonString() ?? string.Empty);
            }
        }
    }

    /// <summary>Quotes <paramref name="argument"/> for a command line shown to the user, when it holds a space.</summary>
    public static string Quote(string argument) =>
        argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;
}
