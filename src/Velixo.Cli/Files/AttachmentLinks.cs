using System.Text.Json.Nodes;
using Velixo.Cli.Commands;

namespace Velixo.Cli.Files;

/// <summary>Finds the download links in a saved command output.</summary>
internal static class AttachmentLinks
{
    /// <summary>
    /// Returns the <c>downloadUrl</c> of every entry of every <c>attachments</c> array in
    /// <paramref name="document"/>, wherever it sits, in document order and without repeats. A screen
    /// run puts them under <c>result.attachments</c> and a batch of runs nests them deeper, so the
    /// search does not depend on the depth.
    /// </summary>
    public static IReadOnlyList<string> Find(JsonNode? document)
    {
        var links = new List<string>();
        Collect(document, links);
        return links.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void Collect(JsonNode? node, List<string> links)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach ((var name, JsonNode? value) in properties)
                {
                    if (name == "attachments" && value is JsonArray attachments)
                    {
                        links.AddRange(attachments
                            .Select(attachment => attachment is JsonObject entry ? CommandText.String(entry["downloadUrl"]) : null)
                            .OfType<string>()
                            .Where(link => link.Length > 0));
                    }

                    Collect(value, links);
                }

                break;

            case JsonArray items:
                foreach (JsonNode? item in items)
                {
                    Collect(item, links);
                }

                break;
        }
    }
}
