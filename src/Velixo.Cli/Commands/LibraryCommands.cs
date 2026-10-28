using System.CommandLine;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Velixo.Cli.Config;
using Velixo.Cli.Output;
using Velixo.Cli.Query;
using Velixo.Cli.Screen;

namespace Velixo.Cli.Commands;

/// <summary>
/// The library of built query plans and screen batches: every build is kept under a number, so
/// <c>velixo query run 3</c> runs it again. <c>list</c> shows the library, <c>delete</c> removes an
/// entry, and a command given no number picks one from a list on a terminal.
/// </summary>
internal static partial class LibraryCommands
{
    /// <summary>The key the build's output carries the entry's number under.</summary>
    public const string NumberKey = "libraryNumber";

    /// <summary>The key the build's output carries the entry's name under, when it has one.</summary>
    public const string NameKey = "libraryName";

    /// <summary>The <c>--name</c> option of <c>query build</c> and <c>command build</c>.</summary>
    public static Option<string?> NameOption(LibraryKind kind) => new("--name")
    {
        Description = $"A name to run the {Noun(kind)} by instead of its number: letters, digits, - and _, starting with a letter. "
            + $"Example: velixo {Group(kind)} run {(kind == LibraryKind.Query ? "ar-aging" : "create-order")}",
        HelpName = "name",
    };

