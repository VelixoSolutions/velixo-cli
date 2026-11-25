using System.Text.RegularExpressions;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Removes the <c>conversationKey</c> the server adds to a call that sends none: a line that reads
/// <c>conversationKey: ck_…</c>, in a text block of its own or at the end of an error, and the line after it when that
/// line names <c>conversationKey</c>, as the server's instruction to send the key back does. Everything else in the
/// text stays. The key is for a client that sends it back, never output for the reader.
/// </summary>
public static partial class ConversationKey
{
    /// <summary>Returns <paramref name="text"/> without the key lines, trimmed, and whether it had any.</summary>
    public static (string Text, bool HadKey) Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var stripped = KeyLines().Replace(text, string.Empty);
        return ReferenceEquals(stripped, text) || stripped == text
            ? (text, false)
            : (stripped.Trim(), true);
    }

    [GeneratedRegex(@"^[ \t]*conversationKey:[ \t]*\S+[^\r\n]*(\r?\n[^\r\n]*conversationKey[^\r\n]*)?(\r?\n|\z)", RegexOptions.Multiline)]
    private static partial Regex KeyLines();
}
