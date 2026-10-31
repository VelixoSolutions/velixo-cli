namespace Velixo.Cli.Query;

/// <summary>One <c>DECLARE</c> of a query, as the CLI reads it to check the parameters it is given.</summary>
/// <param name="Name">The declared name, without its brackets.</param>
/// <param name="Label">The display label after <c>AS</c>, or null when the declaration has none.</param>
public sealed record DeclaredParameter(string Name, string? Label, bool IsRequired, bool HasDefault)
{
    /// <summary>
    /// Gets the key <c>execute_query</c> matches a value against: the display label, or the declared
    /// name for a declaration without one.
    /// </summary>
    public string Key => Label ?? Name;

    /// <summary>Gets a value indicating whether a run without a value for this parameter finds nothing.</summary>
    public bool NeedsValue => IsRequired && !HasDefault;
}