    /// <summary>Checks a <c>--name</c> before the build spends a call on it, and returns it, or null when none was given.</summary>
    /// <exception cref="CliException">Exit 2 for a name that is not a key.</exception>
    public static string? CheckName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        return NamePattern().IsMatch(name)
            ? name
            : throw CliException.Usage(
                $"'{name}' cannot be a name: use letters, digits, - and _, starting with a letter, with no spaces.",
                "For example --name ar-aging or --name create_order.");
    }

    /// <summary>
    /// Keeps <paramref name="document"/> in the profile's library under the next number, and under
    /// <paramref name="name"/> when one is given; adds both to the document; and returns how a command
    /// refers to the entry: its name, or its number.
    /// </summary>
    public static string Save(CliServices services, LibraryKind kind, JsonObject document, string request, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(document);

        var profile = services.Settings.ProfileName;
        LibraryFile library = services.Profiles.LoadLibrary(profile);
        var entry = new LibraryEntry
        {
            Name = name,
            Title = TitleOf(kind, document, request),
            Request = request,
            Connection = services.Settings.Connection,
            BuiltAt = services.Clock.GetUtcNow(),
            Document = (JsonObject)document.DeepClone(),
        };
        LibraryEntry? renamed = library.Add(kind, entry);
        services.Profiles.SaveLibrary(profile, library);

        document[NumberKey] = entry.Number;
        if (name is not null)
        {
            document[NameKey] = name;
        }

        if (renamed is not null)
        {
            services.Output.Progress($"The name '{name}' moved from {Noun(kind)} {renamed.Number} to this one; {Noun(kind)} {renamed.Number} stays in the list unnamed.");
        }

        services.Output.Progress(
            $"Saved as {Noun(kind)} {entry.Number}{(name is null ? string.Empty : $" '{name}'")}. Run it again with: velixo {Group(kind)} run {entry.Reference}");
        return entry.Reference;
    }

    public static Command BuildList(CliServices services, LibraryKind kind)
    {
        var command = new Command(
            "list",
            $"List the {Plural(kind)} built on this profile, by number. Example: velixo {Group(kind)} list");

        command.SetAction(_ =>
        {
            LibraryFile library = services.Profiles.LoadLibrary(services.Settings.ProfileName);
            services.Output.WriteResult(new JsonArray(library.Entries(kind)
                .Select(entry => (JsonNode?)new JsonObject
                {
                    ["number"] = entry.Number,
                    ["name"] = entry.Name,
                    ["title"] = entry.Title,
                    ["values"] = string.Join(", ", ValueNames(kind, entry.Document)),
                    ["connection"] = entry.Connection,
                    ["builtAt"] = entry.BuiltAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                })
                .ToArray()));
            return ExitCodes.Success;
        });

        return command;
    }

    public static Command BuildDelete(CliServices services, LibraryKind kind)
    {
        var number = NumberArgument(kind, "delete");
        var command = new Command(
            "delete",
            $"Delete a {Noun(kind)} from the list by its number or name, or pick it on a terminal. Example: velixo {Group(kind)} delete 3")
        {
            number,
        };

        command.SetAction(parse =>
        {
            var profile = services.Settings.ProfileName;
            LibraryFile library = services.Profiles.LoadLibrary(profile);
            LibraryEntry entry = Resolve(services, kind, library, parse.GetValue(number), "delete");
            library.Entries(kind).Remove(entry);
            services.Profiles.SaveLibrary(profile, library);

            services.Output.WriteResult(new JsonObject { ["deleted"] = entry.Number, ["name"] = entry.Name, ["title"] = entry.Title });
            return ExitCodes.Success;
        });

        return command;
    }

    /// <summary>The optional argument of <c>run</c>, <c>workbook</c> and <c>delete</c>: an entry's number or name.</summary>
    public static Argument<string?> NumberArgument(LibraryKind kind, string verb) => new("number-or-name")
    {
        Description = $"The {Noun(kind)}'s number or name in 'velixo {Group(kind)} list'. Without it, a terminal shows the list to pick from.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    /// <summary>
    /// Returns the entry to <paramref name="verb"/>: the one numbered <paramref name="number"/>, or the
    /// one a person picks from the list on a terminal. Its connection becomes the run's, unless
    /// <c>--connection</c> names another.
    /// </summary>
    public static LibraryEntry Find(CliServices services, LibraryKind kind, string? number, string verb, string? forcedConnection)
    {
        ArgumentNullException.ThrowIfNull(services);

        LibraryEntry entry = Resolve(services, kind, services.Profiles.LoadLibrary(services.Settings.ProfileName), number, verb);
        if (forcedConnection is null && entry.Connection is { } connection)
        {
            services.UseConnection(connection);
        }

        return entry;
    }

    /// <summary>The names of the values a run of the entry takes, as the list shows them.</summary>
    public static IReadOnlyList<string> ValueNames(LibraryKind kind, JsonObject document)
    {
        if (kind == LibraryKind.Query)
        {
            return TryReadPlan(document) is { } plan ? QueryOutput.PlanParameters(plan.Items).Select(parameter => parameter.Key).ToList() : [];
        }

        if (TryReadBatch(document) is not { } batch)
        {
            return [];
        }

        var header = ScreenPlaceholders.Names(batch.Commands).Concat(ScreenPlaceholders.Names(batch.FinishCommands));
        var lines = ScreenPlaceholders.Names(batch.LineCommands).Concat(ScreenPlaceholders.Names(batch.SplitCommands)).Select(name => $"{name} (lines)");
        return header.Concat(lines).Distinct(StringComparer.Ordinal).ToList();
    }

    private static LibraryEntry Resolve(CliServices services, LibraryKind kind, LibraryFile library, string? number, string verb)
    {
        List<LibraryEntry> entries = library.Entries(kind);
        if (!string.IsNullOrWhiteSpace(number))
        {
            return library.Find(kind, number.Trim())
                ?? throw CliException.Usage($"There is no {Noun(kind)} '{number}'.", $"Run 'velixo {Group(kind)} list' for the numbers and names.");
        }

        if (entries.Count == 0)
        {
            throw CliException.Usage(
                $"No {Plural(kind)} have been built on this profile yet.",
                $"Build one with 'velixo {Group(kind)} build \"...\"'.");
        }

        if (!ValuePrompt.CanAsk(services) || !services.Settings.IsInteractive)
        {
            throw CliException.Usage(
                $"Give the number or name of the {Noun(kind)} to {verb}: without a terminal there is no list to pick from.",
                $"Run 'velixo {Group(kind)} list', then 'velixo {Group(kind)} {verb} NUMBER'.");
        }

        var newestFirst = entries.OrderByDescending(entry => entry.Number).ToList();
        var width = newestFirst.Max(entry => entry.Number.ToString(CultureInfo.InvariantCulture).Length);
        var nameWidth = newestFirst.Max(entry => entry.Name?.Length ?? 0);
        var rows = newestFirst
            .Select(entry => $"{entry.Number.ToString(CultureInfo.InvariantCulture).PadLeft(width)}  "
                + (nameWidth == 0 ? string.Empty : (entry.Name ?? string.Empty).PadRight(nameWidth) + "  ")
                + entry.Title
                + (entry.Connection is null ? string.Empty : $"  ({entry.Connection})"))
            .ToList();
        var picked = Picker.Choose(services.Console, $"Which {Noun(kind)} to {verb}?", rows, 0, services.Settings.Output.UseColor);
        return picked is { } index
            ? newestFirst[index]
            : throw new CliException(ExitCodes.Interrupted, ErrorCodes.Interrupted, $"No {Noun(kind)} was chosen; nothing ran.");
    }

    private static string TitleOf(LibraryKind kind, JsonObject document, string request)
    {
        if (kind == LibraryKind.Query && TryReadPlan(document) is { } plan)
        {
            var purposes = plan.Items.Select(item => item.Purpose).Where(purpose => !string.IsNullOrWhiteSpace(purpose)).ToList();
            if (purposes.Count > 0)
            {
                return string.Join("; ", purposes!);
            }
        }

        if (kind == LibraryKind.Screen && TryReadBatch(document) is { } batch)
        {
            return $"{batch.Target}: {request}";
        }

        return request;
    }

    private static QueryPlan? TryReadPlan(JsonObject document)
    {
        try
        {
            return QueryPlan.Read(document, "the library");
        }
        catch (CliException)
        {
            return null;
        }
    }

    private static ScreenBatch? TryReadBatch(JsonObject document)
    {
        try
        {
            return ScreenBatch.From(document, "the library");
        }
        catch (CliException)
        {
            return null;
        }
    }

    public static string Group(LibraryKind kind) => kind == LibraryKind.Query ? "query" : "command";

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]{0,63}$")]
    private static partial Regex NamePattern();

    private static string Noun(LibraryKind kind) => kind == LibraryKind.Query ? "query" : "batch";

    private static string Plural(LibraryKind kind) => kind == LibraryKind.Query ? "queries" : "command batches";
}
