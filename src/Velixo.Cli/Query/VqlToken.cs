namespace Velixo.Cli.Query;

internal enum VqlTokenKind
{
    Word,
    Bracketed,
    Quoted,
    Symbol,
}

internal readonly record struct VqlToken(VqlTokenKind Kind, string Text)
{
    public bool IsWord(string keyword) =>
        Kind == VqlTokenKind.Word && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);
}
