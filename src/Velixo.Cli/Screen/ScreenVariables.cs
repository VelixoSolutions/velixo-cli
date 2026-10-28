using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// Builds the <c>variables</c> map of an <c>execute_commands</c> call from <c>--vars</c>, <c>--var</c>
/// and <c>--var-null</c>. A flag wins over the file. A null is the explicit null that lets the ERP
/// default the field, and an empty value is the empty string, which writes a blank.
/// </summary>
internal static class ScreenVariables
{
    public static JsonObject Bind(string? variablesFile, IReadOnlyList<string> assignments, IReadOnlyList<string> nulls)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(nulls);

        JsonObject variables = variablesFile is null ? [] : ReadFile(variablesFile);

        var assigned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var separator = assignment.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw CliException.Usage(
                    $"--var '{assignment}' has no name: write it as NAME=VALUE.",
                    "Use --var NAME= for an empty value and --var-null NAME for a null.");
            }

            var name = assignment[..separator];
            variables[name] = assignment[(separator + 1)..];
            assigned.Add(name);
        }

        foreach (var name in nulls)
        {
            if (assigned.Contains(name))
            {
                throw CliException.Usage($"'{name}' is given both with --var and with --var-null; give it once.");
            }

            variables[name] = null;
        }

        return variables;
    }

    private static JsonObject ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw CliException.Usage($"The variables file '{path}' does not exist.");
        }

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"The variables file '{path}' is not valid JSON: {exception.Message}");
        }

        if (document is not JsonObject variables)
        {
            throw CliException.Usage($"The variables file '{path}' must hold a JSON object of names and values.");
        }

        foreach ((var name, JsonNode? value) in variables)
        {
            if (value is JsonObject or JsonArray)
            {
                throw CliException.Usage($"'{name}' in '{path}' is not a single value: give a string, a number, true, false or null.");
            }
        }

        return variables;
    }
}
