using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Files;

/// <summary>
/// Moves file bytes between the machine and the server, outside the MCP channel: staging a local file
/// under a token the server hands out, and saving what a download link serves. Every command that
/// stages or fetches a file goes through here.
/// </summary>
public sealed class FileTransfer(IMcpClient mcp, HttpClient http)
{
    private const string FallbackDownloadName = "download";

    /// <summary>
    /// Stages the file at <paramref name="path"/>: asks the server for an upload link in named mode,
    /// declaring the file's size and SHA-256, then streams the bytes to that link.
    /// </summary>
    /// <exception cref="CliException">
    /// Exit 2 when the file cannot be read, exit 5 with the server's code when it refuses the file's
    /// name or size, exit 5 <see cref="ErrorCodes.AttachmentUploadFailed"/> when the upload endpoint
    /// refuses the bytes with a 4xx, and exit 1 when it fails with a 5xx.
    /// </exception>
    public async Task<StagedFile> StageAsync(string path, string? comment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw CliException.Usage($"The file '{path}' does not exist.", "Check the path.");
        }

        var sha256 = await HashAsync(file, cancellationToken).ConfigureAwait(false);

        McpToolResult called = await mcp
            .CallToolAsync(
                McpToolNames.GetUploadUrl,
                new JsonObject { ["fileName"] = file.Name, ["fileSizeBytes"] = file.Length, ["sha256"] = sha256 },
                cancellationToken)
            .ConfigureAwait(false);
        JsonObject link = ToolResultUnwrapper.Unwrap(called).Data as JsonObject
            ?? throw ProtocolError("get_upload_url did not answer with an upload link.");

        var fileToken = CommandText.String(link["fileToken"]) ?? throw ProtocolError("get_upload_url answered without a fileToken.");
        var uploadUrl = CommandText.String(link["uploadUrl"]) ?? throw ProtocolError("get_upload_url answered without an uploadUrl.");
        var expiresAt = CommandText.String(link["expiresAt"]);

        await PutAsync(file, ParseHttpUri(uploadUrl, "upload link"), cancellationToken).ConfigureAwait(false);

