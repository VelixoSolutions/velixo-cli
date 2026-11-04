using Velixo.Cli.Auth;
using Velixo.Cli.Config;

namespace Velixo.Cli.Tests.Fakes;

/// <summary>A <see cref="CliServices"/> over a temporary config directory, a captured console and a scripted HTTP handler.</summary>
internal sealed class TestServices : IDisposable
{
    private readonly TempDirectory _directory = new();

    /// <param name="browser">Builds the browser from the HTTP fake, so it can read what the CLI sent; no browser opens without it.</param>
    /// <param name="outputRedirected">Whether stdout alone is redirected; it follows <paramref name="redirected"/> when null.</param>
    public TestServices(
        Dictionary<string, string>? variables = null,
        bool redirected = true,
        string input = "",
        Func<FakeHttpHandler, IBrowserOpener>? browser = null,
        bool? outputRedirected = null)
    {
        Console = new TestConsole(redirected, input, outputRedirected);
        Environment = new TestEnvironment(_directory.Path, variables);
        Environment.Variables[EnvironmentVariables.ConfigDirectory] = System.IO.Path.Combine(_directory.Path, "config");
        Paths = ConfigPaths.Resolve(Environment);
        Http = new FakeHttpHandler();
        Services = new CliServices(
            Console,
            Environment,
            TimeProvider.System,
            new ProfileStore(Paths),
            new TokenStore(Paths),
            new HttpClient(Http),
            browser?.Invoke(Http) ?? new NoBrowser());
    }

    public TestConsole Console { get; }

    public TestEnvironment Environment { get; }

    public ConfigPaths Paths { get; }

    public FakeHttpHandler Http { get; }

    public CliServices Services { get; }

    public string Directory => _directory.Path;

    public Task<int> RunAsync(params string[] args) => Program.RunAsync(args, Services);

    public void Dispose()
    {
        Services.Http.Dispose();
        _directory.Dispose();
    }

    private sealed class NoBrowser : IBrowserOpener
    {
        public bool TryOpen(Uri url) => false;
    }
}
