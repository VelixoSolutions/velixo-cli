using System.Text;

namespace Velixo.Cli.Output;

/// <summary>
/// Terminal hyperlinks (OSC 8): <c>ESC ]8;;URL ESC \ text ESC ]8;; ESC \</c>. A terminal that supports
/// them shows only the text, which opens the URL on a click; a terminal that does not ignores the
/// sequences. They take no columns on screen, so every width here is measured on the text alone.
/// </summary>
public static class Hyperlink
{
    private const string Start = "\u001b]8;;";
    private const string Terminator = "\u001b\\";
    private const string Close = Start + Terminator;

    /// <summary>Returns <paramref name="text"/> as a link to <paramref name="url"/>, or as it is when either is empty or the URL is not a safe http(s) address.</summary>
    public static string Wrap(string text, string? url)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length == 0 || !IsSafe(url) ? text : $"{Start}{url}{Terminator}{text}{Close}";
    }

    /// <summary>
    /// Returns whether <paramref name="url"/> is an absolute http(s) URL with no control character, so it
    /// can sit inside an escape sequence without ending it early or writing anything to the terminal.
    /// </summary>
    public static bool IsSafe(string? url) =>
        !string.IsNullOrEmpty(url)
        && !url.Any(char.IsControl)
        && Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp);

    /// <summary>The columns <paramref name="line"/> takes on screen: its length without the link sequences.</summary>
    public static int VisibleLength(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var length = 0;
        for (var index = 0; index < line.Length;)
        {
            if (SequenceLength(line, index) is > 0 and var skip)
            {
                index += skip;
                continue;
            }

            length++;
            index++;
        }

        return length;
    }

    /// <summary>
    /// The part of <paramref name="line"/> that shows from column <paramref name="start"/> for at most
    /// <paramref name="width"/> columns. A link cut by either edge keeps its URL around the text left of it.
    /// </summary>
    public static string Slice(string line, int start, int width)
    {
        ArgumentNullException.ThrowIfNull(line);

        var text = new StringBuilder();
        string? open = null;
        var emitted = false;
        var column = 0;
        var end = start + width;
        for (var index = 0; index < line.Length && column < end;)
        {
            if (SequenceLength(line, index) is > 0 and var skip)
            {
                var url = line.Substring(index + Start.Length, skip - Start.Length - Terminator.Length);
                open = url.Length == 0 ? null : url;
                if (column >= start)
                {
                    text.Append(line, index, skip);
                    emitted = open is not null;
                }

                index += skip;
                continue;
            }

            if (column >= start)
            {
                if (open is not null && !emitted)
                {
                    text.Append(Start).Append(open).Append(Terminator);
                    emitted = true;
                }

                text.Append(line[index]);
            }

            column++;
            index++;
        }

        if (open is not null && emitted)
        {
            text.Append(Close);
        }

        return text.ToString();
    }

    /// <summary>The length of the link sequence that starts at <paramref name="index"/>, or 0 when none does.</summary>
    private static int SequenceLength(string line, int index)
    {
        if (string.CompareOrdinal(line, index, Start, 0, Start.Length) != 0)
        {
            return 0;
        }

        var terminator = line.IndexOf(Terminator, index + Start.Length, StringComparison.Ordinal);
        return terminator < 0 ? 0 : terminator + Terminator.Length - index;
    }
}
