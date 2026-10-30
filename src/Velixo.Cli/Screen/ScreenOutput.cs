using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>Prints a batch from <c>command build</c> for a person at a terminal.</summary>
internal static class ScreenOutput
{
    /// <summary>The file name the run command suggests when the batch was not saved.</summary>
    public const string SuggestedFile = "batch.json";

    /// <summary>
    /// Prints the batch. A terminal gets it to read: the screen, whether it writes, the values a run
    /// takes, how the record gets its key, and the commands that run it. JSON prints as it came.
    /// </summary>
    /// <param name="savedTo">The file <c>--save</c> wrote the batch to; null when it was not saved.</param>
    /// <param name="connection">The connection the build named with <c>--connection</c>, which the run commands repeat; null otherwise.</param>
    /// <param name="shell">The shell the inline form is written for.</param>
    /// <param name="number">How the run commands name the batch in the library, its name or its number; null when it is not in it.</param>
    public static void WriteBatch(
        OutputWriter output,
        JsonObject document,
        string? savedTo,
        string? connection = null,
        InlineShell shell = InlineShell.Posix,
        string? number = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);

        ScreenBatch? batch = output.Format == OutputFormat.Table ? TryRead(document) : null;
        if (batch is null)
        {
            output.WriteResult(document);
            return;
        }

        output.WriteText(DescribeBatch(batch, document["result"]?["data"] as JsonObject, savedTo, output.Settings.UseColor, connection, shell, number));
    }

    public static string DescribeBatch(
        ScreenBatch batch,
        JsonObject? data,
        string? savedTo,
        bool useColor,
        string? connection = null,
        InlineShell shell = InlineShell.Posix,
        string? number = null)
    {
        ArgumentNullException.ThrowIfNull(batch);

        string Heading(string text) => useColor ? $"\u001b[1m{text}\u001b[0m" : text;

        JsonArray?[] sets = [batch.Commands, batch.LineCommands, batch.SplitCommands, batch.FinishCommands];
        var writes = batch.Writes(sets);
        var count = sets.Sum(set => set?.Count ?? 0);

        var text = new StringBuilder();
        text.AppendLine(Heading($"Screen {batch.Target}"));
        text.AppendLine($"  {count} {(count == 1 ? "command" : "commands")}; {(writes ? "it changes the record, so a run needs --yes" : "it only reads")}");
        text.AppendLine();

        var headerNames = ScreenPlaceholders.Names(batch.Commands).Concat(ScreenPlaceholders.Names(batch.FinishCommands)).Distinct(StringComparer.Ordinal).ToList();
        var lineNames = ScreenPlaceholders.Names(batch.LineCommands).Concat(ScreenPlaceholders.Names(batch.SplitCommands))
            .Distinct(StringComparer.Ordinal)
            .Except(headerNames, StringComparer.Ordinal)
            .ToList();
        var described = (data?["variables"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(variable => CommandText.String(variable["name"]) is not null)
            .GroupBy(variable => CommandText.String(variable["name"])!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (headerNames.Count > 0)
        {
            var width = headerNames.Max(name => name.Length);
            text.AppendLine(Heading("Values"));
            foreach (var name in headerNames)
            {
                var about = described.TryGetValue(name, out JsonObject? variable)
                    ? string.Join(", ", new[] { CommandText.String(variable["type"]), CommandText.String(variable["description"]) }.Where(part => !string.IsNullOrWhiteSpace(part)))
                    : string.Empty;
                text.AppendLine($"  {name.PadRight(width)}  {about}".TrimEnd());
            }

            text.AppendLine();
        }

        if (batch.LineCommands is not null)
        {
            text.AppendLine(Heading("Detail rows"));
            text.AppendLine(lineNames.Count == 0
                ? "  Each row of --rows FILE adds one line."
                : $"  Each row of --rows FILE adds one line, with values for {string.Join(", ", lineNames)}:");
            if (lineNames.Count > 0)
            {
                text.AppendLine($"  [{{ \"values\": {{ {string.Join(", ", lineNames.Select(name => $"\"{name}\": \"...\""))} }} }}]");
            }

            text.AppendLine();
        }

        if (batch.KeyNumbering is JsonObject numbering && CommandText.String(numbering["field"]) is { Length: > 0 } field)
        {
            var assigned = numbering["autonumbered"] is JsonValue flag && flag.TryGetValue(out bool autonumbered) && autonumbered;
            text.AppendLine(Heading("Key"));
            text.AppendLine(assigned
                ? $"  The ERP assigns {field} when the record is saved; the run prints it."
                : $"  {field} is not numbered by the ERP: it comes from the values you give.");
            text.AppendLine();
        }

        var values = new StringBuilder();
        foreach (var name in headerNames)
        {
            values.Append($" --param {name}=...");
        }

        if (batch.LineCommands is not null)
        {
            values.Append(" --rows rows.json");
        }

        var forced = connection is null ? string.Empty : $" --connection {CommandText.Quote(connection)}";
        var asks = headerNames.Count > 0 || batch.LineCommands is not null;
        var file = savedTo ?? SuggestedFile;
        var source = number is { } saved && savedTo is null ? saved : $"--batch {CommandText.Quote(file)}";
        var command = new StringBuilder($"velixo command run {source}{values}{forced}");

        text.AppendLine(Heading("Run it"));
        if (number is { } listed)
        {
            var prompt = asks
                ? (batch.LineCommands is not null ? "asks for each value and line" : "asks for each value")
                : null;
            text.AppendLine(writes
                ? $"  velixo command run {listed}{forced}    # {(prompt is null ? string.Empty : prompt + ", ")}shows what will change, asks before it writes"
                : $"  velixo command run {listed}{forced}{(prompt is null ? string.Empty : $"    # {prompt}")}");
        }

        if (writes)
        {
            text.AppendLine($"  {command} --dry-run    # check it, and show the user what will change");
            text.AppendLine($"  {command} --yes        # then run it, once the user agrees");
        }
        else if (asks || number is null)
        {
            text.AppendLine($"  {command}");
        }

        // The batch also runs inline, from stdin, in the form the user's shell takes. A batch on stdin
        // cannot answer a prompt, so its write takes --yes.
        var inlineCommand = $"velixo command run --batch -{values}{forced}" + (writes ? " --dry-run" : string.Empty);
        if (InlineInput.Lines(shell, inlineCommand, JsonText.Compact(data ?? new JsonObject()), "JSON") is { } inline)
        {
            text.AppendLine();
            text.AppendLine(writes ? "Or inline (check with --dry-run, then run again with --yes):" : "Or inline:");
            foreach (var line in inline)
            {
                text.AppendLine(line);
            }
        }

        return text.ToString();
    }

    /// <summary>Returns whether <paramref name="document"/> holds a batch, rather than a builder's refusal.</summary>
    public static bool IsBatch(JsonObject document) => TryRead(document) is not null;

    private static ScreenBatch? TryRead(JsonObject document)
    {
        try
        {
            return ScreenBatch.From(document, "the build");
        }
        catch (CliException)
        {
            return null;
        }
    }
}
