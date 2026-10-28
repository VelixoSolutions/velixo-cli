using Velixo.Cli.Config;

namespace Velixo.Cli.Tests.Fakes;

internal sealed class TestEnvironment(string currentDirectory, Dictionary<string, string>? variables = null) : IEnvironmentReader
{
    public Dictionary<string, string> Variables { get; } = variables ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public string? GetVariable(string name) => Variables.GetValueOrDefault(name);

    public string CurrentDirectory { get; } = currentDirectory;

    public string HomeDirectory { get; } = currentDirectory;

    public bool IsWindows => false;
}
