using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class QueryPagerTests
{
    private static readonly ConsoleKeyInfo Enter = Key(ConsoleKey.Enter);
    private static readonly ConsoleKeyInfo Escape = Key(ConsoleKey.Escape);

    [Fact]
    public async Task The_keys_move_between_pages_and_a_page_seen_before_is_not_fetched_again()
    {
        using var test = new TestServices(redirected: false);
        var mcp = Mcp(test, Page(250, 600, 1), Page(250, 600, 251), Page(100, 600, 501));
        var query = Query(test);
        Keys(test, Enter, Key(ConsoleKey.End), Key(ConsoleKey.LeftArrow), Key(ConsoleKey.Home), Escape);

        var exit = await test.RunAsync("query", "run", "--query", query, "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([0, 250, 500], mcp.Calls.Select(call => (int)call.Arguments["skip"]!));
        var printed = test.Console.StdOut;
        Assert.Contains("C251 ", printed, StringComparison.Ordinal);
        Assert.Contains("C600 ", printed, StringComparison.Ordinal);
        Assert.Contains("rows 501 to 600 of 600", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("--skip", printed, StringComparison.Ordinal);
        Assert.Contains("Page 3 of 3", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Empty(test.Console.Keys);
    }

    [Fact]
    public async Task Enter_on_the_last_page_quits()
    {
        using var test = new TestServices(redirected: false);
        var mcp = Mcp(test, Page(250, 300, 1), Page(50, 300, 251));
        Keys(test, Enter, Enter);

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(2, mcp.Calls.Count);
        Assert.Empty(test.Console.Keys);
    }

    [Fact]
    public async Task A_result_that_fits_one_page_prints_without_the_bar()
    {
        using var test = new TestServices(redirected: false);
        Mcp(test, Page(2, 2, 1));

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("Page 1", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("rows 1 to 2 of 2", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_colour_the_bar_names_the_page_the_window_and_the_keys_in_plain_text()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["NO_COLOR"] = "1" }, redirected: false);
        Mcp(test, Page(250, 600, 1));
        Keys(test, Escape);

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Contains("[ Page 1 of 3 · rows 1–250 of 600 ]   Enter → next  End last  Esc quit", test.Console.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[7", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_colour_the_page_is_in_reverse_video_and_the_keys_are_bold()
    {
        using var test = new TestServices(redirected: false);
        Mcp(test, Page(250, 600, 1));
        Keys(test, Escape);

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Contains("\u001b[7;1m Page 1 of 3 \u001b[0m", test.Console.StdErr, StringComparison.Ordinal);
        Assert.Contains("\u001b[1;36mEsc\u001b[0m quit", test.Console.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pipe_gets_the_first_window_and_the_flags_for_the_rest_without_paging()
    {
        using var test = new TestServices();
        var mcp = Mcp(test, Page(250, 600, 1));

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--output", "table", "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(mcp.Calls);
        Assert.Contains("add --skip 250 or --all for the rest", test.Console.StdOut, StringComparison.Ordinal);
    }

    private static FakeMcpClient Mcp(TestServices test, params string[] pages)
    {
        var mcp = new FakeMcpClient();
        foreach (var page in pages)
        {
            mcp.ThenText(page);
        }

        test.Services.McpClientFactory = _ => mcp;
        return mcp;
    }

    private static string Query(TestServices test)
    {
        var path = Path.Combine(test.Directory, "q.vql");
        File.WriteAllText(path, "FROM ARInvoice ORDER BY CustomerID SELECT CustomerID, Balance");
        return path;
    }

    private static void Keys(TestServices test, params ConsoleKeyInfo[] keys)
    {
        foreach (ConsoleKeyInfo key in keys)
        {
            test.Console.Keys.Enqueue(key);
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, shift: false, alt: false, control: false);

    /// <summary>A finished task whose result holds <paramref name="rows"/> rows from row <paramref name="firstRow"/> of <paramref name="total"/>.</summary>
    private static string Page(int rows, int total, int firstRow)
    {
        var values = new JsonArray();
        for (var index = firstRow; index < firstRow + rows; index++)
        {
            values.Add((JsonNode)new JsonArray($"C{index}", index + 0.5));
        }

        var data = new JsonObject
        {
            ["columns"] = JsonNode.Parse("""[{"caption":"Customer","type":"String"},{"caption":"Balance","type":"Decimal","precision":2}]"""),
            ["rows"] = values,
            ["totalCount"] = total,
        };
        return new JsonObject
        {
            ["taskId"] = "t_1",
            ["status"] = "done",
            ["result"] = new JsonObject { ["textualResponse"] = data.ToJsonString() },
        }.ToJsonString();
    }
}
