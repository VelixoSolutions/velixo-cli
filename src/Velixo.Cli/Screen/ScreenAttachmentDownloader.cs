using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// Saves every <c>result.attachments[]</c> entry that carries a <c>downloadUrl</c> into one directory,
/// under its <c>fileName</c>, and adds the saved <c>path</c> to the entry.
/// </summary>
internal static class ScreenAttachmentDownloader
{
    public static async Task DownloadAsync(CliServices services, JsonArray attachments, string directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(directory);

        Directory.CreateDirectory(Path.GetFullPath(directory, services.Environment.CurrentDirectory));
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < attachments.Count; index++)
        {
            if (attachments[index] is not JsonObject entry
                || CommandText.String(entry["downloadUrl"]) is not { } url
                || !Uri.TryCreate(url, UriKind.Absolute, out Uri? downloadUri))
            {
                continue;
            }

            var fileName = UniqueName(SafeName(CommandText.String(entry["fileName"]), index), taken);
            var path = Path.Combine(directory, fileName);
            services.Output.Progress($"Downloading {fileName}");

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
            using HttpResponseMessage response = await HttpCalls
                .SendAsync(services.Http, request, cancellationToken, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new CliException(
                    ExitCodes.Failure,
                    ErrorCodes.Http,
                    $"The batch ran, but the download of '{fileName}' failed with HTTP {(int)response.StatusCode}: "
                    + await HttpCalls.ReadSnippetAsync(response, cancellationToken).ConfigureAwait(false),
                    "Do not run the batch again. Run a read-only batch that delivers the file again, or open the record in the ERP.");
            }

            FileStream file = File.Create(Path.GetFullPath(path, services.Environment.CurrentDirectory));
            await using (file.ConfigureAwait(false))
            {
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            entry["path"] = path;
        }
    }

    /// <summary>Returns the file name alone, so a name the server sends can never write outside the directory.</summary>
    private static string SafeName(string? fileName, int index)
    {
        var name = Path.GetFileName(fileName?.Replace('\\', '/') ?? string.Empty);
        return name is { Length: > 0 } and not ("." or "..") ? name : $"attachment-{index + 1}";
    }

    private static string UniqueName(string fileName, HashSet<string> taken)
    {
        var candidate = fileName;
        for (var copy = 2; !taken.Add(candidate); copy++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(fileName)} ({copy}){Path.GetExtension(fileName)}";
        }

        return candidate;
    }
}
