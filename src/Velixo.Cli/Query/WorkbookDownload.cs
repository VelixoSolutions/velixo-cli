using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Query;

/// <summary>
/// Saves the workbook <c>create_workbook_from_query</c> links to. The tool answers
/// <c>{ attachments: [{ fileName, downloadUrl, … }], note }</c>; the link is the first
/// <c>downloadUrl</c>, and a <c>workbookUrl</c> or <c>url</c> is accepted in case the shape changes.
/// </summary>
internal static class WorkbookDownload
{
    private static readonly string[] _linkKeys = ["downloadUrl", "workbookUrl", "url"];

    /// <summary>
    /// Downloads the linked file to <paramref name="path"/>, or into it under the file's own name when
    /// it is a directory, and adds <c>"path"</c> to the object that holds the link.
    /// </summary>
    public static async Task SaveAsync(HttpClient http, JsonObject document, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);

        (JsonObject holder, Uri link) = FindLink(document["result"]?["data"])
            ?? throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Protocol,
                "The workbook was created, but the result holds no link to download it from.",
                "Open the link in the printed result instead.");

        var target = Directory.Exists(path)
            ? Path.Combine(path, FileNameFor(holder, link))
            : path;

        using var request = new HttpRequestMessage(HttpMethod.Get, link);
        using HttpResponseMessage response = await HttpCalls.SendAsync(http, request, cancellationToken, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"Downloading the workbook answered HTTP {(int)response.StatusCode}: {await HttpCalls.ReadSnippetAsync(response, cancellationToken).ConfigureAwait(false)}",
                "A download link works for a limited time; create the workbook again.");
        }

        try
        {
            var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (stream.ConfigureAwait(false))
            {
                await response.Content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CliException.Usage($"Could not write the workbook to '{target}': {exception.Message}");
        }

        holder["path"] = target;
    }

    /// <summary>Finds the first object, depth first, with an absolute http or https link under one of the link keys.</summary>
    private static (JsonObject Holder, Uri Link)? FindLink(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in _linkKeys)
                {
                    if (obj[key] is JsonValue value
                        && value.TryGetValue(out string? text)
                        && Uri.TryCreate(text, UriKind.Absolute, out Uri? link)
                        && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp))
                    {
                        return (obj, link);
                    }
                }

                foreach ((_, JsonNode? child) in obj)
                {
                    if (FindLink(child) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            case JsonArray array:
                foreach (JsonNode? child in array)
                {
                    if (FindLink(child) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static string FileNameFor(JsonObject holder, Uri link)
    {
        var name = holder["fileName"] is JsonValue value && value.TryGetValue(out string? fileName) ? fileName : null;
        name = Path.GetFileName(string.IsNullOrWhiteSpace(name) ? link.Segments[^1] : name);
        return string.IsNullOrWhiteSpace(name) ? "workbook.xlsx" : name;
    }
}
