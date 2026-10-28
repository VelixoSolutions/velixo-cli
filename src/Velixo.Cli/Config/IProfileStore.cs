namespace Velixo.Cli.Config;

/// <summary>Reads and writes <c>config.json</c> and the per-profile state files.</summary>
public interface IProfileStore
{
    ConfigPaths Paths { get; }

    CliConfig LoadConfig();

    void SaveConfig(CliConfig config);

    /// <summary>Returns the profile's state, or an empty one when it has none or the file is unreadable.</summary>
    ProfileState LoadState(string profile);

    void SaveState(string profile, ProfileState state);

    void DeleteState(string profile);
}
