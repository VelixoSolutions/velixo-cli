using System.Text.RegularExpressions;

namespace Velixo.Cli.Mcp;

/// <summary>
/// Reads and removes the <c>conversationKey</c> the server adds to a call that arrived without a valid one: a text
/// block, or the end of an error message, that reads <c>conversationKey: ck_…</c> and asks to send the key back.
/// The key is for the CLI to send back, never output for the reader.
/// </summary>
public static partial class ConversationKey
{
    /// <summary>The argument that names the chat in the server's audit.</summary>
    public const string NameArgument = "conversationName";

    /// <summary>The argument that ties the calls of one chat together in the server's audit.</summary>
    public const string KeyArgument = "conversationKey";

    /// <summary>Returns the key when <paramref name="text"/> is a whole key block, as the server adds to a result.</summary>
    public static bool TryRead(string text, out string key)
    {
        ArgumentNullException.ThrowIfNull(text);

        Match match = Block().Match(text);
        key = match.Success ? match.Groups["key"].Value : string.Empty;
        return match.Success;
    }

    /// <summary>Returns <paramref name="message"/> without the key lines the server adds to an error message.</summary>
    public static string StripFrom(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Lines().Replace(message, string.Empty).TrimEnd();
    }

    [GeneratedRegex(@"\A\s*conversationKey:[ \t]*(?<key>\S+)[ \t]*(\r?\n[^\r\n]*conversationKey[^\r\n]*)?\s*\z")]
    private static partial Regex Block();

    [GeneratedRegex(@"(\r?\n)?^conversationKey:[ \t]*\S+[ \t]*(\r?\n[^\r\n]*conversationKey[^\r\n]*)?", RegexOptions.Multiline)]
    private static partial Regex Lines();
}
