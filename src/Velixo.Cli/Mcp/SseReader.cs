using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Velixo.Cli.Mcp;

/// <summary>Reads the JSON-RPC messages out of a <c>text/event-stream</c> body, one per event.</summary>
public static class SseReader
{
    public static async IAsyncEnumerable<JsonNode> ReadMessagesAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null || line.Length == 0)
            {
                if (data.Length > 0 && TryParse(data.ToString()) is { } message)
                {
                    yield return message;
                }

                data.Clear();
                if (line is null)
                {
                    yield break;
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                var value = line.AsSpan(5);
                data.Append(value.Length > 0 && value[0] == ' ' ? value[1..] : value);
            }
        }
    }

    private static JsonNode? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
