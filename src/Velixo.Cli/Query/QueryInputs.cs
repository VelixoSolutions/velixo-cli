using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>
/// Reads what the query commands take from files and flags: query text, plan and parameter files,
/// and <c>--param</c> pairs. A relative path is read against <paramref name="currentDirectory"/>. Every
/// bad input is a usage error, exit 2, raised before any call.
/// </summary>
internal sealed class QueryInputs(ICliConsole console, string currentDirectory)
{
    private const string StandardInput = "-";

    private bool _stdinRead;

    /// <summary>Reads a file, or stdin for <c>-</c>; stdin can be read once per command.</summary>
    public string ReadText(string path, string flag)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path == StandardInput)
        {
            if (_stdinRead)
            {
                throw CliException.Usage($"stdin can feed only one input, and {flag} asked for it a second time.");
            }

            _stdinRead = true;
            return console.In.ReadToEnd();
        }

        try
        {
            return File.ReadAllText(Path.GetFullPath(path, currentDirectory));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CliException.Usage($"Could not read {flag} '{path}': {exception.Message}");
        }
    }

    /// <summary>Reads a query file and refuses an empty one.</summary>
    public string ReadQuery(string path, string flag)
    {
        var text = ReadText(path, flag);
        return string.IsNullOrWhiteSpace(text)
            ? throw CliException.Usage($"The query in {flag} '{path}' is empty.")
            : text;
    }

    public JsonNode ReadJson(string path, string flag)
    {
        var text = ReadText(path, flag);
        try
        {
            return JsonNode.Parse(text) ?? throw new JsonException("The document is empty.");
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"{flag} '{path}' is not valid JSON: {exception.Message}");
        }
    }

    /// <summary>
    /// Merges <c>--params</c> and <c>--param</c> into one object keyed by display label. A
    /// <c>--param</c> wins over the same key in the file.
    /// </summary>
    public JsonObject ReadParameters(IEnumerable<string>? pairs, string? file)
    {
        var parameters = new JsonObject();

        if (file is not null)
        {
            if (ReadJson(file, "--params") is not JsonObject fromFile)
            {
                throw CliException.Usage($"--params '{file}' must hold a JSON object of label and value pairs.");
            }

            foreach ((var key, JsonNode? value) in fromFile)
            {
                parameters[key] = value?.DeepClone();
            }
        }

        foreach (var pair in pairs ?? [])
        {
            (string label, string value) = SplitPair(pair);
            parameters[label] = value;
        }

        return parameters;
    }

    /// <summary>Splits <c>LABEL=VALUE</c> on its first <c>=</c>, so a value may hold more of them.</summary>
    public static (string Label, string Value) SplitPair(string pair)
    {
        ArgumentNullException.ThrowIfNull(pair);

        var equals = pair.IndexOf('=', StringComparison.Ordinal);
        var label = equals < 0 ? string.Empty : pair[..equals].Trim();
        return label.Length == 0
            ? throw CliException.Usage(
                $"--param '{pair}' is not LABEL=VALUE.",
                "Pass the parameter's display label, the text after AS in its DECLARE: --param \"As Of Date=2026-09-30\".")
            : (label, pair[(equals + 1)..]);
    }
}
