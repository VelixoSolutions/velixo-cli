using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Velixo.Cli.IntegrationTests;

/// <summary>The ERPs the live tests run on, each through a connection of its own.</summary>
public enum Erp
{
    Acumatica,
    SageIntacct,
    BusinessCentral,
}

/// <summary>One run of the binary: its exit code, its stdout and stderr, and stdout read as JSON.</summary>
public sealed record CliRun(string[] Args, int ExitCode, string StdOut, string StdErr)
{
    public JsonNode? Json => StdOut.Length == 0 ? null : JsonNode.Parse(StdOut);

    /// <summary>The run's command line and output, for an assertion message.</summary>
    public override string ToString() =>
        $"velixo {string.Join(' ', Args)}\nexit {ExitCode}\nstdout:\n{StdOut}\nstderr:\n{StdErr}";
}

/// <summary>
/// Runs the real <c>velixo</c> binary against a live Velixo environment. The tests run only when
/// <c>VELIXO_IT_ENV</c> names an environment, such as <c>nx-dev</c>. They sign in one of two ways:
/// <list type="bullet">
/// <item>On a developer's machine, with a profile of their own configuration that is kept for the
/// tests (<c>VELIXO_IT_PROFILE</c>, default <c>it</c>). Sign it in once with
/// <c>velixo login --profile it --env nx-dev</c>.</item>
/// <item>In CI, with a refresh token in <c>VELIXO_IT_TOKEN</c>, seeded into a configuration folder of
/// the run's own (<c>VELIXO_IT_CONFIG_DIR</c>). The gateway rotates the token on every use, so the
/// workflow saves the rotated one for the next run.</item>
/// </list>
/// Each run is one process with stdin closed, so the CLI prints JSON and never prompts.
/// </summary>
public static class LiveCli
{
    public const string Intent = "Integration test of the Velixo CLI";

    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);

    private static readonly string[] _clearedVariables =
    [
        "CLAUDECODE", "CURSOR_AGENT", "CODEX_SANDBOX", "CODEX_THREAD_ID", "GEMINI_CLI", "CLINE_ACTIVE", "OPENCODE",
        "KIRO_SESSION_ID", "AI_AGENT", "VELIXO_PROFILE", "VELIXO_CONNECTION", "VELIXO_INTENT", "VELIXO_TOKEN",
        "VELIXO_CLIENT_ID", "VELIXO_CONFIG_DIR", "VELIXO_URL", "VELIXO_MCP_URL", "VELIXO_ENV",
    ];

    private static readonly Lazy<string> _configDirectory = new(() =>
        Variable("VELIXO_IT_CONFIG_DIR") ?? Directory.CreateTempSubdirectory("velixo-it-").FullName);

    /// <summary>Gets whether a live environment is configured. Every live test skips without one.</summary>
    public static bool Enabled => Environment is not null;

    public static string? Environment => Variable("VELIXO_IT_ENV");

    /// <summary>The Acumatica connection, and the one every test that is not about a particular ERP uses.</summary>
    public static string Connection => Variable("VELIXO_IT_CONNECTION") ?? "gab-dev";

    /// <summary>The Sage Intacct connection. Its data is from 2017 to 2019.</summary>
    public static string SageConnection => Variable("VELIXO_IT_SAGE_CONNECTION") ?? "skyline";

    /// <summary>The Business Central connection: CRONUS International Ltd., whose GL activity is mostly 2023.</summary>
    public static string BusinessCentralConnection => Variable("VELIXO_IT_BC_CONNECTION") ?? "bc-dev";

    /// <summary>The connection the tests use for <paramref name="erp"/>.</summary>
    public static string ConnectionOf(Erp erp) => erp switch
    {
        Erp.Acumatica => Connection,
        Erp.SageIntacct => SageConnection,
        Erp.BusinessCentral => BusinessCentralConnection,
        _ => throw new ArgumentOutOfRangeException(nameof(erp), erp, null),
    };

    /// <summary>A checked-in file under <c>Fixtures/&lt;erp&gt;/</c>, such as a query or a batch, copied beside the tests.</summary>
    public static string Fixture(Erp erp, string name) => Fixture(Path.Combine(FolderOf(erp), name));

    /// <summary>A checked-in file under <c>Fixtures/</c>, by its path inside that folder.</summary>
    public static string Fixture(string path)
    {
        var full = Path.Combine(AppContext.BaseDirectory, "Fixtures", path);
        return File.Exists(full) ? full : throw new FileNotFoundException($"No fixture {path}; is it under Fixtures/ in the test project?", full);
    }

    /// <summary>The fixture folder of <paramref name="erp"/>: <c>acumatica</c>, <c>sage-intacct</c> or <c>business-central</c>.</summary>
    public static string FolderOf(Erp erp) => erp switch
    {
        Erp.Acumatica => "acumatica",
        Erp.SageIntacct => "sage-intacct",
        Erp.BusinessCentral => "business-central",
        _ => throw new ArgumentOutOfRangeException(nameof(erp), erp, null),
    };

    /// <summary>Gets whether the run signs in with a token (CI) rather than a profile of the developer's.</summary>
    public static bool UsesToken => Variable("VELIXO_IT_TOKEN") is not null;

    public static string Binary => Variable("VELIXO_IT_BINARY") ?? DefaultBinary();

    /// <summary>
    /// Runs <c>velixo</c> signed in to the live environment, with <c>--output json</c> unless the arguments
    /// name another output, and with <c>--env</c> unless they name a server with <c>--url</c>.
    /// </summary>
    public static Task<CliRun> RunAsync(params string[] args) =>
        RunAsync(signedIn: true, args, configDirectory: null);

    /// <summary>Runs <c>velixo</c> signed in, as <see cref="RunAsync(string[])"/> does, with <paramref name="stdin"/> on its standard input.</summary>
    public static Task<CliRun> RunWithInputAsync(string stdin, params string[] args) =>
        RunAsync(signedIn: true, args, configDirectory: null, stdin);

    /// <summary>Runs <c>velixo</c> with an empty configuration of its own and no token: a machine that never signed in.</summary>
    public static Task<CliRun> RunSignedOutAsync(params string[] args) =>
        RunAsync(signedIn: false, args, NewConfigDirectory());

    /// <summary>Runs <c>velixo</c> without a sign-in in <paramref name="configDirectory"/>, so several runs share one throwaway configuration.</summary>
    public static Task<CliRun> RunInAsync(string configDirectory, params string[] args) =>
        RunAsync(signedIn: false, args, configDirectory);

    /// <summary>A configuration folder of its own, for the commands that must not touch the developer's.</summary>
    public static string NewConfigDirectory() => Directory.CreateTempSubdirectory("velixo-it-config-").FullName;

    /// <summary>Gets the profile the signed-in runs use: the environment's name in CI, the developer's test profile otherwise.</summary>
    public static string Profile => UsesToken ? Environment! : Variable("VELIXO_IT_PROFILE") ?? "it";

    /// <summary>A file in a folder the test run owns, for plans and batches between commands.</summary>
    public static string TempFile(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "velixo-it-files");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }

    private static async Task<CliRun> RunAsync(bool signedIn, string[] args, string? configDirectory, string? stdin = null)
    {
        string[] server = args.Contains("--url") ? [] : ["--env", Environment!];
        string[] profile = UsesToken || !signedIn ? [] : ["--profile", Profile];
        string[] output = args.Contains("--output") ? [] : ["--output", "json"];
        string[] all = [.. args, .. profile, .. server, .. output, "--non-interactive"];

        var start = new ProcessStartInfo(Binary)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in all)
        {
            start.ArgumentList.Add(arg);
        }

        foreach (var name in _clearedVariables)
        {
            start.Environment.Remove(name);
        }

        start.Environment["NO_COLOR"] = "1";
        start.Environment["VELIXO_INTENT"] = Intent;
        if (!signedIn)
        {
            start.Environment["VELIXO_CONFIG_DIR"] = configDirectory ?? NewConfigDirectory();
        }
        else if (UsesToken)
        {
            start.Environment["VELIXO_CONFIG_DIR"] = _configDirectory.Value;
            start.Environment["VELIXO_TOKEN"] = Variable("VELIXO_IT_TOKEN");
            start.Environment["VELIXO_CLIENT_ID"] = Variable("VELIXO_IT_CLIENT_ID") ?? "velixo-cli";
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {Binary}.");
        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }

        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(_timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"velixo {string.Join(' ', all)} ran longer than {_timeout.TotalMinutes} minutes.");
        }

        return new CliRun(all, process.ExitCode, await stdout, await stderr);
    }

    /// <summary>The CLI built beside these tests: <c>src/Velixo.Cli/bin/&lt;configuration&gt;/net10.0/velixo</c>.</summary>
    private static string DefaultBinary()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Velixo.Cli.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not find the repository root; set VELIXO_IT_BINARY to the velixo binary.");
        }

        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? "Debug"
            : "Release";
        var name = OperatingSystem.IsWindows() ? "velixo.exe" : "velixo";
        return Path.Combine(directory.FullName, "src", "Velixo.Cli", "bin", configuration, "net10.0", name);
    }

    private static string? Variable(string name) =>
        System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
