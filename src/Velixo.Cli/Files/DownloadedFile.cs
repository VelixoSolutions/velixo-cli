using System.Text.Json.Nodes;

namespace Velixo.Cli.Files;

/// <summary>A file written to disk from a download link.</summary>
/// <param name="FileName">The name the server gave the file, without any directory part.</param>
/// <param name="Path">The full path the file was written to.</param>
/// <param name="Bytes">The number of bytes written.</param>
/// <param name="ContentType">The media type the server named, or null.</param>
public sealed record DownloadedFile(string FileName, string Path, long Bytes, string? ContentType)
{
    public JsonObject ToJson() => new()
    {
        ["fileName"] = FileName,
        ["path"] = Path,
        ["bytes"] = Bytes,
        ["contentType"] = ContentType,
    };
}
