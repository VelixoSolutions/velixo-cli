using System.Text.Json.Nodes;

namespace Velixo.Cli.Files;

/// <summary>A file the server holds under a <see cref="FileToken"/>, ready for <c>command run --attach</c> or <c>import run</c> to spend.</summary>
/// <param name="FileName">The file's name without its directory, which is the name the token is spent under.</param>
/// <param name="SizeBytes">The file's size in bytes.</param>
/// <param name="Sha256">The lower-case hex SHA-256 of the file's bytes, which the server checks against what arrived.</param>
/// <param name="FileToken">The token that stands in for the bytes in a later call.</param>
/// <param name="ExpiresAt">When the token stops working, as the server wrote it, or null when it did not say.</param>
/// <param name="Comment">The note to store beside the file, or null.</param>
public sealed record StagedFile(string FileName, long SizeBytes, string Sha256, string FileToken, string? ExpiresAt, string? Comment = null)
{
    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["fileName"] = FileName,
            ["sizeBytes"] = SizeBytes,
            ["sha256"] = Sha256,
            ["fileToken"] = FileToken,
            ["expiresAt"] = ExpiresAt,
        };

        if (Comment is not null)
        {
            json["comment"] = Comment;
        }

        return json;
    }
}

/// <summary>What the server hands out for a file to stage: the token that stands in for it, and the URL that takes its bytes once.</summary>
public sealed record UploadLink(string FileToken, string UploadUrl, string? ExpiresAt);
