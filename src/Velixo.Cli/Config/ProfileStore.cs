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
                "CONFIG_INVALID",
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

    public void DeleteState(string profile)
    {
        var path = Paths.StateFile(profile);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
