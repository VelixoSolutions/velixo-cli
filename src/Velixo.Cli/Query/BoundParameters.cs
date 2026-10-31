using System.Text.Json.Nodes;

namespace Velixo.Cli.Query;

/// <summary>The parameters one run sends, and what the CLI learned binding them, for its warnings.</summary>
public sealed class BoundParameters
{
    /// <summary>Gets the <c>parameters</c> object the run sends.</summary>
    required public JsonObject Send { get; init; }

    /// <summary>Gets the given keys this run takes.</summary>
    required public IReadOnlySet<string> Accepted { get; init; }

    /// <summary>Gets every key this run can take, for the message about a key it cannot.</summary>
    required public IReadOnlyList<string> Known { get; init; }

    /// <summary>Gets one sentence per parameter that needs a value and has none.</summary>
    required public IReadOnlyList<string> Missing { get; init; }

    /// <summary>Gets a value indicating whether the CLI could see what the run takes; false for a saved inquiry run by name.</summary>
    public bool IsChecked { get; init; } = true;
}