        return new StagedFile(file.Name, file.Length, sha256, fileToken, expiresAt, comment);
    }

    /// <summary>
    /// Fetches a download link and writes the file it serves. With <paramref name="outPath"/> the file
    /// goes to that exact path and replaces what is there. With <paramref name="directory"/> it goes
    /// under its own name, and an existing file of that name is kept: the new one is saved as
    /// <c>name (2).ext</c>. Its own name is <paramref name="fileName"/> when the caller has one from the
    /// result that issued the link, else the <c>Content-Disposition</c> header's, else the URL's last segment.
    /// </summary>
    /// <exception cref="CliException">
    /// Exit 2 for a URL that is not http or https, exit 5 <see cref="ErrorCodes.AttachmentTokenInvalid"/>
    /// when the server answers with a 4xx, which is how an expired or spent link answers, and exit 1 for
    /// any other failure.
    /// </exception>
    public async Task<DownloadedFile> DownloadAsync(string url, string? directory, string? outPath, string? fileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if ((directory is null) == (outPath is null))
        {
            throw new ArgumentException("Give a directory or an output path, exactly one of them.", nameof(outPath));
        }

        Uri uri = ParseHttpUri(url, "download link");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using HttpResponseMessage response = await HttpCalls
            .SendAsync(http, request, cancellationToken, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await DownloadRefusedAsync(response, uri, cancellationToken).ConfigureAwait(false);
        }

        var name = SafeFileName(fileName)
            ?? SafeFileName(HeaderFileName(response.Content.Headers.ContentDisposition))
            ?? SafeFileName(UrlFileName(uri))
            ?? FallbackDownloadName;
        var target = outPath is not null
            ? Path.GetFullPath(outPath)
            : AvoidExisting(Path.GetFullPath(Path.Combine(directory!, name)));

        var bytes = await SaveAsync(response, target, cancellationToken).ConfigureAwait(false);
        return new DownloadedFile(name, target, bytes, response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<string> HashAsync(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = file.OpenRead();
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw CliException.Usage($"The file '{file.FullName}' cannot be read: {exception.Message}");
        }
    }

    private async Task PutAsync(FileInfo file, Uri uploadUri, CancellationToken cancellationToken)
    {
        await using FileStream stream = file.OpenRead();
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri) { Content = new StreamContent(stream) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using HttpResponseMessage response = await HttpCalls.SendAsync(http, request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        var reason = await HttpCalls.ReadSnippetAsync(response, cancellationToken).ConfigureAwait(false);
        if (ToolResultUnwrapper.IsCodedError(reason))
        {
            throw ToolResultUnwrapper.MapErrorText(reason);
        }

        throw status is >= 400 and < 500
            ? new CliException(
                ExitCodes.ServerRefused,
                ErrorCodes.AttachmentUploadFailed,
                $"{ErrorCodes.AttachmentUploadFailed}: the server refused the upload of '{file.Name}' with HTTP {status}. {reason}".TrimEnd(),
                "Stage the file again for a new token; an upload link accepts one file once and lasts about an hour.")
            : new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"The upload of '{file.Name}' failed with HTTP {status}. {reason}".TrimEnd(),
                "Retry once; nothing that needed the file has run yet.");
    }

    private static async Task<CliException> DownloadRefusedAsync(HttpResponseMessage response, Uri uri, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var reason = await HttpCalls.ReadSnippetAsync(response, cancellationToken).ConfigureAwait(false);
        var host = uri.GetLeftPart(UriPartial.Authority);

        return status is >= 400 and < 500
            ? new CliException(
                ExitCodes.ServerRefused,
                ErrorCodes.AttachmentTokenInvalid,
                $"{ErrorCodes.AttachmentTokenInvalid}: {host} refused the download link with HTTP {status}. {reason}".TrimEnd(),
                "Run the command that issued the link again for a new one; a link lasts about an hour.")
            : new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"{host} answered the download with HTTP {status}. {reason}".TrimEnd(),
                "Retry once; if it fails again, report it.");
    }

    private static async Task<long> SaveAsync(HttpResponseMessage response, string target, CancellationToken cancellationToken)
    {
        var temporary = target + ".part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (FileStream destination = File.Create(temporary))
            {
                await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, target, overwrite: true);
            return new FileInfo(target).Length;
        }
        catch (Exception exception) when (exception is HttpRequestException or HttpIOException)
        {
            File.Delete(temporary);
            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Network,
                $"The download stopped before the file was complete: {exception.Message}",
                "Retry once; the partial file was removed.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            File.Delete(temporary);
            throw CliException.Usage($"The file '{target}' cannot be written: {exception.Message}", "Check the path and its permissions.");
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    private static string AvoidExisting(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        var extension = Path.GetExtension(path);
        for (var attempt = 2; ; attempt++)
        {
            var candidate = $"{stem} ({attempt}){extension}";
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string? UrlFileName(Uri uri)
    {
        var segment = uri.Segments.LastOrDefault()?.Trim('/');
        return segment is { Length: > 0 } ? Uri.UnescapeDataString(segment) : null;
    }

    private static string? HeaderFileName(ContentDispositionHeaderValue? disposition) =>
        (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"');

    /// <summary>The last path segment of <paramref name="candidate"/> made safe to create on any platform, or null when nothing usable is left.</summary>
    private static string? SafeFileName(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var slashed = candidate.Replace('\\', '/');
        var name = slashed[(slashed.LastIndexOf('/') + 1)..];
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim();
        return name is "" or "." or ".." ? null : name;
    }

    private static Uri ParseHttpUri(string text, string what) =>
        Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw CliException.Usage($"The {what} is not an http or https URL.");

    private static CliException ProtocolError(string message) =>
        new(ExitCodes.Failure, ErrorCodes.Protocol, message, "Report this: the server answered in a shape the CLI does not know.");
}
