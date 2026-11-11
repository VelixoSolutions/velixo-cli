using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;
using Velixo.Cli.Tests.Screen;

namespace Velixo.Cli.Tests;

/// <summary>Record links: in a query result's cells, and under a batch that saved a record.</summary>
public sealed class RecordLinksTests
{
    private const string Intent = "Open the sales orders";

    private const string BaseUrl = "https://demo.velixo.com/Main?CompanyID=Company&";

    private const string Order396 = BaseUrl + "ScreenId=SO301000&OrderType=SO&OrderNbr=SO005396";

    private const string Osc = "\u001b]8;;";

    private const string End = "\u001b\\";

    [Fact]
    public void A_link_takes_no_columns_and_keeps_its_url_around_the_text_a_slice_leaves()
    {
        var line = "ab " + Hyperlink.Wrap("SO005396", Order396) + " cd";

        Assert.Equal(14, Hyperlink.VisibleLength(line));
        Assert.Equal($"{Osc}{Order396}{End}5396{Osc}{End} c", Hyperlink.Slice(line, 7, 6));
        Assert.Equal($"b {Osc}{Order396}{End}SO{Osc}{End}", Hyperlink.Slice(line, 1, 4));
        Assert.Equal("ab ", Hyperlink.Slice(line, 0, 3));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://x.example.com/\u001b]8;;evil")]
    [InlineData("relative/path")]
    [InlineData("")]
    public void A_url_that_is_not_plain_http_links_nothing(string url) =>
        Assert.Equal("SO005396", Hyperlink.Wrap("SO005396", url));

    [Fact]
    public void Each_kind_of_link_resolves_to_the_url_of_its_record()
    {
        var rows = JsonNode.Parse("""[["SO","SO005396","ABARTENDE "],["SO","SO 7/8","ABARTENDE "],["SO","SO005398","X"],["SO","SO005399","Y"]]""")!.AsArray();
        var links = JsonNode.Parse($$"""
            {
              "baseUrl": "{{BaseUrl}}",
              "columns": [
                { "column": 1, "keys": { "OrderType": 0, "OrderNbr": 1 }, "screenId": "SO301000" },
                { "column": 2, "keys": { "AcctCD": 2 }, "screenId": "AR303000" },
                { "column": 0 }
              ],
              "rows": [[1, 1, 1], [1, "AR303001"], [null, null], null]
            }
            """);

        IReadOnlyList<IReadOnlyList<string?>> cells = RecordLinks.Resolve(links, rows)!;

        Assert.Equal([null, Order396, BaseUrl + "ScreenId=AR303000&AcctCD=ABARTENDE"], cells[0]);
        Assert.Equal(BaseUrl + "ScreenId=SO301000&OrderType=SO&OrderNbr=SO%207%2F8", cells[1][1]);
        Assert.Equal(BaseUrl + "ScreenId=AR303001&AcctCD=ABARTENDE", cells[1][2]);
        Assert.All(cells[2], Assert.Null);
        Assert.All(cells[3], Assert.Null);
    }

    [Fact]
    public void A_rest_column_appends_its_string_to_the_base_url()
    {
        var rows = JsonNode.Parse("""[["RD14754-QA-2220"],["V2"]]""")!.AsArray();
        var links = JsonNode.Parse("""
            { "baseUrl": "https://www.intacct.com/ia/acct/ur.phtml?.r=", "columns": [{ "column": 0, "rest": true }], "rows": [["_U468raOWMtlClXMbm-yPQ"], [null]] }
            """);

        IReadOnlyList<IReadOnlyList<string?>> cells = RecordLinks.Resolve(links, rows)!;

        Assert.Equal("https://www.intacct.com/ia/acct/ur.phtml?.r=_U468raOWMtlClXMbm-yPQ", cells[0][0]);
        Assert.Null(cells[1][0]);
    }

    [Fact]
    public void A_table_lines_up_on_the_text_of_a_linked_cell_not_on_its_escape_sequence()
    {
        var table = TableFormatter.Format(
            ["Nbr", "Customer"],
            [["SO005396", "ABARTENDE"], ["SO1", "B"]],
            links: [[Order396, null], [null, null]]);

        var lines = table.Split('\n');
        Assert.Equal($"{Osc}{Order396}{End}SO005396{Osc}{End}  ABARTENDE", lines[2]);
        Assert.Equal("SO1       B", lines[3]);
    }

    [Fact]
    public async Task A_table_at_a_terminal_asks_for_links_and_makes_the_cells_clickable()
    {
        using var test = new TestServices(redirected: false);
        FakeMcpClient mcp = Mcp(test, OrderResult());

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True((bool?)mcp.Calls[0].Arguments["includeRecordLinks"]);
        Assert.Contains($"{Osc}{Order396}{End}SO005396{Osc}{End}", test.Console.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(" URL", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_at_a_terminal_asks_for_none()
    {
        using var test = new TestServices(redirected: false);
        FakeMcpClient mcp = Mcp(test, OrderResult());

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent, "--output", "json");

        Assert.False(mcp.Calls[0].Arguments.ContainsKey("includeRecordLinks"));
        Assert.DoesNotContain(Osc, test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_at_a_terminal_gets_json_without_links_unless_it_asks()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["CLAUDECODE"] = "1" }, redirected: false);
        FakeMcpClient mcp = Mcp(test, OrderResult(), OrderResult());

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent);
        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent, "--links");

        Assert.False(mcp.Calls[0].Arguments.ContainsKey("includeRecordLinks"));
        Assert.True((bool?)mcp.Calls[1].Arguments["includeRecordLinks"]);
    }

