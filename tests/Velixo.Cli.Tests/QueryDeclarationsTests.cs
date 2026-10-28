using System.Text.Json.Nodes;
using Velixo.Cli.Output;
using Velixo.Cli.Query;

namespace Velixo.Cli.Tests;

public sealed class QueryDeclarationsTests
{
    private const string Query = """
        DECLARE [AsOf] SCHEMA [PX.Objects.AR.ARInvoice].DocDate AS 'As Of Date' REQUIRED
        DECLARE [Branch] TYPE string REQUIRED
        DECLARE [DateFrom] TYPE date DEFAULT @YearStart AS 'Date From' REQUIRED
        DECLARE [Cust] SCHEMA [PX.Objects.AR.ARInvoice].CustomerID AS 'Customer''s ID'
        FROM ARInvoice
        SELECT CustomerID
        """;

    [Fact]
    public void Reads_label_name_required_and_default_of_each_declaration()
    {
        IReadOnlyList<DeclaredParameter> declared = QueryDeclarations.Read(Query);

        Assert.Equal(["As Of Date", "Branch", "Date From", "Customer's ID"], declared.Select(parameter => parameter.Key));
        Assert.Equal([true, true, false, false], declared.Select(parameter => parameter.NeedsValue));
        Assert.Null(declared[1].Label);
        Assert.Equal("Branch", declared[1].Name);
    }

    [Fact]
    public void Reads_declarations_written_on_one_line()
    {
        IReadOnlyList<DeclaredParameter> declared = QueryDeclarations.Read(
            "DECLARE [A] TYPE date AS 'From' REQUIRED DECLARE B TYPE number FROM GLTran SELECT Amount");

        Assert.Equal(["From", "B"], declared.Select(parameter => parameter.Key));
    }

    [Theory]
    [InlineData("FROM GLTran ORDER BY GLTran.TranDate SELECT Amount", true)]
    [InlineData("FROM GLTran order  by TranDate SELECT Amount", true)]
    [InlineData("DECLARE [A] TYPE string AS 'Order by' FROM GLTran SELECT Amount", false)]
    [InlineData("FROM GLTran -- ORDER BY TranDate\nSELECT Amount", false)]
    [InlineData("FROM GLTran SELECT Amount", false)]
    public void Finds_an_order_by_only_outside_labels_and_comments(string query, bool expected) =>
        Assert.Equal(expected, QueryDeclarations.HasOrderBy(query));

    [Fact]
    public void Warns_for_an_unknown_key_and_a_missing_required_value()
    {
        var given = new JsonObject { ["As Of Date"] = "2026-09-30", ["Bogus"] = "1", ["customer's id"] = "C1" };

        BoundParameters bound = ParameterBinder.ForQuery(Query, given, onlyDeclared: false);
        IReadOnlyList<string> warnings = ParameterBinder.Warnings(given, [(null, bound)]);

        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, warning => warning.StartsWith("The parameter 'Bogus' matches no parameter", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("Did you mean 'Customer's ID'?", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.StartsWith("The query requires 'Branch'", StringComparison.Ordinal));
        Assert.Equal(3, bound.Send.Count);
    }

    [Fact]
    public void A_plan_query_is_sent_only_the_keys_it_declares()
    {
        var given = new JsonObject { ["As Of Date"] = "2026-09-30", ["Branch"] = "HQ", ["Ledger"] = "ACTUAL" };

        BoundParameters bound = ParameterBinder.ForQuery(Query, given, onlyDeclared: true);

        Assert.Equal(["As Of Date", "Branch"], bound.Send.Select(pair => pair.Key));
    }

    [Fact]
    public void An_inquiry_item_fills_its_placeholders_and_leaves_out_an_unfilled_one()
    {
        var plan = new JsonObject { ["Branch"] = "{{BranchID}}", ["As Of"] = "{{AsOfDate}}", ["Status"] = "Open" };
        var given = new JsonObject { ["BranchID"] = "HQ" };

        BoundParameters bound = ParameterBinder.ForInquiry(plan, given);

        Assert.Equal("""{"Branch":"HQ","Status":"Open"}""", bound.Send.ToJsonString());
        Assert.Contains("{{AsOfDate}}", Assert.Single(bound.Missing), StringComparison.Ordinal);
        Assert.Contains("BranchID", bound.Accepted);
    }

    [Theory]
    [InlineData("As Of Date=2026-09-30", "As Of Date", "2026-09-30")]
    [InlineData(" Filter = a=b ", "Filter", " a=b ")]
    [InlineData("Note=", "Note", "")]
    public void A_param_splits_on_its_first_equals_sign(string pair, string label, string value) =>
        Assert.Equal((label, value), QueryInputs.SplitPair(pair));

    [Theory]
    [InlineData("NoEquals")]
    [InlineData("=value")]
    public void A_param_without_a_label_is_a_usage_error(string pair)
    {
        CliException error = Assert.Throws<CliException>(() => QueryInputs.SplitPair(pair));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
    }

    [Fact]
    public void Normalising_a_plan_renames_giql_to_query_in_place()
    {
        var document = JsonNode.Parse("""
            {"taskId":"t_1","status":"done","result":{"data":{"queries":[
              {"purpose":"Balance","giql":"FROM A SELECT B"},
              {"purpose":"Aging","inquiry":{"name":"AR-Aging","parameters":{}}}],"note":"n"}}}
            """)!.AsObject();

        QueryPlan.NormalizeItemKeys(document);

        JsonArray queries = document["result"]!["data"]!["queries"]!.AsArray();
        Assert.Equal("""{"purpose":"Balance","query":"FROM A SELECT B"}""", queries[0]!.ToJsonString());
        Assert.NotNull(queries[1]!["inquiry"]);
    }

    [Fact]
    public void A_plan_reads_query_or_giql_from_the_whole_document_or_its_data()
    {
        var data = JsonNode.Parse("""{"queries":[{"purpose":"A","query":"Q1"},{"purpose":"B","giql":"Q2"}],"note":"n"}""")!;
        var whole = new JsonObject { ["taskId"] = "t_1", ["status"] = "done", ["result"] = new JsonObject { ["data"] = data.DeepClone() } };

        foreach (JsonNode document in new[] { data, whole })
        {
            QueryPlan plan = QueryPlan.Read(document, "plan.json");

            Assert.Equal(["Q1", "Q2"], plan.Items.Select(item => item.Query));
            Assert.Equal("n", plan.Note);
        }
    }

    [Fact]
    public void A_footer_names_the_window_and_how_to_get_the_rest()
    {
        var data = new JsonObject { ["totalCount"] = 1204 };

        Assert.Equal("rows 1 to 250 of 1,204; add --skip 250 or --all for the rest", QueryOutput.Footer(data, 250, 0));
        Assert.Equal("rows 1,001 to 1,204 of 1,204", QueryOutput.Footer(data, 204, 1000));
        Assert.Equal("no rows of 1,204", QueryOutput.Footer(data, 0, 0));
    }
}
