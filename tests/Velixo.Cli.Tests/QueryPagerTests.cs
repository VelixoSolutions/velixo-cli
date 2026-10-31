using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Velixo.Cli.Output;
using Velixo.Cli.Tests.Fakes;

namespace Velixo.Cli.Tests;

public sealed class QueryPagerTests
{
    private static readonly ConsoleKeyInfo Enter = Key(ConsoleKey.Enter);
    private static readonly ConsoleKeyInfo Escape = Key(ConsoleKey.Escape);
    private static readonly ConsoleKeyInfo Up = Key(ConsoleKey.UpArrow);
    private static readonly ConsoleKeyInfo Down = Key(ConsoleKey.DownArrow);
    private static readonly ConsoleKeyInfo Left = Key(ConsoleKey.LeftArrow);
    private static readonly ConsoleKeyInfo Right = Key(ConsoleKey.RightArrow);
    private static readonly ConsoleKeyInfo PageDown = Key(ConsoleKey.PageDown);
    private static readonly ConsoleKeyInfo Home = Key(ConsoleKey.Home);
    private static readonly ConsoleKeyInfo End = Key(ConsoleKey.End);

    [Fact]
    public async Task The_headers_stay_at_the_top_of_every_screen_while_the_rows_scroll_under_them()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["NO_COLOR"] = "1" }, redirected: false);
        test.Console.Height = 10;
        Mcp(test, Page(250, 600, 1), Page(100, 600, 501));
        Keys(test, Down, Down, End, Up, Escape);

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        var frames = Frames(test);
        Assert.Equal(5, frames.Count);
        Assert.All(frames, frame => Assert.StartsWith("Customer", frame[0], StringComparison.Ordinal));
        Assert.All(frames, frame => Assert.StartsWith("--------", frame[1], StringComparison.Ordinal));
        Assert.StartsWith("C3 ", frames[2][2], StringComparison.Ordinal);
        Assert.StartsWith("C600 ", frames[3][8], StringComparison.Ordinal);
        Assert.StartsWith("C593 ", frames[4][2], StringComparison.Ordinal);
        Assert.Empty(test.Console.Keys);
    }

    [Fact]
    public async Task A_resized_terminal_is_drawn_again_at_its_new_size_without_a_key()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["NO_COLOR"] = "1" }, redirected: false);
        test.Console.Height = 10;
        Mcp(test, Page(250, 600, 1));
        test.Console.Resizes.Enqueue((60, 16));
        Keys(test, Escape);

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        var frames = Frames(test);
        Assert.Equal(2, frames.Count);
        Assert.Contains("\u001b[2J", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Contains("rows 1–7 of 600", frames[0][^1], StringComparison.Ordinal);
        Assert.Contains("rows 1–13 of 600", frames[1][^1], StringComparison.Ordinal);
        Assert.All(frames[1], line => Assert.True(line.Length <= 60, $"'{line}' is wider than the terminal."));
    }

    [Fact]
    public async Task Rows_are_fetched_a_window_at_a_time_as_they_scroll_into_view_and_never_twice()
    {
        using var test = new TestServices(redirected: false);
        var mcp = Mcp(test, Page(250, 600, 1), Page(250, 600, 251), Page(100, 600, 501));
        Keys(test, [.. Enumerable.Repeat(PageDown, 9), End, Home, PageDown, Escape]);

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([0, 250, 500], mcp.Calls.Select(call => (int)call.Arguments["skip"]!));
        Assert.Equal([250, 250, 250], mcp.Calls.Select(call => (int)call.Arguments["top"]!));
    }

    [Fact]
    public async Task A_window_after_skip_scrolls_back_to_the_rows_before_it()
    {
        using var test = new TestServices(redirected: false);
        var mcp = Mcp(test, Page(250, 600, 301), Page(250, 600, 51), Page(50, 600, 1));
        Keys(test, Up, Home, Escape);

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--skip", "300", "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal([300, 50, 0], mcp.Calls.Select(call => (int)call.Arguments["skip"]!));
        Assert.StartsWith("C1 ", Frames(test)[^1][2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Quitting_leaves_the_screen_and_prints_the_rows_that_were_on_it()
    {
        using var test = new TestServices(redirected: false);
        test.Console.Height = 10;
        Mcp(test, Page(250, 600, 1));
        Keys(test, PageDown, Escape);

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        var printed = test.Console.StdOut;
        Assert.Contains("\u001b[?1049h", printed, StringComparison.Ordinal);
        var afterViewer = printed[(printed.LastIndexOf("\u001b[?1049l", StringComparison.Ordinal) + 8)..];
        var lines = afterViewer.TrimEnd('\n').Split('\n');
        Assert.StartsWith("Customer", lines[0], StringComparison.Ordinal);
        Assert.Equal(["C8 ", "C14 "], [lines[2][..3], lines[^2][..4]]);
        Assert.Equal("rows 8 to 14 of 600; add --skip 14 or --all for the rest", lines[^1]);
    }

    [Fact]
    public async Task Enter_on_the_last_screen_quits()
    {
        using var test = new TestServices(redirected: false);
        test.Console.Height = 10;
        var mcp = Mcp(test, Page(30, 30, 1));
        Keys(test, Enter, End, Enter);

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(mcp.Calls);
        Assert.Empty(test.Console.Keys);
    }

    [Fact]
    public async Task A_result_that_fits_on_the_screen_prints_as_a_table_without_the_viewer()
    {
        using var test = new TestServices(redirected: false);
        Mcp(test, Page(2, 2, 1));

        var exit = await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.DoesNotContain("\u001b[?1049h", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Contains("rows 1 to 2 of 2", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_whole_result_taller_than_the_screen_opens_the_viewer()
    {
        using var test = new TestServices(redirected: false);
        test.Console.Height = 10;
        Mcp(test, Page(20, 20, 1));
        Keys(test, Escape);

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Contains("\u001b[?1049h", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_colour_the_bar_names_the_rows_on_screen_and_the_keys_in_plain_text()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["NO_COLOR"] = "1" }, redirected: false);
        Mcp(test, Page(250, 600, 1));
        Keys(test, Escape);

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Contains("[ rows 1–27 of 600 ]   ↑↓ scroll  PgUp PgDn page  Home End first, last  Esc quit", test.Console.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[7", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_colour_the_rows_are_in_reverse_video_and_the_keys_are_bold()
    {
        using var test = new TestServices(redirected: false);
        Mcp(test, Page(250, 600, 1));
        Keys(test, Escape);

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        Assert.Contains("\u001b[7;1m rows 1–27 of 600 \u001b[0m", test.Console.StdOut, StringComparison.Ordinal);
        Assert.Contains("\u001b[1;36mEsc\u001b[0m quit", test.Console.StdOut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_table_wider_than_the_terminal_scrolls_sideways()
    {
        using var test = new TestServices(new Dictionary<string, string> { ["NO_COLOR"] = "1" }, redirected: false);
        Mcp(test, Page(250, 600, 1, padding: 70));
        Keys(test, Right, Left, Escape);

        await test.RunAsync("query", "run", "--query", Query(test), "--intent", "Test the pager");

        var frames = Frames(test);
        Assert.Contains("← → sideways", test.Console.StdOut, StringComparison.Ordinal);
        Assert.StartsWith("C1x", frames[0][2], StringComparison.Ordinal);
        Assert.StartsWith("xxx", frames[1][2], StringComparison.Ordinal);
        Assert.Equal(frames[0][2], frames[2][2]);
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

    /// <summary>The screens the viewer drew, each as its lines without the escape sequences: the header, the rule, then the rows.</summary>
    private static List<string[]> Frames(TestServices test)
    {
        var printed = test.Console.StdOut;
        var viewer = printed[..printed.LastIndexOf("\u001b[?1049l", StringComparison.Ordinal)];
        return viewer.Split("\u001b[H")[1..]
            .Select(frame => Regex.Replace(frame, "\u001b\\[[0-9;?]*[A-Za-z]", string.Empty).Split("\r\n"))
            .ToList();
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, shift: false, alt: false, control: false);

    /// <summary>
    /// A finished task whose result holds <paramref name="rows"/> rows from row <paramref name="firstRow"/> of <paramref name="total"/>,
    /// each customer followed by <paramref name="padding"/> x's to make the table wide.
    /// </summary>
    private static string Page(int rows, int total, int firstRow, int padding = 0)
    {
        var values = new JsonArray();
        for (var index = firstRow; index < firstRow + rows; index++)
        {
            values.Add((JsonNode)new JsonArray($"C{index}" + new string('x', padding), index + 0.5, $"C{index}" + new string('x', padding)));
        }

        var data = new JsonObject
        {
            ["columns"] = JsonNode.Parse("""[{"caption":"Customer","type":"String"},{"caption":"Balance","type":"Decimal","precision":2},{"caption":"Name","type":"String"}]"""),
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
