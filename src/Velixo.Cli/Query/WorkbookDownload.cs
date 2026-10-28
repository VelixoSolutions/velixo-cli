using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Files;
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
    public static async Task SaveAsync(FileTransfer transfer, JsonObject document, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);

        (JsonObject holder, Uri link) = FindLink(document["result"]?["data"])
            ?? throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Protocol,
                "The workbook was created, but the result holds no link to download it from.",
                "Open the link in the printed result instead.");

        var intoDirectory = Directory.Exists(path);
        DownloadedFile saved = await transfer
            .DownloadAsync(link.AbsoluteUri, intoDirectory ? path : null, intoDirectory ? null : path, CommandText.String(holder["fileName"]), cancellationToken)
            .ConfigureAwait(false);
        holder["path"] = saved.Path;
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
}
