using System.Globalization;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Query;

namespace Velixo.Cli.Screen;

/// <summary>
/// Resolves a <c>--param</c> value that names a query in the profile's list, such as
/// <c>--param Giql=@query:3</c>, to that query's text, so a batch can save a query built with
/// <c>velixo query build</c> without the caller copying the text out of the plan.
/// <para>
/// <c>@query:REF</c> takes the query of entry REF, a number or a name as <c>velixo query list</c> shows it.
/// A plan with several queries needs <c>@query:REF:ITEM</c>, ITEM counted from 1. A value that starts with
/// <c>@@query:</c> is the literal text with one <c>@</c> removed, and every other value is left as it is.
/// </para>
/// </summary>
internal static class QueryReferences
{
    private const string Prefix = "@query:";
    private const string EscapedPrefix = "@" + Prefix;

    /// <summary>
    /// Returns <paramref name="value"/>, or the query text it names. <paramref name="loadLibrary"/> is called
    /// only for a value that names a query.
    /// </summary>
    /// <exception cref="CliException">Exit 2 when the entry or the item does not exist, or holds no query text.</exception>
    public static string Resolve(string value, Func<LibraryFile> loadLibrary)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(loadLibrary);

        if (value.StartsWith(EscapedPrefix, StringComparison.Ordinal))
        {
            return value[1..];
        }

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return value;
        }

        (var reference, var itemNumber) = Parse(value);

        LibraryEntry entry = loadLibrary().Find(LibraryKind.Query, reference)
            ?? throw CliException.Usage(
                $"'{value}' names query '{reference}', which is not in this profile's list.",
                "See the queries by number and name with: velixo query list");

        QueryPlan plan = QueryPlan.Read(entry.Document, $"query {entry.Reference}");
        QueryPlanItem item = PickItem(value, entry, plan, itemNumber);

        return item.Query
            ?? throw CliException.Usage(
                $"'{value}' names {item.Title} of query {entry.Reference}, which runs the saved inquiry '{item.InquiryName}' and has no query text.",
                "Build the query again asking for a query over the inquiry, then name that one.");
    }

    private static (string Reference, int? ItemNumber) Parse(string value)
    {
        var rest = value[Prefix.Length..];
        var separator = rest.LastIndexOf(':');

        if (separator < 0)
        {
            return (Required(value, rest), null);
        }

        var item = rest[(separator + 1)..];
        if (!int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var itemNumber) || itemNumber < 1)
        {
            throw CliException.Usage(
                $"'{value}' ends in '{item}', which is not an item number: write @query:REF:ITEM, ITEM counted from 1.");
        }

        return (Required(value, rest[..separator]), itemNumber);
    }

    private static string Required(string value, string reference) =>
        string.IsNullOrWhiteSpace(reference)
            ? throw CliException.Usage($"'{value}' names no query: write @query:REF, REF a number or name from velixo query list.")
            : reference;

    private static QueryPlanItem PickItem(string value, LibraryEntry entry, QueryPlan plan, int? itemNumber)
    {
        if (itemNumber is null)
        {
            return plan.Items.Count == 1
                ? plan.Items[0]
                : throw CliException.Usage(
                    $"'{value}' names query {entry.Reference}, whose plan holds {plan.Items.Count} queries.",
                    $"Name one with @query:{entry.Reference}:ITEM, ITEM from 1 to {plan.Items.Count}.");
        }

        return itemNumber <= plan.Items.Count
            ? plan.Items[itemNumber.Value - 1]
            : throw CliException.Usage(
                $"'{value}' names item {itemNumber} of query {entry.Reference}, whose plan holds {plan.Items.Count}.");
    }
}
