using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Velixo.Cli.Query;

/// <summary>
/// Matches the parameters given with <c>--param</c> and <c>--params</c> against what a run takes, and
/// words the warnings for what does not match. Acumatica discards a key that matches no label without
/// an error, and a REQUIRED parameter without a value then matches nothing, so both are warned about
/// on stderr before the call.
/// </summary>
public static partial class ParameterBinder
{
    /// <summary>Binds <paramref name="given"/> to the declarations of <paramref name="query"/>.</summary>
    /// <param name="onlyDeclared">
    /// Send only the keys the query declares. A plan runs several queries with one set of values, and
    /// Sage Intacct and Business Central refuse a key their query does not take.
    /// </param>
    public static BoundParameters ForQuery(string query, JsonObject given, bool onlyDeclared)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(given);

        IReadOnlyList<DeclaredParameter> declarations = QueryDeclarations.Read(query);
        var known = declarations.Select(declaration => declaration.Key).Distinct(StringComparer.Ordinal).ToList();
        var accepted = given.Select(pair => pair.Key).Where(key => known.Contains(key, StringComparer.Ordinal)).ToHashSet(StringComparer.Ordinal);

        var send = new JsonObject();
        foreach ((var key, JsonNode? value) in given)
        {
            if (!onlyDeclared || accepted.Contains(key))
            {
                send[key] = value?.DeepClone();
            }
        }

        var missing = declarations
            .Where(declaration => declaration.NeedsValue && given[declaration.Key] is null)
            .Select(declaration => $"The query requires '{declaration.Key}' and no --param or --params gives it; without a value it matches nothing.")
            .ToList();

        return new BoundParameters { Send = send, Accepted = accepted, Known = known, Missing = missing };
    }

    /// <summary>
    /// Binds <paramref name="given"/> to a plan item that runs a saved inquiry. A value given under the
    /// inquiry parameter's own label replaces the plan's; otherwise each <c>{{Placeholder}}</c> in the
    /// plan's value takes the value given under the placeholder's name. A parameter whose placeholder
    /// has no value is left out, so the inquiry falls back to its own default.
    /// </summary>
    public static BoundParameters ForInquiry(JsonObject? planParameters, JsonObject given)
    {
        ArgumentNullException.ThrowIfNull(given);

        var send = new JsonObject();
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        var known = new List<string>();
        var missing = new List<string>();

        foreach ((var label, JsonNode? value) in planParameters ?? [])
        {
            known.Add(label);
            if (given.ContainsKey(label))
            {
                send[label] = given[label]?.DeepClone();
                accepted.Add(label);
                continue;
            }

            if (value is not JsonValue text || !text.TryGetValue(out string? template) || !Placeholder().IsMatch(template))
            {
                send[label] = value?.DeepClone();
                continue;
            }

            var unbound = new List<string>();
            var bound = Placeholder().Replace(template, match =>
            {
                var name = match.Groups["name"].Value;
                known.Add(name);
                if (given[name] is { } supplied)
                {
                    accepted.Add(name);
                    return supplied is JsonValue scalar && scalar.TryGetValue(out string? plain) ? plain : supplied.ToJsonString();
                }

                unbound.Add(name);
                return match.Value;
            });

            if (unbound.Count == 0)
            {
                send[label] = bound;
            }
            else
            {
                missing.AddRange(unbound.Select(name =>
                    $"The inquiry parameter '{label}' needs {{{{{name}}}}}; pass --param \"{name}=VALUE\". It is left out, so the inquiry uses its default."));
            }
        }

        return new BoundParameters
        {
            Send = send,
            Accepted = accepted,
            Known = known.Distinct(StringComparer.Ordinal).ToList(),
            Missing = missing,
        };
    }

    /// <summary>A run of a saved inquiry by name: the CLI cannot see its parameters, so it sends what it was given.</summary>
    public static BoundParameters Unchecked(JsonObject given)
    {
        ArgumentNullException.ThrowIfNull(given);

        return new BoundParameters
        {
            Send = (JsonObject)given.DeepClone(),
            Accepted = given.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal),
            Known = [],
            Missing = [],
            IsChecked = false,
        };
    }

    /// <summary>
    /// Words a warning for every given key that no run takes, and for every parameter a run needs and
    /// was not given. <paramref name="runs"/> pairs each binding with the title of its plan item, or
    /// null for a single query.
    /// </summary>
    public static IReadOnlyList<string> Warnings(JsonObject given, IReadOnlyList<(string? Title, BoundParameters Bound)> runs)
    {
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(runs);

        var warnings = new List<string>();
        if (runs.Any(run => !run.Bound.IsChecked))
        {
            return warnings;
        }

        var known = runs.SelectMany(run => run.Bound.Known).Distinct(StringComparer.Ordinal).ToList();
        foreach (var key in given.Select(pair => pair.Key).Where(key => !runs.Any(run => run.Bound.Accepted.Contains(key))))
        {
            warnings.Add(DescribeUnknown(key, known, runs.Count > 1));
        }

        foreach ((string? title, BoundParameters bound) in runs)
        {
            warnings.AddRange(bound.Missing.Select(sentence => title is null ? sentence : $"Plan {title}: {sentence}"));
        }

        return warnings;
    }

    private static string DescribeUnknown(string key, IReadOnlyList<string> known, bool isPlan)
    {
        var subject = isPlan ? "no query of the plan" : "the query";
        if (known.Count == 0)
        {
            return $"The parameter '{key}' is not used: {subject} declares no parameters.";
        }

        var takes = string.Join(", ", known.Select(label => $"'{label}'"));
        var similar = known.FirstOrDefault(label => string.Equals(label, key, StringComparison.OrdinalIgnoreCase));
        var hint = similar is null ? string.Empty : $" Did you mean '{similar}'?";
        return $"The parameter '{key}' matches no parameter {(isPlan ? "any query of the plan" : "the query")} takes ({takes}). "
            + $"Acumatica discards an unknown key without an error, so the value is not used.{hint}";
    }

    [GeneratedRegex(@"\{\{\s*(?<name>[^{}]+?)\s*\}\}")]
    private static partial Regex Placeholder();
}
