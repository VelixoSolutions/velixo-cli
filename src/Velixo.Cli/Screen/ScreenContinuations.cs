using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// Keeps the record a <c>--leave-open</c> call left open in the profile state, and refuses locally a
/// continuation the server would refuse: one from another MCP session, one past its deadline, and one
/// for another screen.
/// </summary>
internal static class ScreenContinuations
{
    /// <summary>The idle time after which the server closes a screen session, measured from the response that left the record open.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long after its deadline a continuation is still sent. The deadline is measured on this
    /// machine's clock, which may drift from the server's, so the local check never refuses a call
    /// the server might still accept.
    /// </summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(10);

    public static ScreenContinuationSource LoadStored(ProfileState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.LastContinuation is not { Continuation: JsonObject continuation } stored)
        {
            throw new CliException(
                ExitCodes.Usage,
                ErrorCodes.NoContinuation,
                "No record is open from an earlier 'command run --leave-open' on this profile.",
                "Run the batch from its header with --leave-open first, or pass a saved continuation with --continuation FILE.");
        }

        return new ScreenContinuationSource
        {
            Continuation = (JsonObject)continuation.DeepClone(),
            SessionId = stored.SessionId,
            ExpiresAt = stored.ExpiresAt,
        };
    }

    /// <summary>
    /// Reads a continuation from <paramref name="path"/>: the <c>continuation</c> object itself, or a whole
    /// saved <c>command run</c> result that carries one. When it is the continuation this profile stored,
    /// the stored session id and deadline are checked too.
    /// </summary>
    public static ScreenContinuationSource ReadFile(string path, ProfileState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!File.Exists(path))
        {
            throw CliException.Usage($"The continuation file '{path}' does not exist.");
        }

        JsonNode? document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw CliException.Usage($"The continuation file '{path}' is not valid JSON: {exception.Message}");
        }

        JsonObject? holder = document switch
        {
            JsonObject whole when whole["result"] is JsonObject result => result,
            JsonObject other => other,
            _ => null,
        };

        (JsonObject? continuation, JsonNode? expiresAt) = holder switch
        {
            null => (null, null),
            _ when holder.ContainsKey("screenId") => (holder, null),
            _ => (holder["continuation"] as JsonObject, holder["continuationExpiresAt"] ?? holder["expiresAt"]),
        };

        if (continuation is null || CommandText.String(continuation["screenId"]) is null)
        {
            throw CliException.Usage(
                $"'{path}' holds no continuation: it needs {{ screenId, keys, stamp }} or a result that carries one.",
                "Save result.continuation from the output of a 'command run --leave-open' call.");
        }

        StoredContinuation? stored = state.LastContinuation is { Continuation: { } known } match
            && JsonNode.DeepEquals(known, continuation)
                ? match
                : null;

        return new ScreenContinuationSource
        {
            Continuation = (JsonObject)continuation.DeepClone(),
            SessionId = stored?.SessionId,
            ExpiresAt = ParseTime(expiresAt) ?? stored?.ExpiresAt,
        };
    }

    /// <summary>Refuses with exit 5, and without a round trip, a continuation the server would refuse before running anything.</summary>
    public static void Check(ScreenContinuationSource source, string? currentSessionId, string target, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source.SessionId is not null && !string.Equals(source.SessionId, currentSessionId, StringComparison.Ordinal))
        {
            throw new CliException(
                ExitCodes.ServerRefused,
                ErrorCodes.ScreenSessionLost,
                $"{ErrorCodes.ScreenSessionLost}: the record was left open in an MCP session this CLI no longer uses, so the screen "
                + "session that held it is gone. Nothing was sent. Edits that were never saved are lost with the session.",
                ErrorGuidance.SessionLostNothingRanStep);
        }

        if (source.ExpiresAt is { } expiresAt && now > expiresAt + Margin)
        {
            throw new CliException(
                ExitCodes.ServerRefused,
                ErrorCodes.ScreenSessionLost,
                $"{ErrorCodes.ScreenSessionLost}: the continuation expired at {FormatTime(expiresAt)}. A screen session closes after "
                + "2 minutes without a call, and edits that were never saved are lost with it. Nothing was sent.",
                ErrorGuidance.SessionLostNothingRanStep);
        }

        if (!string.Equals(source.ScreenId?.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                ExitCodes.ServerRefused,
                ErrorCodes.ScreenContinuationMismatch,
                $"{ErrorCodes.ScreenContinuationMismatch}: the continuation is for screen '{source.ScreenId}', and the batch targets "
                + $"'{target}'. Nothing was sent.",
                ErrorGuidance.ContinuationMismatchStep);
        }
    }

    /// <summary>Stores <paramref name="continuation"/> as the profile's open record and returns its deadline.</summary>
    public static DateTimeOffset Remember(ProfileState state, JsonObject continuation, string? sessionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(continuation);

        DateTimeOffset expiresAt = now + Lifetime;
        state.LastContinuation = new StoredContinuation
        {
            Continuation = continuation.DeepClone(),
            SessionId = sessionId,
            ExpiresAt = expiresAt,
        };
        return expiresAt;
    }

    public static string FormatTime(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTime(JsonNode? node) =>
        CommandText.String(node) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset time)
            ? time
            : null;
}
