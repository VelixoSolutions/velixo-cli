using Velixo.Cli.Config;
using Velixo.Cli.Output;

namespace Velixo.Cli.Tests;

public sealed class SettingsResolverTests
{
    private static readonly Uri _devUrl = new("https://nx-dev.velixo.com/mcp");

    [Fact]
    public void Flag_beats_variable_project_and_profile()
    {
        ResolvedSettings settings = Resolve(
            new CommandLineValues { Connection = "from-flag", Url = "https://flag.example.com/mcp" },
            AllVariables(),
            AllProject(),
            AllProfiles());

        Assert.Equal("from-flag", settings.Connection);
        Assert.Equal(new Uri("https://flag.example.com/mcp"), settings.McpUrl);
    }

    [Fact]
    public void VELIXO_URL_names_the_server_and_wins_over_its_first_name()
    {
        var variables = new Dictionary<string, string>
        {
            [EnvironmentVariables.Url] = "https://url.example.com/mcp",
            [EnvironmentVariables.McpUrl] = "https://old-name.example.com/mcp",
        };

        ResolvedSettings settings = Resolve(new CommandLineValues(), variables, null, new CliConfig());

        Assert.Equal(new Uri("https://url.example.com/mcp"), settings.McpUrl);
    }

    [Theory]
    [InlineData("https://feature-x.velixo.com", "https://feature-x.velixo.com/mcp")]
    [InlineData("https://feature-x.velixo.com/", "https://feature-x.velixo.com/mcp")]
    [InlineData("https://feature-x.velixo.com/mcp", "https://feature-x.velixo.com/mcp")]
    [InlineData("http://localhost:5100", "http://localhost:5100/mcp")]
    [InlineData("https://example.com/gateway/mcp", "https://example.com/gateway/mcp")]
    public void A_server_address_without_a_path_gets_the_endpoint_path(string given, string expected)
    {
        ResolvedSettings settings = Resolve(new CommandLineValues { Url = given }, [], null, new CliConfig());

        Assert.Equal(new Uri(expected), settings.McpUrl);
    }

    [Fact]
    public void A_bare_address_of_a_built_in_environment_takes_its_profile()
    {
        ResolvedSettings settings = Resolve(new CommandLineValues { Url = "https://nx-dev.velixo.com" }, [], null, new CliConfig());

        Assert.Equal("nx-dev", settings.ProfileName);
        Assert.Equal("nx-dev", settings.EnvironmentName);
    }

    [Fact]
    public void Variable_beats_project_and_profile()
    {
        ResolvedSettings settings = Resolve(new CommandLineValues(), AllVariables(), AllProject(), AllProfiles());

        Assert.Equal("from-variable", settings.Connection);
        Assert.Equal(new Uri("https://variable.example.com/mcp"), settings.McpUrl);
        Assert.Equal("variable-profile", settings.ProfileName);
    }

    [Fact]
    public void Project_beats_profile()
    {
        ResolvedSettings settings = Resolve(new CommandLineValues(), [], AllProject(), AllProfiles());

        Assert.Equal("from-project", settings.Connection);
        Assert.Equal(new Uri("https://project.example.com/mcp"), settings.McpUrl);
        Assert.Equal("project-profile", settings.ProfileName);
    }

    [Fact]
    public void Profile_beats_default()
    {
        var config = new CliConfig { DefaultProfile = "acme" };
        config.Profiles["acme"] = new ProfileConfig { Environment = Environments.NxDev, Connection = "from-profile" };

        ResolvedSettings settings = Resolve(new CommandLineValues(), [], project: null, config);

        Assert.Equal("acme", settings.ProfileName);
        Assert.Equal("from-profile", settings.Connection);
        Assert.Equal(_devUrl, settings.McpUrl);
        Assert.Equal(Environments.NxDev, settings.EnvironmentName);
    }

    [Fact]
    public void Default_is_prod_with_no_connection()
    {
        ResolvedSettings settings = Resolve(new CommandLineValues(), [], project: null, new CliConfig());

        Assert.Equal(Environments.Prod, settings.ProfileName);
        Assert.Equal(new Uri("https://app.velixo.com/mcp"), settings.McpUrl);
        Assert.Null(settings.Connection);
    }

    [Fact]
    public void Env_flag_names_the_profile_when_no_profile_is_given()
    {
        ResolvedSettings settings = Resolve(new CommandLineValues { Environment = Environments.NxDev }, [], project: null, new CliConfig { DefaultProfile = "prod" });

        Assert.Equal(Environments.NxDev, settings.ProfileName);
        Assert.Equal(_devUrl, settings.McpUrl);
    }

    [Fact]
    public void Unknown_environment_is_a_usage_error()
    {
        CliException error = Assert.Throws<CliException>(() =>
            Resolve(new CommandLineValues { Environment = "nowhere" }, [], project: null, new CliConfig()));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
    }

    [Theory]
    [InlineData(true, null, OutputFormat.Json)]
    [InlineData(false, null, OutputFormat.Table)]
    [InlineData(false, "1", OutputFormat.Json)]
    public void Output_defaults_to_json_for_a_pipe_or_an_agent(bool redirected, string? claudeCode, OutputFormat expected)
    {
        var variables = new Dictionary<string, string>();
        if (claudeCode is not null)
        {
            variables["CLAUDECODE"] = claudeCode;
        }

        ResolvedSettings settings = SettingsResolver.Resolve(new SettingsSources
        {
            Flags = new CommandLineValues(),
            GetVariable = variables.GetValueOrDefault,
            Config = new CliConfig(),
            IsOutputRedirected = redirected,
            IsInputRedirected = redirected,
        });

        Assert.Equal(expected, settings.Output.Format);
        Assert.False(settings.Output.IsFormatExplicit);
        Assert.Equal(claudeCode is null && !redirected, settings.IsInteractive);
    }

    [Fact]
    public void Output_flag_beats_variable_and_config()
    {
        ResolvedSettings settings = Resolve(
            new CommandLineValues { Output = "csv" },
            new Dictionary<string, string> { [EnvironmentVariables.Output] = "table" },
            project: null,
            new CliConfig { DefaultOutput = "json" });

        Assert.Equal(OutputFormat.Csv, settings.Output.Format);
        Assert.True(settings.Output.IsFormatExplicit);
    }

    private static ResolvedSettings Resolve(CommandLineValues flags, Dictionary<string, string> variables, ProjectConfig? project, CliConfig config) =>
        SettingsResolver.Resolve(new SettingsSources
        {
            Flags = flags,
            GetVariable = variables.GetValueOrDefault,
            Project = project,
            Config = config,
            IsOutputRedirected = true,
        });

    private static Dictionary<string, string> AllVariables() => new()
    {
        [EnvironmentVariables.Connection] = "from-variable",
        [EnvironmentVariables.McpUrl] = "https://variable.example.com/mcp",
        [EnvironmentVariables.Profile] = "variable-profile",
    };

    private static ProjectConfig AllProject() => new()
    {
        Connection = "from-project",
        Url = "https://project.example.com/mcp",
        Profile = "project-profile",
    };

    private static CliConfig AllProfiles()
    {
        var config = new CliConfig { DefaultProfile = "project-profile" };
        foreach (var name in new[] { "variable-profile", "project-profile" })
        {
            config.Profiles[name] = new ProfileConfig { Url = "https://profile.example.com/mcp", Connection = "from-profile" };
        }

        return config;
    }
}
