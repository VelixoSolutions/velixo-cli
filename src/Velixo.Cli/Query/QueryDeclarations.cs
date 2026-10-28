namespace Velixo.Cli.Query;

/// <summary>
/// Reads what the CLI can see in a query's text before it runs it: the parameters its <c>DECLARE</c>
/// lines take, and whether it sorts its rows.
/// </summary>
public static class QueryDeclarations
{
    /// <summary>
    /// Returns every declaration of <paramref name="query"/>, in the grammar
    /// <c>DECLARE [Name] (SCHEMA … | TYPE …) [DEFAULT value] [AS 'Label'] [REQUIRED] [HIDDEN]</c>.
    /// </summary>
    public static IReadOnlyList<DeclaredParameter> Read(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<VqlToken> tokens = VqlTokenizer.Tokenize(query);
        var declarations = new List<DeclaredParameter>();

        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            VqlToken name = tokens[index + 1];
            if (!tokens[index].IsWord("DECLARE") || name.Kind is not (VqlTokenKind.Word or VqlTokenKind.Bracketed))
            {
                continue;
            }

            string? label = null;
            var required = false;
            var hasDefault = false;
            var clause = index + 2;
            for (; clause < tokens.Count && !EndsDeclaration(tokens[clause]); clause++)
            {
                if (tokens[clause].IsWord("AS") && clause + 1 < tokens.Count && tokens[clause + 1].Kind == VqlTokenKind.Quoted)
                {
                    label = tokens[clause + 1].Text;
                    clause++;
                }
                else if (tokens[clause].IsWord("REQUIRED"))
                {
                    required = true;
                }
                else if (tokens[clause].IsWord("DEFAULT"))
                {
                    hasDefault = true;
                }
            }

            declarations.Add(new DeclaredParameter(name.Text, label, required, hasDefault));
            index = clause - 1;
        }

        return declarations;
    }

    /// <summary>Returns whether <paramref name="query"/> has an <c>ORDER BY</c> outside its labels and comments.</summary>
    public static bool HasOrderBy(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<VqlToken> tokens = VqlTokenizer.Tokenize(query);
        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].IsWord("ORDER") && tokens[index + 1].IsWord("BY"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EndsDeclaration(VqlToken token) =>
        token.IsWord("DECLARE") || token.IsWord("FROM") || token.IsWord("SELECT") || token.IsWord("OPTIONS");
}
