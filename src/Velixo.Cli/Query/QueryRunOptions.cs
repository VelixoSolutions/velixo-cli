namespace Velixo.Cli.Query;

/// <summary>The window and paging flags of <c>velixo query run</c>.</summary>
public sealed record QueryRunOptions
{
    /// <summary>The <c>top</c> the CLI sends when <c>--top</c> is not given, as the tool guidance says to start.</summary>
    public const int DefaultTop = 250;

    /// <summary>The most rows <c>execute_query</c> serves in one call.</summary>
    public const int MaxTop = 10_000;

    public const int DefaultMaxRows = 10_000;

    public int Top { get; init; } = DefaultTop;

    public int Skip { get; init; }

    /// <summary>Gets a value indicating whether to walk every window from <see cref="Skip"/> up to <see cref="MaxRows"/>.</summary>
    public bool All { get; init; }

    public int MaxRows { get; init; } = DefaultMaxRows;
}
