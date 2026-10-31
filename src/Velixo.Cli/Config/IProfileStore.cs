namespace Velixo.Cli.Config;

/// <summary>Reads and writes <c>config.json</c> and the per-profile state files.</summary>
public interface IProfileStore
{
    ConfigPaths Paths { get; }

    CliConfig LoadConfig();

    /// <summary>
    /// Reads <c>config.json</c>, applies <paramref name="change"/> and writes it back under a lock, as
    /// <see cref="UpdateState"/> does, and returns the configuration as written.
    /// </summary>
    CliConfig UpdateConfig(Action<CliConfig> change);

    /// <summary>Returns the profile's state, or an empty one when it has none or the file is unreadable.</summary>
    ProfileState LoadState(string profile);

    /// <summary>
    /// Reads the profile's state, applies <paramref name="change"/> and writes it back, holding a lock so that
    /// another velixo process cannot write in between and lose this change or its own.
    /// </summary>
    void UpdateState(string profile, Action<ProfileState> change);

    void DeleteState(string profile);

    /// <summary>Returns the files the profile staged, or none when it has no such file or the file is unreadable.</summary>
    StagedFilesFile LoadStagedFiles(string profile);

    /// <summary>Changes the staged files under a lock, as <see cref="UpdateState"/> does.</summary>
    void UpdateStagedFiles(string profile, Action<StagedFilesFile> change);

    /// <summary>Returns the query plans and screen batches the profile built, or an empty library when it has none.</summary>
    LibraryFile LoadLibrary(string profile);

    /// <summary>Changes the library under a lock, as <see cref="UpdateState"/> does, and returns what the change returns.</summary>
    T UpdateLibrary<T>(string profile, Func<LibraryFile, T> change);
}
