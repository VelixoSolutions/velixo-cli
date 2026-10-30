using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Velixo.Cli.Config;

/// <summary>
/// Reads and writes the CLI's JSON files. A write goes to a temporary file that is then renamed
/// over the target, so a reader in a parallel process never sees half a file.
/// </summary>
internal static class JsonFile
{
    /// <summary>Owner read and write only, for files that hold tokens.</summary>
    public const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode OwnerOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <exception cref="JsonException">The file exists but does not hold a valid document.</exception>
    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            // Another process may replace the file while this one reads it; on Windows the replace fails unless
            // the reader shares delete access.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return stream.Length == 0 ? null : JsonSerializer.Deserialize(stream, typeInfo);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public static void Write<T>(string path, T value, JsonTypeInfo<T> typeInfo, bool ownerOnly = false)
    {
        var directory = Path.GetDirectoryName(path)!;
        CreateDirectory(directory);

        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (ownerOnly && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }

        try
        {
            using (var stream = new FileStream(temporary, options))
            {
                var json = JsonSerializer.Serialize(value, typeInfo);
                stream.Write(Encoding.UTF8.GetBytes(json));
                stream.Write("\n"u8);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static void CreateDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, OwnerOnlyDirectory);
        }
    }
}
