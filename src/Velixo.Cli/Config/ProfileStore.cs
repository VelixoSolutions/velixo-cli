using System.Text.Json;
using Velixo.Cli.Output;
using Velixo.Cli.Serialization;

namespace Velixo.Cli.Config;

public sealed class ProfileStore(ConfigPaths paths) : IProfileStore
{
    public ConfigPaths Paths { get; } = paths;

    public CliConfig LoadConfig()
    {
        try
        {
            return JsonFile.Read(Paths.ConfigFile, CliJsonContext.Default.CliConfig) ?? new CliConfig();
        }
        catch (JsonException exception)
        {
            throw new CliException(
                ExitCodes.Usage,
                ErrorCodes.ConfigInvalid,
                $"{Paths.ConfigFile} is not valid JSON: {exception.Message}",
                "Fix or delete the file; 'velixo config path' shows where it is.");
        }
    }

    public void SaveConfig(CliConfig config) => JsonFile.Write(Paths.ConfigFile, config, CliJsonContext.Default.CliConfig);

    public ProfileState LoadState(string profile)
    {
        try
        {
            return JsonFile.Read(Paths.StateFile(profile), CliJsonContext.Default.ProfileState) ?? new ProfileState();
        }
        catch (JsonException)
        {
            return new ProfileState();
        }
    }

    public void SaveState(string profile, ProfileState state) =>
        JsonFile.Write(Paths.StateFile(profile), state, CliJsonContext.Default.ProfileState, ownerOnly: true);

    public StagedFilesFile LoadStagedFiles(string profile)
    {
        try
        {
            return JsonFile.Read(Paths.StagedFilesFile(profile), CliJsonContext.Default.StagedFilesFile) ?? new StagedFilesFile();
        }
        catch (JsonException)
        {
            return new StagedFilesFile();
        }
    }

    public void SaveStagedFiles(string profile, StagedFilesFile staged) =>
        JsonFile.Write(Paths.StagedFilesFile(profile), staged, CliJsonContext.Default.StagedFilesFile, ownerOnly: true);

    public LibraryFile LoadLibrary(string profile)
    {
        var path = Paths.LibraryFile(profile);
        try
        {
            return JsonFile.Read(path, CliJsonContext.Default.LibraryFile) ?? new LibraryFile();
        }
        catch (JsonException exception)
        {
            throw new CliException(
                ExitCodes.Usage,
                ErrorCodes.ConfigInvalid,
                $"{path} is not valid JSON: {exception.Message}",
                "Fix or delete the file; it holds only the plans and batches built on this profile.");
        }
    }

    public void SaveLibrary(string profile, LibraryFile library) =>
        JsonFile.Write(Paths.LibraryFile(profile), library, CliJsonContext.Default.LibraryFile, ownerOnly: true);

    public void DeleteState(string profile)
    {
        var path = Paths.StateFile(profile);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
