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

    /// <summary>
    /// Returns <paramref name="message"/> without the key the server adds to the end of an error message, and the
    /// key, or null when it carries none.
    /// </summary>
    public static (string Message, string? Key) StripFrom(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Match match = Tail().Match(message);
        return match.Success
            ? (message[..match.Index].TrimEnd(), match.Groups["key"].Value)
            : (message, null);
    }

    // A block is the server's whenever its first line is the key, whatever the instruction after it says.
    [GeneratedRegex(@"\A\s*conversationKey:[ \t]*(?<key>\S+)[ \t]*(\r?\n|\z)")]
    private static partial Regex Block();

    // The server appends the key to an error message as its last lines.
    [GeneratedRegex(@"(\r?\n|\A)conversationKey:[ \t]*(?<key>\S+)[^\r\n]*(\r?\n[\s\S]*)?\z")]
    private static partial Regex Tail();
}
