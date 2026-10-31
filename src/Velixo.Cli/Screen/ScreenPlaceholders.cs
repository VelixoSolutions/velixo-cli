using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Velixo.Cli.Screen;

/// <summary>
/// Reads the <c>{{Name}}</c> placeholders of a command set the way the server's binder does: a
/// placeholder is a command's whole <c>value</c>, never a part of it and never its <c>name</c>.
/// </summary>
internal static partial class ScreenPlaceholders
{
    /// <summary>Returns the placeholder names <paramref name="commands"/> use, in the order they first appear.</summary>
    public static IReadOnlyList<string> Names(JsonArray? commands)
    {
        var names = new List<string>();
        if (commands is null)
        {
            return names;
        }

        foreach (JsonNode? command in commands)
        {
            if (command?["value"] is JsonValue value
                && value.TryGetValue(out string? text)
                && WholeValueToken().Match(text) is { Success: true } match
                && !names.Contains(match.Groups[1].Value, StringComparer.Ordinal))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        return names;
    }

    [GeneratedRegex(@"^\s*\{\{([A-Za-z_][A-Za-z0-9_]{0,63})\}\}\s*$")]
    private static partial Regex WholeValueToken();
}
