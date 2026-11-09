using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// Builds the <c>variables</c> map of an <c>execute_commands</c> call from <c>--params</c>, <c>--param</c>
/// and <c>--param-null</c>. A flag wins over the file. A null is the explicit null that lets the ERP
/// default the field, and an empty value is the empty string, which writes a blank. A <c>--param</c> value
/// passes through the resolver the caller gives, after the flags are checked against each other; a value
/// from the file is always taken as it is.
/// </summary>
internal static class ScreenVariables
{
    public static JsonObject Bind(
        string? variablesFile,
        IReadOnlyList<string> assignments,
        IReadOnlyList<string> nulls,
        Func<string, string> resolveValue)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(nulls);
        ArgumentNullException.ThrowIfNull(resolveValue);

        JsonObject variables = variablesFile is null ? [] : ReadFile(variablesFile);

        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var separator = assignment.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw CliException.Usage(
                    $"--param '{assignment}' has no name: write it as NAME=VALUE.",
                    "Use --param NAME= for an empty value and --param-null NAME for a null.");
            }

            assigned[assignment[..separator]] = assignment[(separator + 1)..];
        }

        foreach (var name in nulls)
        {
            if (assigned.ContainsKey(name))
            {
                throw CliException.Usage($"'{name}' is given both with --param and with --param-null; give it once.");
            }

            variables[name] = null;
        }

        foreach ((var name, var value) in assigned)
        {
            variables[name] = resolveValue(value);
        }

        return variables;
    }

    private static JsonObject ReadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw CliException.Usage($"The --params file '{path}' does not exist.");
        }

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"The --params file '{path}' is not valid JSON: {exception.Message}");
        }

        if (document is not JsonObject variables)
        {
            throw CliException.Usage($"The --params file '{path}' must hold a JSON object of names and values.");
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
