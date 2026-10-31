using System.Text.Json.Nodes;
using Velixo.Cli.Output;

namespace Velixo.Cli.Tests;

public sealed class FormatterTests
{
    [Fact]
    public void Csv_quotes_commas_quotes_line_breaks_and_edge_spaces()
    {
        var csv = CsvFormatter.Format(
            ["Customer", "Note"],
            [
                ["ACME, Inc.", "said \"hi\""],
                ["plain", "two\nlines"],
                [" padded", null],
            ]);

        Assert.Equal(
            "Customer,Note\n\"ACME, Inc.\",\"said \"\"hi\"\"\"\nplain,\"two\nlines\"\n\" padded\",\n",
            csv);
    }

    [Fact]
    public void Table_pads_columns_and_cuts_wide_cells()
    {
        var table = TableFormatter.Format(
            ["Customer", "Natural Net"],
            [["ABARTENDE", "12500"], ["A-VERY-LONG-CUSTOMER-NAME", "980.15"]],
            maxCellWidth: 10,
            footer: "rows 1 to 2 of 2");

        Assert.Equal(
            "Customer    Natural N…\n"
            + "----------  ----------\n"
            + "ABARTENDE   12500\n"
            + "A-VERY-LO…  980.15\n"
            + "rows 1 to 2 of 2\n",
            table);
    }

    [Fact]
    public void Query_result_takes_its_header_from_the_captions()
    {
        JsonNode document = JsonNode.Parse("""
            {
              "taskId": "t_1",
              "status": "done",
              "result": {
                "data": {
                  "columns": [
                    { "objectName": "ARInvoice", "fieldName": "CustomerID", "caption": "Customer", "type": "string" },
                    { "objectName": "ARInvoice", "fieldName": "Balance", "caption": "Natural Net", "type": "decimal" }
                  ],
                  "rows": [["ABARTENDE", 12500.00], ["BCORP", 980.15]],
                  "totalCount": 1204
                }
              }
            }
            """)!;

        TabularView view = TabularView.From(document)!;

        Assert.Equal(["Customer", "Natural Net"], view.Columns);
        Assert.Equal("BCORP", view.Rows[1][0]);
        Assert.Equal("980.15", view.Rows[1][1]);
        Assert.Equal("rows 1 to 2 of 1,204", view.Footer);
        Assert.Equal("Customer,Natural Net\nABARTENDE,12500.00\nBCORP,980.15\n", CsvFormatter.Format(view.Columns, view.Rows));
    }

    [Fact]
    public void Array_of_objects_takes_its_header_from_the_keys()
    {
        TabularView view = TabularView.From(JsonNode.Parse("""
            [{ "name": "gab-dev", "type": "acumatica", "status": "ok" }, { "name": "skyline", "type": "sage_intacct", "status": "ok", "description": "x" }]
            """))!;

        Assert.Equal(["name", "type", "status", "description"], view.Columns);
        Assert.Equal(string.Empty, view.Rows[0][3]);
    }
}
