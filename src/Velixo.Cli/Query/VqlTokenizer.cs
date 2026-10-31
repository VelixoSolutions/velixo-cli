using System.Text;

namespace Velixo.Cli.Query;

/// <summary>
/// Splits query text into the few kinds of token the CLI needs to read its declarations and its
/// <c>ORDER BY</c>: words, bracketed names, quoted strings and single symbols. Comments are skipped,
/// so a keyword inside a label or a comment is never read as a keyword.
/// </summary>
internal static class VqlTokenizer
{
    public static IReadOnlyList<VqlToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokens = new List<VqlToken>();
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];

            if (char.IsWhiteSpace(current))
            {
                index++;
            }
            else if (current == '-' && Peek(text, index + 1) == '-')
            {
                index = SkipPast(text, index, "\n");
            }
            else if (current == '/' && Peek(text, index + 1) == '*')
            {
                index = SkipPast(text, index + 2, "*/");
            }
            else if (current == '\'')
            {
                index = ReadQuoted(text, index, tokens);
            }
            else if (current == '[')
            {
                var end = text.IndexOf(']', index + 1);
                end = end < 0 ? text.Length : end;
                tokens.Add(new VqlToken(VqlTokenKind.Bracketed, text[(index + 1)..end]));
                index = end + 1;
            }
            else if (IsWordCharacter(current))
            {
                var start = index;
                while (index < text.Length && IsWordCharacter(text[index]))
                {
                    index++;
                }

                tokens.Add(new VqlToken(VqlTokenKind.Word, text[start..index]));
            }
            else
            {
                tokens.Add(new VqlToken(VqlTokenKind.Symbol, current.ToString()));
                index++;
            }
        }

        return tokens;
    }

    private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

    private static int SkipPast(string text, int start, string terminator)
    {
        var end = text.IndexOf(terminator, start, StringComparison.Ordinal);
        return end < 0 ? text.Length : end + terminator.Length;
    }

    /// <summary>Reads a quoted string, where a doubled quote stands for one quote inside it.</summary>
    private static int ReadQuoted(string text, int start, List<VqlToken> tokens)
    {
        var value = new StringBuilder();
        var index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '\'')
            {
                if (Peek(text, index + 1) != '\'')
                {
                    index++;
                    break;
                }

                index++;
            }

            value.Append(text[index]);
            index++;
        }

        tokens.Add(new VqlToken(VqlTokenKind.Quoted, value.ToString()));
        return index;
    }

    private static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '_' or '@' or '.';
}
