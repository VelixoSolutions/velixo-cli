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
    /// <summary>The argument that names the conversation in the server's audit.</summary>
    public const string NameArgument = "conversationName";

    /// <summary>The argument that ties the calls of one conversation together in the server's audit.</summary>
    public const string KeyArgument = "conversationKey";

    /// <summary>Returns <paramref name="text"/> without the key lines, trimmed, whether it had any, and the last key it held.</summary>
    public static (string Text, bool HadKey, string? Key) Strip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string? key = null;
        var stripped = KeyLines().Replace(text, match =>
        {
            key = match.Groups["key"].Value;
            return string.Empty;
        });
        return key is null ? (text, false, null) : (stripped.Trim(), true, key);
    }

    [GeneratedRegex(@"^[ \t]*conversationKey:[ \t]*(?<key>\S+)[^\r\n]*(\r?\n[^\r\n]*conversationKey[^\r\n]*)?(\r?\n|\z)", RegexOptions.Multiline)]
    private static partial Regex KeyLines();
}
