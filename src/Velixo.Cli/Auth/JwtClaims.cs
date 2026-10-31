using System.Text.Json;
using System.Text.Json.Nodes;

namespace Velixo.Cli.Auth;

/// <summary>
/// Reads the payload of a JWT without verifying it. The CLI uses it only to show who is signed in;
/// the server verifies every token it receives.
/// </summary>
public static class JwtClaims
{
    public static JsonObject? ReadPayload(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(Base64Url.Decode(parts[1])) as JsonObject;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    public static string? GetString(JsonObject? claims, string name) =>
        claims?[name] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    public static DateTimeOffset? GetTime(JsonObject? claims, string name) =>
        claims?[name] is JsonValue value && value.TryGetValue(out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
