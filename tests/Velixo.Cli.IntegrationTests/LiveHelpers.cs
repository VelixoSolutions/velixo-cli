using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.IntegrationTests;

/// <summary>What several live test classes read from the CLI's output, and the files they write for it.</summary>
public static class LiveHelpers
{
    /// <summary>The Acumatica vendor every test record and test import uses.</summary>
    public const string TestVendor = "VELIXOIT";

    /// <summary>The number a build's output carries its entry in the profile's list under, as text for the command line.</summary>
    public static string LibraryNumberOf(CliRun build) =>
        ((int)build.Json!["libraryNumber"]!).ToString(CultureInfo.InvariantCulture);

    /// <summary>Asserts that <paramref name="run"/> exited with <paramref name="exitCode"/>, with the run in the message.</summary>
    public static CliRun Expect(this CliRun run, int exitCode = ExitCodes.Success)
    {
        Assert.True(run.ExitCode == exitCode, $"Expected exit {exitCode}.\n{run}");
        return run;
    }

    /// <summary>Asserts that <paramref name="run"/> failed with the error <paramref name="code"/> and <paramref name="exitCode"/>.</summary>
    public static CliRun ExpectError(this CliRun run, int exitCode, string code)
    {
        run.Expect(exitCode);
        Assert.True((string?)run.Json?["error"]?["code"] == code, $"Expected error {code}.\n{run}");
        return run;
    }

    /// <summary>The <c>result.data</c> of a single-query run: its <c>columns</c>, <c>rows</c> and <c>totalCount</c>.</summary>
    public static JsonObject QueryData(CliRun run) =>
        run.Json?["result"]?["data"] as JsonObject ?? throw new Xunit.Sdk.XunitException($"No result.data.\n{run}");

    /// <summary>The <c>rows</c> of a single-query run.</summary>
    public static JsonArray Rows(CliRun run) =>
        QueryData(run)["rows"] as JsonArray ?? throw new Xunit.Sdk.XunitException($"No result.data.rows.\n{run}");

    /// <summary>The type of every command in a build's batch, over all its command sets.</summary>
    public static List<string?> CommandTypes(JsonNode batch)
    {
        JsonObject data = (batch["result"]?["data"] ?? batch).AsObject();
        return new[] { "commands", "lineCommands", "splitCommands", "finishCommands" }
            .SelectMany(set => data[set] as JsonArray ?? [])
            .Select(command => (string?)command?["type"])
            .ToList();
    }

    /// <summary>One CSV field, quoted when it holds a comma or a quote.</summary>
    public static string Csv(string value) =>
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;

    /// <summary>
    /// Writes a workbook of text cells: one sheet per entry of <paramref name="sheets"/>, in order, each
    /// row starting at <paramref name="firstCell"/> (such as <c>B2</c>). Enough for an import to read; the
    /// test project needs no spreadsheet library.
    /// </summary>
    public static void WriteWorkbook(string path, IReadOnlyList<(string Name, IReadOnlyList<IReadOnlyList<string>> Rows)> sheets, string firstCell = "A1")
    {
        var column = firstCell.TakeWhile(char.IsLetter).Aggregate(0, (sum, letter) => (sum * 26) + (char.ToUpperInvariant(letter) - 'A' + 1)) - 1;
        var row = int.Parse(firstCell.AsSpan(firstCell.TakeWhile(char.IsLetter).Count()), CultureInfo.InvariantCulture);
        var strings = new List<string>();
        int StringIndex(string text)
        {
            var index = strings.IndexOf(text);
            if (index < 0)
            {
                strings.Add(text);
                index = strings.Count - 1;
            }

            return index;
        }

        const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

        var worksheets = sheets.Select(sheet =>
        {
            var xml = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"{main}\"><sheetData>");
            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                xml.Append(CultureInfo.InvariantCulture, $"<row r=\"{row + r}\">");
                for (var c = 0; c < sheet.Rows[r].Count; c++)
                {
                    xml.Append(CultureInfo.InvariantCulture, $"<c r=\"{ColumnName(column + c)}{row + r}\" t=\"s\"><v>{StringIndex(sheet.Rows[r][c])}</v></c>");
                }

                xml.Append("</row>");
            }

            return xml.Append("</sheetData></worksheet>").ToString();
        }).ToList();

        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        Add("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>"
            + string.Concat(worksheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))
            + "</Types>");
        Add("_rels/.rels",
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"{packageRelationships}\">"
            + $"<Relationship Id=\"rId1\" Type=\"{relationships}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Add("xl/workbook.xml",
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"{main}\" xmlns:r=\"{relationships}\"><sheets>"
            + string.Concat(sheets.Select((sheet, i) => $"<sheet name=\"{SecurityElement.Escape(sheet.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))
            + "</sheets></workbook>");
        Add("xl/_rels/workbook.xml.rels",
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"{packageRelationships}\">"
            + string.Concat(worksheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"{relationships}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
            + $"<Relationship Id=\"rId{worksheets.Count + 1}\" Type=\"{relationships}/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>");
        for (var i = 0; i < worksheets.Count; i++)
        {
            Add($"xl/worksheets/sheet{i + 1}.xml", worksheets[i]);
        }

        Add("xl/sharedStrings.xml",
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"{main}\" count=\"{strings.Count}\" uniqueCount=\"{strings.Count}\">"
            + string.Concat(strings.Select(text => $"<si><t xml:space=\"preserve\">{SecurityElement.Escape(text)}</t></si>"))
            + "</sst>");
    }

    /// <summary>The letters of a zero-based column: 0 is A, 26 is AA.</summary>
    public static string ColumnName(int column)
    {
        var name = string.Empty;
        for (column++; column > 0; column = (column - 1) / 26)
        {
            name = (char)('A' + ((column - 1) % 26)) + name;
        }

        return name;
    }
}
