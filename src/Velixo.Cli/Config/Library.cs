using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Velixo.Cli.Config;

/// <summary>The two kinds of built artefact the library keeps: query plans and screen batches.</summary>
public enum LibraryKind
{
    Query,
    Screen,
}

/// <summary>
/// One query plan or screen batch a build produced, kept so it can be run again by its number. The
/// document is the build's whole output, the same one <c>--save</c> writes.
/// </summary>
public sealed class LibraryEntry
{
    /// <summary>Gets or sets the entry's number: unique among the entries of its kind, and never reused.</summary>
    [JsonPropertyName("number")]
    public int Number { get; set; }

    /// <summary>Gets or sets the name the build gave it with <c>--name</c>, unique among the entries of its kind; null when it has none.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>Gets how a command refers to the entry: its name when it has one, its number otherwise.</summary>
    [JsonIgnore]
    public string Reference => Name ?? Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gets or sets what the entry does, in a line: the plan's purposes, or the batch's screen and request.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the request the build was given, in the user's words.</summary>
    [JsonPropertyName("request")]
    public string? Request { get; set; }

    /// <summary>Gets or sets the connection the build ran against, which a run from the library uses unless told otherwise.</summary>
    [JsonPropertyName("connection")]
    public string? Connection { get; set; }

    [JsonPropertyName("builtAt")]
    public DateTimeOffset BuiltAt { get; set; }

    [JsonPropertyName("document")]
    public JsonObject Document { get; set; } = [];
}

/// <summary>
/// <c>library/&lt;profile&gt;.json</c>: the query plans and screen batches the profile built. It lives
/// beside the state files rather than in them, because <c>velixo logout</c> deletes the state.
/// </summary>
public sealed class LibraryFile
{
    [JsonPropertyName("nextQuery")]
    public int NextQuery { get; set; } = 1;

    [JsonPropertyName("nextScreen")]
    public int NextScreen { get; set; } = 1;

    [JsonPropertyName("queries")]
    public List<LibraryEntry> Queries { get; set; } = [];

    [JsonPropertyName("screens")]
    public List<LibraryEntry> Screens { get; set; } = [];

    public List<LibraryEntry> Entries(LibraryKind kind) => kind == LibraryKind.Query ? Queries : Screens;

    /// <summary>
    /// Adds <paramref name="entry"/> under the next number of its kind and returns the entry that had its
    /// name before, which loses it, or null. A name names one entry of a kind, and the newest build takes it.
    /// </summary>
    public LibraryEntry? Add(LibraryKind kind, LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        LibraryEntry? previous = entry.Name is null ? null : FindByName(kind, entry.Name);
        if (previous is not null)
        {
            previous.Name = null;
        }

        entry.Number = kind == LibraryKind.Query ? NextQuery++ : NextScreen++;
        Entries(kind).Add(entry);
        return previous;
    }

    /// <summary>Returns the entry <paramref name="reference"/> names: a number, or a name, whatever its case.</summary>
    public LibraryEntry? Find(LibraryKind kind, string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return int.TryParse(reference, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? Entries(kind).FirstOrDefault(entry => entry.Number == number)
            : FindByName(kind, reference);
    }

    private LibraryEntry? FindByName(LibraryKind kind, string name) =>
        Entries(kind).FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
}
