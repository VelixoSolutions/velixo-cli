using System.Text.Json.Nodes;
using Velixo.Cli.Commands;

namespace Velixo.Cli.Screen;

/// <summary>
/// Asks a person at a terminal for what a batch run still needs: each header value no flag gave, then,
/// for a document with any number of lines, one line at a time, with its sub-lines when the batch has
/// them. <see cref="ValuePrompt.NullAnswer"/> leaves a field to the ERP's default.
/// </summary>
internal static class ScreenPrompts
{
    /// <summary>Adds an answer to <paramref name="variables"/> for every header placeholder it does not bind.</summary>
    public static void AskForValues(CliServices services, ScreenBatch batch, JsonObject variables)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(variables);

        var missing = HeaderNames(batch).Where(name => !variables.ContainsKey(name)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        services.Console.Error.WriteLine($"Values for the batch (type {ValuePrompt.NullAnswer} to leave a field to the ERP's default):");
        foreach (var name in missing)
        {
            PromptAnswer answer = ValuePrompt.Ask(services, name, About(batch, name), required: true, allowNull: true);
            variables[name] = answer.Value;
        }
    }

    /// <summary>
    /// Asks for the detail lines of a batch that has <c>lineCommands</c>: a line at a time until the person
    /// says no more, each with its sub-lines for a batch that has <c>splitCommands</c>.
    /// </summary>
    public static JsonArray AskForRows(CliServices services, ScreenBatch batch, JsonObject variables)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(variables);

        var bound = HeaderNames(batch).Concat(variables.Select(pair => pair.Key)).ToHashSet(StringComparer.Ordinal);
        var lineNames = ScreenPlaceholders.Names(batch.LineCommands).Where(name => !bound.Contains(name)).ToList();
        var splitNames = ScreenPlaceholders.Names(batch.SplitCommands).Where(name => !bound.Contains(name) && !lineNames.Contains(name)).ToList();

        var rows = new JsonArray();
        while (ValuePrompt.YesNo(services, rows.Count == 0 ? "Add a line?" : "Add another line?", defaultYes: rows.Count == 0))
        {
            services.Console.Error.WriteLine($"Line {rows.Count + 1}:");
            var row = new JsonObject { ["values"] = AskRow(services, batch, lineNames, "  ") };

            if (batch.SplitCommands is not null)
            {
                var splits = new JsonArray();
                while (ValuePrompt.YesNo(services, splits.Count == 0 ? "  Add a sub-line to this line?" : "  Add another sub-line?", defaultYes: false))
                {
                    services.Console.Error.WriteLine($"  Sub-line {splits.Count + 1}:");
                    splits.Add((JsonNode)new JsonObject { ["values"] = AskRow(services, batch, splitNames, "    ") });
                }

                if (splits.Count > 0)
                {
                    row["rows"] = splits;
                }
            }

            rows.Add((JsonNode)row);
        }

        return rows;
    }

    private static JsonObject AskRow(CliServices services, ScreenBatch batch, IReadOnlyList<string> names, string indent)
    {
        var values = new JsonObject();
        foreach (var name in names)
        {
            PromptAnswer answer = ValuePrompt.Ask(services, indent + name, About(batch, name), required: true, allowNull: true);
            values[name] = answer.Value;
        }

        return values;
    }

    /// <summary>The placeholders a run binds once: those of the header and the finish commands.</summary>
    private static List<string> HeaderNames(ScreenBatch batch) =>
        ScreenPlaceholders.Names(batch.Commands).Concat(ScreenPlaceholders.Names(batch.FinishCommands)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The build's description of a placeholder: its type and what it is, when the build gave them.</summary>
    private static string? About(ScreenBatch batch, string name)
    {
        JsonObject? variable = batch.Variables?.OfType<JsonObject>().FirstOrDefault(item => CommandText.String(item["name"]) == name);
        if (variable is null)
        {
            return null;
        }

        var parts = new[] { CommandText.String(variable["type"]), CommandText.String(variable["description"]) }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
