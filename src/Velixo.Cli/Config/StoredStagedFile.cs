using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>
/// A file <c>velixo files stage</c> staged on this profile: the name its token was issued for, which a
/// later <c>--attach-token</c> or <c>--file-token</c> must send with it, since the server checks the two together.
/// </summary>
public sealed class StoredStagedFile
{
    [JsonPropertyName("fileToken")]
    public string FileToken { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets when the token stops working, as the server wrote it, or null when it did not say.</summary>
    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }
}

/// <summary>
/// <c>state/&lt;profile&gt;.staged.json</c>: the files <c>velixo files stage</c> staged on the profile, by
/// token. It has a file of its own, so staging never rewrites the continuation and the duplicate guard's
/// fingerprints that a run in parallel saves.
/// </summary>
public sealed class StagedFilesFile
{
    [JsonPropertyName("files")]
    public List<StoredStagedFile> Files { get; set; } = [];
}
