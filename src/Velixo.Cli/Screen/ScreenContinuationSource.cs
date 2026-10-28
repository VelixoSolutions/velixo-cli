using System.Text.Json.Nodes;
using Velixo.Cli.Commands;

namespace Velixo.Cli.Screen;

/// <summary>
/// A continuation a <c>screen run</c> call sends: the server's <c>{ screenId, keys, stamp }</c>, the MCP
/// session it was issued in and when it expires, each null when the source did not say.
/// </summary>
internal sealed class ScreenContinuationSource
{
    required public JsonObject Continuation { get; init; }

    public string? SessionId { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? ScreenId => CommandText.String(Continuation["screenId"]);

    /// <summary>Returns the record it names for a person, such as <c>SO301000 Document.OrderNbr = &lt;NEW&gt; (stamp 17)</c>.</summary>
    public string Describe()
    {
        var keys = Continuation["keys"] is JsonObject pairs
            ? string.Join(", ", pairs.Select(pair => $"{pair.Key} = {CommandText.String(pair.Value) ?? pair.Value?.ToJsonString()}"))
            : "no keys";
        return $"{ScreenId} {keys} (stamp {Continuation["stamp"]?.ToJsonString() ?? "none"})";
    }
}
