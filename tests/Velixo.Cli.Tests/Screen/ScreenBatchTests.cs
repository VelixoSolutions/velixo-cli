using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using Velixo.Cli.Screen;

namespace Velixo.Cli.Tests.Screen;

public sealed class ScreenBatchTests
{
    [Fact]
    public void Reads_the_whole_build_output_and_its_result_data_alike()
    {
        JsonNode whole = JsonNode.Parse(ScreenHarness.OrderBatch)!;

        ScreenBatch fromWhole = ScreenBatch.From(whole, "order.batch.json");
        ScreenBatch fromData = ScreenBatch.From(whole["result"]!["data"]!.DeepClone(), "order.batch.json");

        foreach (ScreenBatch batch in new[] { fromWhole, fromData })
        {
            Assert.Equal("SO301000", batch.Target);
            Assert.Equal(3, batch.Commands.Count);
            Assert.Equal(2, batch.LineCommands!.Count);
            Assert.Null(batch.SplitCommands);
            Assert.Equal(2, batch.FinishCommands!.Count);
            Assert.Equal("OrderNbr", (string?)batch.KeyNumbering!["field"]);
        }
    }

    [Theory]
    [InlineData("""{"target":"IN202500","writes":false,"commands":[{"type":"Clear"},{"type":"Set","name":"Item.InventoryCD","value":"X"},{"type":"DownloadAttachments","name":"Item"}]}""", false)]
    [InlineData("""{"target":"IN202500","writes":false,"commands":[{"type":"Set","name":"Item.Descr","value":"edited"},{"type":"Action","name":"Save"}]}""", true)]
    [InlineData("""{"target":"BC:customers","writes":true,"commands":[{"type":"Key","name":"number","value":"C1"},{"type":"Get","name":"displayName"}]}""", true)]
    [InlineData("""{"target":"IN202500","commands":[{"type":"Clear"},{"type":"Get","name":"Item.Descr"}]}""", false)]
    [InlineData("""{"target":"IN202500","commands":[{"type":"Action","name":"Save"}]}""", true)]
    public void A_batch_writes_when_the_server_says_so_or_a_command_can_save(string json, bool writes)
    {
        ScreenBatch batch = ScreenBatch.From(JsonNode.Parse(json), "b.json");

        Assert.Equal(writes, batch.Writes([batch.Commands, batch.LineCommands, batch.SplitCommands, batch.FinishCommands]));
    }

    [Fact]
    public void A_document_without_target_and_commands_is_a_usage_error()
    {
        CliException error = Assert.Throws<CliException>(() => ScreenBatch.From(JsonNode.Parse("""{"result":{"data":{"rows":[]}}}"""), "q.json"));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
        Assert.Contains("command build", error.NextStep, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""[{"type":"Clear"},{"type":"Get","name":"Document.Status"}]""", true)]
    [InlineData("""[{"type":"clear"},{"type":"get","name":"Document.Status"}]""", true)]
    [InlineData("""[{"type":"Clear"},{"type":"Key","name":"Document.RefNbr","value":"INV1"},{"type":"Get","name":"Document.Status"}]""", true)]
    [InlineData("""[{"type":"Clear"},{"type":"Set","name":"Item.InventoryCD","value":"{{InventoryID}}","commit":null},{"type":"DownloadAttachments","name":"Item","value":"*"}]""", true)]
    [InlineData("""[{"type":"Clear"},{"type":"Set","name":"Document.RefNbr","value":"INV1"},{"type":"ListAttachments","name":"Document"}]""", true)]
    [InlineData("""[{"type":"Clear"},{"type":"Action","name":"Save"}]""", false)]
    [InlineData("""[{"type":"Set","name":"Parameters.ReportID","value":"AR631000"},{"type":"Action","name":"RunReport"}]""", true)]
    [InlineData("""[{"type":"Set","name":"Document.RefNbr","value":"INV1"},{"type":"Action","name":"Next"},{"type":"Action","name":"cancel"}]""", true)]
    [InlineData("""[{"type":"Clear"},{"type":"Action","name":"RunReport","commit":true}]""", false)]
    [InlineData("""[{"type":"Clear"},{"type":"Action","name":"Release"}]""", false)]
    [InlineData("""[{"type":"Clear"},{"type":"Action"}]""", false)]
    [InlineData("""[{"type":"Set","name":"Document.RefNbr","value":"INV1"},{"type":"Set","name":"Document.Descr","value":"x"},{"type":"Action","name":"Save"}]""", false)]
    [InlineData("""[{"type":"Set","name":"Document.RefNbr","value":"INV1"},{"type":"Set","name":"Document.NoteText","value":"x"}]""", false)]
    [InlineData("""[{"type":"Set","name":"NotePopupText","value":"x"}]""", false)]
    [InlineData("""[{"type":"Set","name":"Document.Descr","value":"x","commit":true}]""", false)]
    [InlineData("""[{"type":"Set","name":"Document.RefNbr","value":"INV1"},{"type":"UploadAttachment","name":"Document"}]""", false)]
    [InlineData("""[{"type":"ServiceCommand","name":"Transactions.NewRow"}]""", false)]
    [InlineData("""[{"type":"ContinueOn"}]""", false)]
    [InlineData("""[{"type":"SomethingNew"}]""", false)]
    [InlineData("""[{"name":"Document.Status"}]""", false)]
    public void Only_a_batch_that_can_leave_nothing_in_the_ERP_is_read_only(string commands, bool readOnly)
    {
        Assert.Equal(readOnly, ScreenBatch.OnlyReads([JsonNode.Parse(commands)!.AsArray(), null]));
    }

    [Fact]
    public void Placeholders_are_whole_values_only()
    {
        JsonArray commands = JsonNode.Parse("""
            [
              { "type": "Set", "name": "A", "value": "{{CustomerID}}" },
              { "type": "Set", "name": "B", "value": " {{Amount}} " },
              { "type": "Set", "name": "C", "value": "INV-{{Nbr}}" },
              { "type": "Set", "name": "D", "value": "{{CustomerID}}" }
            ]
            """)!.AsArray();

        Assert.Equal(["CustomerID", "Amount"], ScreenPlaceholders.Names(commands));
    }

    [Fact]
    public void Variables_bind_null_and_empty_differently_and_flags_win_over_the_file()
    {
        using var directory = new Fakes.TempDirectory();
        var file = Path.Combine(directory.Path, "vars.json");
        File.WriteAllText(file, """{"CustomerID":"FROMFILE","Note":"kept","Hold":"true"}""");

        JsonObject variables = ScreenVariables.Bind(file, ["CustomerID=ACME", "Formula=a=b", "Blank="], ["Hold"]);

        Assert.Equal("ACME", (string?)variables["CustomerID"]);
        Assert.Equal("kept", (string?)variables["Note"]);
        Assert.Equal("a=b", (string?)variables["Formula"]);
        Assert.Equal(string.Empty, (string?)variables["Blank"]);
        Assert.True(variables.ContainsKey("Hold"));
        Assert.Null(variables["Hold"]);
    }

    [Theory]
    [InlineData("CustomerID")]
    [InlineData("=ACME")]
    public void A_var_without_a_name_and_an_equals_sign_is_a_usage_error(string assignment)
    {
        CliException error = Assert.Throws<CliException>(() => ScreenVariables.Bind(null, [assignment], []));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
    }

    [Fact]
    public void The_same_name_with_var_and_var_null_is_a_usage_error()
    {
        CliException error = Assert.Throws<CliException>(() => ScreenVariables.Bind(null, ["Hold=1"], ["Hold"]));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
    }
}
