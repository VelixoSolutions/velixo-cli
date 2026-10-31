using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>Reads the <c>--rows</c> file: a JSON array of detail rows, each <c>{ values, rows? }</c>.</summary>
internal static class ScreenRows
{
    public static JsonArray Read(string path)
    {
        if (!File.Exists(path))
        {
            throw CliException.Usage($"The rows file '{path}' does not exist.");
        }

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"The rows file '{path}' is not valid JSON: {exception.Message}");
        }

        if (document is not JsonArray rows)
        {
            throw CliException.Usage(
                $"The rows file '{path}' must hold a JSON array of rows.",
                "Write each detail row as { \"values\": { \"Name\": \"value\" } }, and [] for none.");
        }

        for (var line = 0; line < rows.Count; line++)
        {
            CheckRow(rows[line], $"line {line + 1}", path);
            if (rows[line]!["rows"] is JsonArray splits)
            {
                for (var split = 0; split < splits.Count; split++)
                {
                    CheckRow(splits[split], $"line {line + 1}, split {split + 1}", path);
                }
            }
        }

        return rows;
    }

    /// <summary>Returns the number of sub-detail rows under all the detail rows.</summary>
    public static int CountSplits(JsonArray? rows) =>
        rows?.Sum(row => row?["rows"] is JsonArray splits ? splits.Count : 0) ?? 0;

    private static void CheckRow(JsonNode? row, string name, string path)
    {
        if (row is not JsonObject entry)
        {
            throw CliException.Usage($"{name} in '{path}' is not an object: write it as {{ \"values\": {{ ... }} }}.");
        }

        if (entry["values"] is not (null or JsonObject))
        {
            throw CliException.Usage($"The values of {name} in '{path}' are not an object of names and values.");
        }

        if (entry["rows"] is not (null or JsonArray))
        {
            throw CliException.Usage($"The rows of {name} in '{path}' are not an array.");
        }
    }
}
