using Velixo.Cli.Output;

namespace Velixo.Cli.Screen;

/// <summary>One <c>--attach PATH[:COMMENT]</c>: the local file, the name it is filed under, and the note stored with it.</summary>
internal sealed record ScreenAttachmentSpec(string Path, string FileName, long SizeBytes, string? Comment)
{
    /// <summary>
    /// Reads <c>PATH</c> or <c>PATH:COMMENT</c>, resolving a relative path against
    /// <paramref name="currentDirectory"/>. The value splits on its last colon, except when that colon
    /// belongs to the path: a drive letter (<c>C:\x.pdf</c>), a path separator right after it, or a file
    /// whose own name holds the colon.
    /// </summary>
    /// <exception cref="CliException">Exit 2 when the file does not exist.</exception>
    public static ScreenAttachmentSpec Parse(string value, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(value);

        var path = value;
        string? comment = null;

        var colon = value.LastIndexOf(':');
        if (colon > 0 && !File.Exists(System.IO.Path.GetFullPath(value, currentDirectory)))
        {
            var before = value[..colon];
            var after = value[(colon + 1)..];
            var isDrive = before.Length == 1 && char.IsAsciiLetter(before[0]);
            var isPath = after.Length > 0 && after[0] is '/' or '\\';
            if (!isDrive && !isPath)
            {
                path = before;
                comment = after.Length == 0 ? null : after;
            }
        }

        var fullPath = System.IO.Path.GetFullPath(path, currentDirectory);
        if (!File.Exists(fullPath))
        {
            throw CliException.Usage(
                $"The file to attach, '{path}', does not exist.",
                "Pass the path of a file the user gave you, optionally followed by :COMMENT.");
        }

        return new ScreenAttachmentSpec(fullPath, System.IO.Path.GetFileName(fullPath), new FileInfo(fullPath).Length, comment);
    }
}