    [Fact]
    public async Task A_pipe_with_links_gets_the_server_links_in_json_and_a_url_column_in_csv_and_table()
    {
        using var test = new TestServices();
        FakeMcpClient mcp = Mcp(test, OrderResult(), OrderResult(), OrderResult());

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent, "--links");
        JsonNode json = JsonNode.Parse(test.Console.StdOut)!;
        test.Console.OutWriter.GetStringBuilder().Clear();
        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent, "--links", "--output", "csv");
        var csv = test.Console.StdOut;
        test.Console.OutWriter.GetStringBuilder().Clear();
        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent, "--links", "--output", "table");

        Assert.All(mcp.Calls, call => Assert.True((bool?)call.Arguments["includeRecordLinks"]));
        Assert.Equal(BaseUrl, (string?)json["result"]!["data"]!["recordLinks"]!["baseUrl"]);
        Assert.StartsWith($"Order Type,Order Nbr.,Order Nbr. URL,Customer\nSO,SO005396,{Order396},\"ABARTENDE \"\nSO,SO005397,,", csv, StringComparison.Ordinal);
        Assert.Contains($"SO005396    {Order396}  ABARTENDE", test.Console.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain(Osc, test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pipe_without_links_asks_for_none_even_in_a_table()
    {
        using var test = new TestServices();
        FakeMcpClient mcp = Mcp(test, OrderResult());

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent, "--output", "table");

        Assert.False(mcp.Calls[0].Arguments.ContainsKey("includeRecordLinks"));
    }

    [Fact]
    public async Task A_saved_inquiry_never_asks_for_links_and_says_so_when_links_were_asked_for()
    {
        using var test = new TestServices();
        FakeMcpClient mcp = Mcp(test, OrderResult(), OrderResult());

        await test.RunAsync("query", "run", "--inquiry", "SO-Open", "--intent", Intent);
        Assert.DoesNotContain("--links", test.Console.StdErr, StringComparison.Ordinal);
        await test.RunAsync("query", "run", "--inquiry", "SO-Open", "--intent", Intent, "--links");

        Assert.All(mcp.Calls, call => Assert.False(call.Arguments.ContainsKey("includeRecordLinks")));
        Assert.Contains("warning: A saved inquiry has no record links, so --links gives 'SO-Open' none.", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_url_column_prints_whole_while_the_other_columns_are_still_cut()
    {
        var (columns, rows, _, isUrl) = RecordLinks.AddUrlColumns(
            ["Nbr", "Description"],
            [["SO005396", new string('d', 40)]],
            [[Order396, null]]);

        var table = TableFormatter.Format(columns, rows, maxCellWidth: 20, uncut: isUrl);

        Assert.Equal(["Nbr", "Nbr URL", "Description"], columns);
        Assert.Contains($"SO005396  {Order396}  {new string('d', 19)}…", table, StringComparison.Ordinal);
    }

    [Fact]
    public async Task All_puts_each_windows_links_at_the_offset_of_its_first_row()
    {
        using var test = new TestServices();
        Mcp(test, OrderResult(first: 396, total: 4), OrderResult(first: 398, total: 4));

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent, "--links", "--all", "--top", "2");

        JsonNode links = JsonNode.Parse(test.Console.StdOut)!["result"]!["data"]!["recordLinks"]!;
        Assert.Equal("""[[1],null,[1],null]""", links["rows"]!.ToJsonString());
        Assert.Equal(BaseUrl, (string?)links["baseUrl"]);
    }

    [Fact]
    public async Task The_pager_keeps_a_link_on_the_cells_it_shows()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["NO_COLOR"] = "1" }, redirected: false);
        test.Console.Height = 5;
        Mcp(test, OrderResult(rows: 3, total: 3));
        test.Console.Keys.Enqueue(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", Intent);

        Assert.Contains("\u001b[?1049h", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Contains($"{Osc}{Order396}{End}SO005396{Osc}{End}", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, null, null, true)]
    [InlineData(true, null, null, false)]
    [InlineData(false, "1", null, false)]
    [InlineData(false, null, "dumb", false)]
    public void Hyperlinks_go_only_to_a_terminal_a_person_reads(bool outputRedirected, string? claudeCode, string? term, bool expected)
    {
        var variables = new Dictionary<string, string>();
        if (claudeCode is not null)
        {
            variables["CLAUDECODE"] = claudeCode;
        }

        if (term is not null)
        {
            variables["TERM"] = term;
        }

        ResolvedSettings settings = Resolve(new CommandLineValues(), variables, outputRedirected);

        Assert.Equal(expected && !OperatingSystem.IsWindows(), settings.Output.Hyperlinks);
    }

    [Fact]
    public async Task A_batch_that_saved_a_record_prints_its_link_under_the_table()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(SavedResult());

        await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--yes", "--intent", ScreenHarness.Intent, "--output", "table");

        Assert.EndsWith($"\nSaved SO SO005396: {Order396}\n", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_prints_no_saved_record_line()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("invoice.batch.json", ScreenHarness.InvoiceBatch);
        test.Mcp.ThenText(SavedResult());

        await test.RunAsync("command", "run", "--batch", batch, "--param", "CustomerID=ACME", "--yes", "--intent", ScreenHarness.Intent, "--output", "json");

        Assert.DoesNotContain("Saved SO", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_batch_without_a_record_url_prints_no_saved_record_line()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("read.batch.json", ScreenHarness.ReadBatch);
        test.Mcp.ThenText(ScreenHarness.Done(beside: "\"feedback\":{\"savedKey\":{\"RefNbr\":\"INV001\"}}"));

        await test.RunAsync("command", "run", "--batch", batch, "--intent", ScreenHarness.Intent, "--output", "table");

        Assert.DoesNotContain("Saved", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"id\":\"V-READ\",\"name\":\"Acme\"}", "Saved V-READ: ")]
    [InlineData("{\"name\":\"Acme\"}", "Saved V-PARAM: ")]
    public async Task A_sage_intacct_record_is_named_by_its_id_read_back_or_else_as_passed(string fields, string expected)
    {
        using var test = new ScreenHarness();
        var batch = test.Write("vendor.batch.json", VendorBatch);
        test.Mcp.ThenText(VendorSaved(fields));

        await test.RunAsync("command", "run", "--batch", batch, "--param", "VendorID=V-PARAM", "--param", "Name=Acme", "--yes", "--intent", ScreenHarness.Intent, "--output", "table");

        Assert.EndsWith($"\n{expected}{IntacctUrl}\n", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sage_intacct_batch_without_a_key_command_keeps_the_record_number()
    {
        using var test = new ScreenHarness();
        var batch = test.Write("vendor.batch.json", VendorBatch.Replace("\"Key\"", "\"Set\"", StringComparison.Ordinal));
        test.Mcp.ThenText(VendorSaved("{\"name\":\"Acme\"}"));

        await test.RunAsync("command", "run", "--batch", batch, "--param", "VendorID=V-PARAM", "--param", "Name=Acme", "--yes", "--intent", ScreenHarness.Intent, "--output", "table");

        Assert.EndsWith($"\nSaved 300: {IntacctUrl}\n", test.Console.StdOut, StringComparison.Ordinal);
    }

    private const string IntacctUrl = "https://www.intacct.com/ia/acct/ur.phtml?.r=uXIhsGKU";

    private const string VendorBatch = """
        {
          "target": "accounts-payable/vendor",
          "commands": [
            { "type": "Key", "name": "id", "value": "{{VendorID}}" },
            { "type": "Set", "name": "name", "value": "{{Name}}" },
            { "type": "Action", "name": "Save" },
            { "type": "Get", "name": "name" }
          ]
        }
        """;

    /// <summary>A saved Sage Intacct vendor: record number 300 in <c>savedKey</c>, the read-back in <c>data.fields</c>.</summary>
    private static string VendorSaved(string fields) =>
        ScreenHarness.Done(beside: $"\"data\":{{\"object\":\"accounts-payable/vendor\",\"operation\":\"updated\",\"key\":\"300\",\"fields\":{fields}}},"
            + $"\"feedback\":{{\"savedKey\":{{\"key\":\"300\"}},\"recordUrl\":\"{IntacctUrl}\"}}");

    private static string SavedResult() =>
        ScreenHarness.Done(beside: $"\"feedback\":{{\"savedKey\":{{\"OrderType\":\"SO\",\"OrderNbr\":\"SO005396\"}},\"recordUrl\":\"{Order396}\"}}");

    private static FakeMcpClient Mcp(TestServices test, params string[] results)
    {
        var mcp = new FakeMcpClient();
        foreach (var result in results)
        {
            mcp.ThenText(result);
        }

        test.Services.McpClientFactory = _ => mcp;
        return mcp;
    }

    private static string Query(TestServices test)
    {
        var path = Path.Combine(test.Directory, "so.vql");
        File.WriteAllText(path, "FROM SOOrder ORDER BY OrderNbr SELECT OrderType, OrderNbr, CustomerID");
        return path;
    }

    /// <summary>
    /// A finished query of <paramref name="rows"/> sales orders from SO00<paramref name="first"/>, whose record links
    /// cover the first row only, as the server's links cover the first rows of a window.
    /// </summary>
    private static string OrderResult(int first = 396, int rows = 2, int total = 2)
    {
        var values = new JsonArray();
        for (var index = first; index < first + rows; index++)
        {
            values.Add((JsonNode)new JsonArray("SO", $"SO005{index}", "ABARTENDE "));
        }

        var data = new JsonObject
        {
            ["columns"] = JsonNode.Parse("""[{"caption":"Order Type","type":"string"},{"caption":"Order Nbr.","type":"string"},{"caption":"Customer","type":"string"}]"""),
            ["rows"] = values,
            ["totalCount"] = total,
            ["recordLinks"] = JsonNode.Parse($$"""
                { "baseUrl": "{{BaseUrl}}", "columns": [{ "column": 1, "keys": { "OrderType": 0, "OrderNbr": 1 }, "screenId": "SO301000" }], "rows": [[1]] }
                """),
        };
        return new JsonObject
        {
            ["taskId"] = "t_1",
            ["status"] = "done",
            ["result"] = new JsonObject { ["textualResponse"] = data.ToJsonString() },
        }.ToJsonString();
    }

    private static ResolvedSettings Resolve(CommandLineValues flags, Dictionary<string, string>? variables = null, bool outputRedirected = false) =>
        SettingsResolver.Resolve(new SettingsSources
        {
            Flags = flags,
            GetVariable = (variables ?? []).GetValueOrDefault,
            Config = new CliConfig(),
            IsOutputRedirected = outputRedirected,
        });
}
