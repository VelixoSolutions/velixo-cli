using System.Text.Json.Nodes;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Screen;

namespace Velixo.Cli.Tests.Screen;

/// <summary>
/// Checks how <c>--param NAME=@query:REF</c> fills a batch's value with the text of a query in the
/// profile's list, so a batch can save a built query without the caller copying it out of the plan.
/// </summary>
public sealed class QueryReferencesTests
{
    private const string VendorQuery = "FROM [PX.Objects.AP.Vendor] As Vendor\nSELECT Vendor.AcctCD AS 'Vendor ID'";
    private const string CustomerQuery = "FROM [PX.Objects.AR.Customer] As Customer\nWHERE Customer.Balance > 0\nSELECT Customer.AcctCD";

    private readonly List<string> warnings = [];

    [Theory]
    [InlineData("@query:1")]
    [InlineData("@query:vendors")]
    [InlineData("@query:VENDORS")]
    [InlineData("@query: 1 ")]
    public void A_reference_by_number_or_name_is_the_query_text(string value)
    {
        Assert.Equal(VendorQuery, References().Resolve(value));
    }

    [Fact]
    public void A_plan_saved_with_the_servers_key_is_read_too()
    {
        Assert.Equal(CustomerQuery, References().Resolve("@query:3"));
    }

    [Fact]
    public void An_item_of_a_plan_with_several_queries_is_named_by_its_number()
    {
        Assert.Equal(CustomerQuery, References().Resolve("@query:2:2"));
    }

    [Theory]
    [InlineData("ACME")]
    [InlineData("")]
    [InlineData("user@query:1")]
    [InlineData("@queries")]
    public void Any_other_value_is_kept_and_the_library_is_not_read(string value)
    {
        Assert.Equal(value, Unreadable().Resolve(value));
    }

    [Fact]
    public void A_doubled_at_sign_is_the_literal_text()
    {
        Assert.Equal("@query:1", Unreadable().Resolve("@@query:1"));
    }

    [Theory]
    [InlineData("@query:9")]
    [InlineData("@query:")]
    [InlineData("@query:2")]
    [InlineData("@query:2:3")]
    [InlineData("@query:2:0")]
    [InlineData("@query:2:x")]
    [InlineData("@query:4")]
    [InlineData("@query:5")]
    public void A_reference_that_names_no_query_text_is_a_usage_error(string value)
    {
        CliException error = Assert.Throws<CliException>(() => References().Resolve(value));

        Assert.Equal(ExitCodes.Usage, error.ExitCode);
    }

    [Fact]
    public void An_entry_with_no_plan_points_to_rebuilding_or_deleting_it()
    {
        CliException error = Assert.Throws<CliException>(() => References().Resolve("@query:5"));

        Assert.Contains("velixo query delete 5", error.NextStep, StringComparison.Ordinal);
    }

    [Fact]
    public void A_query_built_on_another_connection_is_used_with_a_warning()
    {
        Assert.Equal(VendorQuery, References(runConnection: "sandbox").Resolve("@query:1"));

        Assert.Contains(warnings, warning => warning.Contains("'prod'", StringComparison.Ordinal) && warning.Contains("'sandbox'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("PROD")]
    [InlineData(null)]
    public void A_query_built_on_the_same_or_the_default_connection_is_used_without_a_warning(string? runConnection)
    {
        Assert.Equal(VendorQuery, References(runConnection).Resolve("@query:1"));

        Assert.Empty(warnings);
    }

    [Fact]
    public void The_library_is_read_once_however_many_values_name_a_query()
    {
        var reads = 0;
        var references = new QueryReferences(() => { reads++; return Library(); }, null, warnings.Add);

        references.Resolve("@query:1");
        references.Resolve("@query:3");

        Assert.Equal(1, reads);
    }

    [Fact]
    public void A_param_value_is_resolved_and_a_file_value_is_not()
    {
        using var directory = new Fakes.TempDirectory();
        var file = Path.Combine(directory.Path, "vars.json");
        File.WriteAllText(file, """{"FromFile":"@query:1"}""");

        JsonObject variables = ScreenVariables.Bind(file, ["Giql=@query:1", "Title=Vendors"], [], References().Resolve);

        Assert.Equal(VendorQuery, (string?)variables["Giql"]);
        Assert.Equal("Vendors", (string?)variables["Title"]);
        Assert.Equal("@query:1", (string?)variables["FromFile"]);
    }

    [Fact]
    public void A_name_given_with_both_flags_is_refused_before_its_reference_is_resolved()
    {
        CliException error = Assert.Throws<CliException>(
            () => ScreenVariables.Bind(null, ["Giql=@query:99"], ["Giql"], Unreadable().Resolve));

        Assert.Contains("both with --param and with --param-null", error.Message, StringComparison.Ordinal);
    }

    private QueryReferences References(string? runConnection = null) => new(Library, runConnection, warnings.Add);

    private QueryReferences Unreadable() =>
        new(() => throw new InvalidOperationException("The library was read."), null, warnings.Add);

    private static LibraryFile Library()
    {
        var library = new LibraryFile();
        library.Add(LibraryKind.Query, Entry("vendors", Plan(Item("query", VendorQuery))));
        library.Add(LibraryKind.Query, Entry(null, Plan(Item("query", VendorQuery), Item("query", CustomerQuery))));
        library.Add(LibraryKind.Query, Entry(null, Plan(Item("giql", CustomerQuery))));
        library.Add(LibraryKind.Query, Entry(null, Plan(new JsonObject { ["inquiry"] = new JsonObject { ["name"] = "AR Aging" } })));
        library.Add(LibraryKind.Query, Entry(null, []));
        return library;
    }

    private static LibraryEntry Entry(string? name, JsonObject document) =>
        new() { Name = name, Document = document, Connection = "prod" };

    private static JsonObject Plan(params JsonObject[] items) =>
        new() { ["result"] = new JsonObject { ["data"] = new JsonObject { ["queries"] = new JsonArray(items) } } };

    private static JsonObject Item(string key, string query) => new() { ["purpose"] = "test", [key] = query };
}
