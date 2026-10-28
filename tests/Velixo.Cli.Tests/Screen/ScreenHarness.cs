using System.Text.Json.Nodes;
using Velixo.Cli.Auth;
using Velixo.Cli.Config;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests.Screen;

/// <summary>
/// Runs <c>velixo screen</c> commands against a scripted MCP client and HTTP handler, on a clock the
/// test moves, with a console that is a terminal or a pipe.
/// </summary>
internal sealed class ScreenHarness : IDisposable
{
    public const string Intent = "Load the Q4 order for Acme";

    public const string OrderBatch = """
        {
          "taskId": "t_b",
          "status": "done",
          "result": {
            "data": {
              "target": "SO301000",
              "commands": [
                { "type": "Clear" },
                { "type": "Set", "name": "Document.CustomerID", "value": "{{CustomerID}}" },
                { "type": "Set", "name": "Document.Description", "value": "{{Note}}" }
              ],
              "lineCommands": [
                { "type": "Set", "name": "Transactions.InventoryID", "value": "{{InventoryID}}" },
                { "type": "Set", "name": "Transactions.OrderQty", "value": "{{Qty}}" }
              ],
              "finishCommands": [
                { "type": "Action", "name": "Save" },
                { "type": "Get", "name": "Document.OrderNbr" }
              ],
              "variables": [
                { "name": "CustomerID", "type": "string", "description": "The customer" },
                { "name": "Note", "type": "string", "description": "The description" }
              ],
              "keyNumbering": { "field": "OrderNbr", "autonumbered": true }
            }
          }
        }
        """;

    public const string InvoiceBatch = """
        {
          "target": "AR301000",
          "commands": [
            { "type": "Clear" },
            { "type": "Set", "name": "Document.CustomerID", "value": "{{CustomerID}}" },
            { "type": "Action", "name": "Save" }
          ],
          "variables": [ { "name": "CustomerID", "type": "string", "description": "The customer" } ]
        }
        """;

    public const string ReadBatch = """
        {
          "target": "AR301000",
          "commands": [ { "type": "Clear" }, { "type": "Get", "name": "Document.Status" } ]
        }
        """;

    private readonly TempDirectory _directory = new();

    public ScreenHarness(bool terminal = false, string input = "")
    {
        Console = new TestConsole(redirected: !terminal, input);
        Environment = new TestEnvironment(_directory.Path);
        Environment.Variables[EnvironmentVariables.ConfigDirectory] = Path.Combine(_directory.Path, "config");
        Profiles = new ProfileStore(ConfigPaths.Resolve(Environment));
        Http = new FakeHttpHandler();
        Services = new CliServices(
            Console,
            Environment,
            Clock,
            Profiles,
            new TokenStore(ConfigPaths.Resolve(Environment)),
            new HttpClient(Http),
            new NoBrowser())
        {
            McpClientFactory = _ => Mcp,
        };
    }

    public ManualClock Clock { get; } = new(DateTimeOffset.Parse("2026-10-28T14:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

    public FakeMcpClient Mcp { get; } = new();

    public TestConsole Console { get; }

    public TestEnvironment Environment { get; }

    public ProfileStore Profiles { get; }

    public FakeHttpHandler Http { get; }

    public CliServices Services { get; }

    public string Directory => _directory.Path;

    public JsonNode Output => JsonNode.Parse(Console.StdOut)!;

    public JsonObject Error => (JsonObject)Output["error"]!;

    /// <summary>Gets the state of the default profile, which every test runs on.</summary>
    public ProfileState State => Profiles.LoadState("prod");

    /// <summary>Gets the arguments of the last <c>execute_commands</c> call.</summary>
    public JsonObject Executed => Mcp.Calls.Last(call => call.Name == "execute_commands").Arguments;

    /// <summary>Returns a <c>done</c> envelope whose <c>textualResponse</c> is <paramref name="envelope"/>, with <paramref name="beside"/> next to it.</summary>
    public static string Done(string envelope = """{"ok":true}""", string beside = "")
    {
        var result = JsonNode.Parse($"{{{beside}}}")!.AsObject();
        result["textualResponse"] = envelope;
        return new JsonObject { ["taskId"] = "t_1", ["status"] = "done", ["result"] = result }.ToJsonString();
    }

    public static string Continuation(long stamp) =>
        $$"""
        "continuation": { "screenId": "SO301000", "keys": { "Document.OrderType": "SO", "Document.OrderNbr": "<NEW>" }, "stamp": {{stamp}} }
        """;

    public string Write(string name, string content)
    {
        var path = Path.Combine(_directory.Path, name);
        File.WriteAllText(path, content);
        return path;
    }

    public Task<int> RunAsync(params string[] args) => Program.RunAsync(args, Services, TestContext.Current.CancellationToken);

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
