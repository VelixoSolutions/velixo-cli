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

    /// <summary>Returns the files the profile staged, or none when it has no such file or the file is unreadable.</summary>
    StagedFilesFile LoadStagedFiles(string profile);

    void SaveStagedFiles(string profile, StagedFilesFile staged);

    /// <summary>Returns the query plans and screen batches the profile built, or an empty library when it has none.</summary>
    LibraryFile LoadLibrary(string profile);

    void SaveLibrary(string profile, LibraryFile library);
}
