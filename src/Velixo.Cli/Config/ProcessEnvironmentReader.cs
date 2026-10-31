namespace Velixo.Cli.Config;

public sealed class ProcessEnvironmentReader : IEnvironmentReader
{
    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string CurrentDirectory => Environment.CurrentDirectory;

    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public bool IsWindows => OperatingSystem.IsWindows();
}
