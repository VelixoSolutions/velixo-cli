using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// Refuses a write the CLI already ran in the last 10 minutes. Re-running a batch writes again, so an
/// agent that retries after an ambiguous failure has to read the record first, or say <c>--force</c>.
/// </summary>
internal static class ScreenDuplicateGuard
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Returns the SHA-256 of <paramref name="call"/> in a canonical form: object keys sorted, so the
    /// order in which <c>--params</c> and <c>--param</c> named the variables does not change it.
    /// </summary>
    public static string Fingerprint(JsonObject call)
    {
        ArgumentNullException.ThrowIfNull(call);

        var canonical = new StringBuilder();
        WriteCanonical(call, canonical);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <exception cref="CliException">Exit 2 with <c>DUPLICATE_RUN</c> when the same write ran within the window and <paramref name="force"/> is false.</exception>
    public static void Check(ProfileState state, string fingerprint, DateTimeOffset now, bool force)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (force)
        {
            return;
        }

        RecentWrite? earlier = state.RecentWrites
            .Where(write => write.Fingerprint == fingerprint && now - write.ExecutedAt < Window)
            .OrderByDescending(write => write.ExecutedAt)
            .FirstOrDefault();

        if (earlier is not null)
        {
            throw new CliException(
                ExitCodes.Usage,
                ErrorCodes.DuplicateRun,
                $"The same batch with the same values already ran at {ScreenContinuations.FormatTime(earlier.ExecutedAt)}. "
                + "Running it again writes again: a second document, or the same change twice.",
                "Read the record first to see what the earlier run wrote. Pass --force only when the user wants it written again.");
        }
    }

    /// <summary>Records a write that ran, and drops the ones older than the window.</summary>
    public static void Record(ProfileState state, string fingerprint, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.RecentWrites.RemoveAll(write => now - write.ExecutedAt >= Window);
        state.RecentWrites.Add(new RecentWrite { Fingerprint = fingerprint, ExecutedAt = now });
    }

    private static void WriteCanonical(JsonNode? node, StringBuilder text)
    {
        switch (node)
        {
            case JsonObject obj:
                text.Append('{');
                var first = true;
                foreach ((var key, JsonNode? value) in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        text.Append(',');
                    }

                    first = false;
                    text.Append(JsonValue.Create(key).ToJsonString()).Append(':');
                    WriteCanonical(value, text);
                }

                text.Append('}');
                break;

            case JsonArray array:
                text.Append('[');
                for (var index = 0; index < array.Count; index++)
                {
                    if (index > 0)
                    {
                        text.Append(',');
                    }

                    WriteCanonical(array[index], text);
                }

                text.Append(']');
                break;

            case null:
                text.Append("null");
                break;

            default:
                text.Append(node.ToJsonString());
                break;
        }
    }
}
