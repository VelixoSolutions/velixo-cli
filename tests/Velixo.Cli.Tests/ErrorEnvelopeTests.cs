using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class ErrorEnvelopeTests
{
    [Fact]
    public void Json_mode_prints_exactly_the_envelope_on_stdout()
    {
        var console = new TestConsole();
        var output = new OutputWriter(console, new OutputSettings { Format = OutputFormat.Json });

        output.WriteError(new CliException(ExitCodes.ServerRefused, "SCREEN_SESSION_LOST", "SCREEN_SESSION_LOST: gone.", "Read the record.") { TaskId = "t_8f3a" });

        JsonObject envelope = JsonNode.Parse(console.StdOut)!.AsObject();
        Assert.Equal(["error"], envelope.Select(property => property.Key));
        JsonObject error = envelope["error"]!.AsObject();
        Assert.Equal("SCREEN_SESSION_LOST", (string?)error["code"]);
        Assert.Equal(5, (int?)error["exitCode"]);
        Assert.Equal("SCREEN_SESSION_LOST: gone.", (string?)error["message"]);
        Assert.Equal("t_8f3a", (string?)error["taskId"]);
        Assert.Equal("Read the record.", (string?)error["nextStep"]);
        Assert.False(error.ContainsKey("detail"));
        Assert.Empty(console.StdErr);
    }

    [Fact]
    public void Text_mode_prints_the_error_on_stderr_only()
    {
        var console = new TestConsole(redirected: false);
        var output = new OutputWriter(console, new OutputSettings { Format = OutputFormat.Table });

        output.WriteError(CliException.NotSignedIn("nx-dev"));

        Assert.Empty(console.StdOut);
        Assert.Contains("NOT_SIGNED_IN", console.StdErr, StringComparison.Ordinal);
        Assert.Contains("velixo login --profile nx-dev", console.StdErr, StringComparison.Ordinal);
    }
}
