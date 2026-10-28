using System.CommandLine;
using System.Net;
using Velixo.Cli.Auth;
using Velixo.Cli.Commands;
using Velixo.Cli.Config;
using Velixo.Cli.Mcp;
using Velixo.Cli.Output;

namespace Velixo.Cli;

/// <summary>
/// The one object every command is built with. It holds the process's infrastructure from the start,
/// and the settings, the output writer and the MCP client once <see cref="Bind(ParseResult)"/> has
/// read the command line. Tests replace any part through the constructor and the factories.
/// </summary>
public sealed class CliServices
{
    private ResolvedSettings? _settings;
    private OutputWriter? _output;
    private IAccessTokenProvider? _accessTokens;
    private IMcpClient? _mcp;
    private long _boundAt;

    public CliServices(
        ICliConsole console,
        IEnvironmentReader environment,
        TimeProvider clock,
        IProfileStore profiles,
        ITokenStore tokens,
        HttpClient http,
        IBrowserOpener browser)
    {
        Console = console;
        Environment = environment;
        Clock = clock;
        Profiles = profiles;
        Tokens = tokens;
        Http = http;
        Browser = browser;
    }

    public ICliConsole Console { get; }

    public IEnvironmentReader Environment { get; }

    public TimeProvider Clock { get; }

    public IProfileStore Profiles { get; }

    public ITokenStore Tokens { get; }

    /// <summary>Gets the one HTTP client of the process. It honours HTTPS_PROXY and the other standard proxy variables.</summary>
    public HttpClient Http { get; }

    public IBrowserOpener Browser { get; }

    public GlobalOptions Options { get; } = new();

    /// <summary>Gets or sets what builds the MCP client once settings are known; tests put a fake here.</summary>
    public Func<CliServices, IMcpClient> McpClientFactory { get; set; } = CreateMcpClient;

    /// <summary>Gets or sets what builds the access token provider once settings are known.</summary>
    public Func<CliServices, IAccessTokenProvider> AccessTokenFactory { get; set; } = CreateAccessTokenProvider;

    public ResolvedSettings Settings => _settings ?? throw Unbound();

    public OutputWriter Output => _output ?? throw Unbound();

    public IAccessTokenProvider AccessTokens => _accessTokens ??= AccessTokenFactory(this);

    public IMcpClient Mcp => _mcp ??= McpClientFactory(this);

    /// <summary>Gets a follower for tasks started through <see cref="Mcp"/>, printing progress through <see cref="Output"/>.</summary>
    public TaskFollower Tasks => new(Mcp, Output, Clock);

    public bool IsBound => _settings is not null;

    public static CliServices CreateDefault()
    {
        var environment = new ProcessEnvironmentReader();
        ConfigPaths paths = ConfigPaths.Resolve(environment);
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"velixo-cli/{CliInfo.Version}");

        return new CliServices(
            new SystemConsole(),
            environment,
            TimeProvider.System,
            new ProfileStore(paths),
            new TokenStore(paths),
            http,
            new BrowserOpener());
    }

    /// <summary>Resolves the settings from <paramref name="parse"/> and the environment, and builds the output writer.</summary>
    /// <exception cref="CliException">A setting has a value the CLI cannot use, such as an unknown environment.</exception>
    public void Bind(ParseResult parse)
    {
        ArgumentNullException.ThrowIfNull(parse);

        var sources = new SettingsSources
        {
            Flags = Options.Read(parse),
            GetVariable = Environment.GetVariable,
            Project = ProjectConfigLocator.Find(Environment.CurrentDirectory),
            Config = Profiles.LoadConfig(),
            IsOutputRedirected = Console.IsOutputRedirected,
            IsErrorRedirected = Console.IsErrorRedirected,
            IsInputRedirected = Console.IsInputRedirected,
        };

        Bind(SettingsResolver.Resolve(sources));
    }

    public void Bind(ResolvedSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _boundAt = Clock.GetTimestamp();
        _output = new OutputWriter(Console, settings.Output);
        _accessTokens = null;
        _mcp = null;
    }

    /// <summary>
    /// Returns how the command's next task call waits: not at all with <c>--no-wait</c>, and for what is
    /// left of <c>--timeout</c>. The timeout is one budget for the whole command, measured from when it
    /// started, however many calls the command makes.
    /// </summary>
    /// <exception cref="CliException">Exit 124 when the budget ran out before this call.</exception>
    public TaskFollowOptions FollowOptions()
    {
        if (Settings.Timeout is not { } timeout)
        {
            return new TaskFollowOptions { NoWait = Settings.NoWait };
        }

        TimeSpan left = timeout - Clock.GetElapsedTime(_boundAt);
        return left > TimeSpan.Zero
            ? new TaskFollowOptions { NoWait = Settings.NoWait, Timeout = left }
            : throw new CliException(
                ExitCodes.Timeout,
                ErrorCodes.Timeout,
                $"The --timeout of {DurationText.Format(timeout)} ran out before the command's next call; what it had read so far is not printed.",
                "Run the command again with a longer --timeout.");
    }

    /// <summary>
    /// Binds an output writer when the settings could not be resolved, so the error can still be
    /// reported: JSON when stdout is not a terminal, text otherwise.
    /// </summary>
    public OutputWriter BindFallbackOutput()
    {
        var json = Console.IsOutputRedirected || AgentDetection.Detect(Environment.GetVariable) is not null;
        _output = new OutputWriter(Console, new OutputSettings { Format = json ? OutputFormat.Json : OutputFormat.Table });
        return _output;
    }

    /// <summary>
    /// The intent for a working tool, from <c>--intent</c> or <c>VELIXO_INTENT</c>. Call it before any
    /// network call, so a missing intent costs nothing.
    /// </summary>
    /// <exception cref="CliException">Exit 2 with an example when there is no intent.</exception>
    public string RequireIntent() =>
        Settings.Intent ?? throw new CliException(
            ExitCodes.Usage,
            ErrorCodes.IntentRequired,
            "This command calls a tool that records why it was called, and no --intent was given.",
            ToolResultUnwrapper.IntentExample);

    private static IMcpClient CreateMcpClient(CliServices services) =>
        new StreamableHttpMcpClient(
            services.Http,
            services.Settings.McpUrl,
            services.AccessTokens,
            services.Profiles,
            services.Settings.ProfileName,
            services.Settings.Agent,
            CliInfo.Version);

    private static IAccessTokenProvider CreateAccessTokenProvider(CliServices services) =>
        new GatewayTokenProvider(
            services.Tokens,
            new OAuthTokenClient(services.Http),
            new OAuthDiscovery(services.Http),
            services.Settings,
            services.Clock);

    private static InvalidOperationException Unbound() =>
        new("CliServices is not bound to a command line yet; Program binds it before invoking a command.");
}
