using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Velixo.Cli.Commands;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>
/// Stages each <c>--attach</c> file the way the tool guidance prescribes: <c>get_upload_url</c> in
/// named mode with the name, size and SHA-256, then a PUT of the bytes to the returned link. The
/// result is the <c>attachments</c> argument of <c>execute_commands</c>.
/// </summary>
internal static class ScreenAttachmentStager
{
    public static async Task<JsonArray> StageAsync(CliServices services, IReadOnlyList<ScreenAttachmentSpec> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(files);

        var attachments = new JsonArray();
        foreach (ScreenAttachmentSpec file in files)
        {
            services.Output.Progress($"Uploading {file.FileName} ({file.SizeBytes:N0} bytes)");

            var arguments = new JsonObject
            {
                ["fileName"] = file.FileName,
                ["fileSizeBytes"] = file.SizeBytes,
                ["sha256"] = await HashAsync(file.Path, cancellationToken).ConfigureAwait(false),
            };

            UnwrappedToolResult link = ToolResultUnwrapper.Unwrap(
                await services.Mcp.CallToolAsync(McpToolNames.GetUploadUrl, arguments, cancellationToken).ConfigureAwait(false));

            if (CommandText.String(link.Data?["fileToken"]) is not { } fileToken
                || CommandText.String(link.Data?["uploadUrl"]) is not { } uploadUrl
                || !Uri.TryCreate(uploadUrl, UriKind.Absolute, out Uri? uploadUri))
            {
                throw new CliException(
                    ExitCodes.Failure,
                    ErrorCodes.Protocol,
                    $"get_upload_url answered without a fileToken and an uploadUrl for '{file.FileName}'.",
                    "Retry once; if it fails again, report it.");
            }

            await PutAsync(services, file, uploadUri, cancellationToken).ConfigureAwait(false);

            var attachment = new JsonObject { ["fileName"] = file.FileName, ["fileToken"] = fileToken };
            if (file.Comment is not null)
            {
                attachment["comment"] = file.Comment;
            }

            attachments.Add((JsonNode)attachment);
        }

        return attachments;
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        FileStream stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// PUTs the file's bytes. The link is a credential, so no message names it: a failure names the
    /// file and the server's answer only.
    /// </summary>
    private static async Task PutAsync(CliServices services, ScreenAttachmentSpec file, Uri uploadUri, CancellationToken cancellationToken)
    {
        FileStream stream = File.OpenRead(file.Path);
        await using (stream.ConfigureAwait(false))
        {
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentLength = file.SizeBytes;
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri) { Content = content };
            using HttpResponseMessage response = await HttpCalls.SendAsync(services.Http, request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var answer = await HttpCalls.ReadSnippetAsync(response, cancellationToken).ConfigureAwait(false);
            if (answer.StartsWith("ATTACHMENT_", StringComparison.Ordinal))
            {
                throw ToolResultUnwrapper.MapErrorText(answer);
            }

            throw new CliException(
                ExitCodes.Failure,
                ErrorCodes.Http,
                $"The upload of '{file.FileName}' failed with HTTP {(int)response.StatusCode}: {answer}",
                "Nothing was written to the ERP. Run the command again; it stages the file afresh.");
        }
    }
}
