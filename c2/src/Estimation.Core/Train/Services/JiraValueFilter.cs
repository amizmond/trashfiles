namespace Estimation.Core.Train.Services;

public sealed class JiraValueFilter
{
    public const string EmptyToken = "(empty)";

    public const char NotPrefix = '!';

    public static readonly JiraValueFilter Any = new([], false, [], false);

    private static readonly IReadOnlySet<string> NoValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private JiraValueFilter(IReadOnlyList<string> included, bool includesEmpty, IReadOnlyList<string> excluded, bool excludesEmpty)
    {
        IncludedValues = included;
        Included = new HashSet<string>(included, StringComparer.OrdinalIgnoreCase);
        IncludesEmpty = includesEmpty;
        ExcludedValues = excluded;
        Excluded = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        ExcludesEmpty = excludesEmpty;
        Tokens = new HashSet<string>(ToTokens(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> IncludedValues { get; }

    public IReadOnlySet<string> Included { get; }

    public bool IncludesEmpty { get; }

    public IReadOnlyList<string> ExcludedValues { get; }

    public IReadOnlySet<string> Excluded { get; }

    public bool ExcludesEmpty { get; }

    public IReadOnlySet<string> Tokens { get; }

    public bool HasIncludes => Included.Count > 0 || IncludesEmpty;

    public bool HasExcludes => Excluded.Count > 0 || ExcludesEmpty;

    public bool IsActive => HasIncludes || HasExcludes;

    public bool IsNot => HasExcludes && !HasIncludes;

    public bool IsMixed => HasIncludes && HasExcludes;

    public IReadOnlyList<string> Values => IsNot ? ExcludedValues : IncludedValues;

    public bool HasEmpty => IsNot ? ExcludesEmpty : IncludesEmpty;

    public IEnumerable<string> Mentioned => IncludedValues.Concat(ExcludedValues);

    public static bool IsReserved(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && (trimmed[0] == NotPrefix || string.Equals(trimmed, EmptyToken, StringComparison.OrdinalIgnoreCase));
    }

    public static JiraValueFilter Parse(string? csv) =>
        string.IsNullOrWhiteSpace(csv) ? Any : Parse(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static JiraValueFilter Parse(IEnumerable<string> tokens)
    {
        var included = new List<string>();
        var excluded = new List<string>();
        bool includesEmpty = false, excludesEmpty = false;

        foreach (var token in tokens)
        {
            var trimmed = token?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            var not = trimmed[0] == NotPrefix;
            var value = not ? trimmed[1..].Trim() : trimmed;
            if (value.Length == 0)
            {
                continue;
            }

            if (string.Equals(value, EmptyToken, StringComparison.OrdinalIgnoreCase))
            {
                if (not)
                {
                    excludesEmpty = true;
                }
                else
                {
                    includesEmpty = true;
                }
                continue;
            }

            var list = not ? excluded : included;
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(value);
            }
        }

        return included.Count == 0 && excluded.Count == 0 && !includesEmpty && !excludesEmpty
            ? Any
            : new JiraValueFilter(included, includesEmpty, excluded, excludesEmpty);
    }

    public static JiraValueFilter Create(IEnumerable<string> values, bool withEmpty, bool not)
    {
        var tokens = values.Select(v => v?.Trim() ?? string.Empty).Where(v => v.Length > 0).ToList();
        if (withEmpty)
        {
            tokens.Add(EmptyToken);
        }

        return Parse(not ? tokens.Select(t => NotPrefix + t) : tokens);
    }

    public JiraValueFilter WithValues(IEnumerable<string> values) =>
        IsMixed ? this : Create(Values.Concat(values), HasEmpty, IsNot);

    public JiraValueFilter WithoutValue(string value) =>
        IsMixed ? this : Create(Values.Where(v => !string.Equals(v, value, StringComparison.OrdinalIgnoreCase)), HasEmpty, IsNot);

    public JiraValueFilter WithEmpty(bool withEmpty) =>
        IsMixed ? this : Create(Values, withEmpty, IsNot);

    public JiraValueFilter WithNot(bool not) =>
        IsMixed || !IsActive ? this : Create(Values, HasEmpty, not);

    public bool Accepts(IReadOnlySet<string> issueValues) =>
        (!HasIncludes || Hits(issueValues, Included, IncludesEmpty))
        && (!HasExcludes || !Hits(issueValues, Excluded, ExcludesEmpty));

    public bool AcceptsEmpty => Accepts(NoValues);

    public string? ToCsv() => Tokens.Count == 0 ? null : string.Join(",", ToTokens());

    public string? Problem(string plural)
    {
        if (IsMixed)
        {
            return $"The {plural} mix values with and without Not. Use Not for all of them or for none.";
        }

        var reserved = Mentioned.FirstOrDefault(v => v[0] == NotPrefix);
        return reserved is null
            ? null
            : $"The {plural} value \"{reserved}\" starts with '{NotPrefix}', which is reserved for Not.";
    }

    public string Describe()
    {
        var parts = new List<string>();
        if (HasIncludes)
        {
            var values = string.Join(", ", IncludedValues);
            parts.Add(IncludesEmpty
                ? IncludedValues.Count == 0 ? "empty" : $"{values} or empty"
                : values);
        }
        if (HasExcludes)
        {
            var values = ExcludedValues.Count switch
            {
                0 => null,
                1 => $"not {ExcludedValues[0]}",
                _ => $"none of {string.Join(", ", ExcludedValues)}"
            };
            parts.Add(ExcludesEmpty
                ? values is null ? "not empty" : $"{values} and not empty"
                : values!);
        }

        return string.Join(", ", parts);
    }

    public string Requirement(string singular, string plural)
    {
        var parts = new List<string>();
        if (HasIncludes)
        {
            var values = IncludedValues.Count switch
            {
                0 => null,
                1 => $"{singular} {IncludedValues[0]}",
                _ => $"one of the {plural} {string.Join(", ", IncludedValues)}"
            };
            parts.Add(IncludesEmpty
                ? values is null ? $"no {singular}" : $"{values} or no {singular}"
                : values!);
        }
        if (HasExcludes)
        {
            var values = ExcludedValues.Count switch
            {
                0 => null,
                1 => ExcludedValues[0],
                _ => $"none of {string.Join(", ", ExcludedValues)}"
            };
            parts.Add(ExcludesEmpty
                ? values is null ? $"a {singular}" : ExcludedValues.Count == 1 ? $"a {singular}, but not {values}" : $"a {singular}, but {values}"
                : ExcludedValues.Count == 1 ? $"no {singular} {values}" : $"none of the {plural} {string.Join(", ", ExcludedValues)}");
        }

        return string.Join(" and ", parts);
    }

    private static bool Hits(IReadOnlySet<string> issueValues, IReadOnlySet<string> values, bool empty) =>
        issueValues.Count == 0 ? empty : values.Overlaps(issueValues);

    private IEnumerable<string> ToTokens()
    {
        foreach (var value in IncludedValues)
        {
            yield return value;
        }
        if (IncludesEmpty)
        {
            yield return EmptyToken;
        }
        foreach (var value in ExcludedValues)
        {
            yield return NotPrefix + value;
        }
        if (ExcludesEmpty)
        {
            yield return NotPrefix + EmptyToken;
        }
    }
}

public sealed record JiraValueCount(string Value, int Count)
{
    public static IReadOnlyList<JiraValueCount> Tally(IEnumerable<string?> csvs) =>
        csvs
            .SelectMany(c => JiraListValues.Parse(c))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(g => new JiraValueCount(g.First(), g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed record JiraKeyValues(
    int Features,
    IReadOnlyList<JiraValueCount> Components,
    int WithoutComponents,
    IReadOnlyList<JiraValueCount> Labels,
    int WithoutLabels)
{
    public static JiraKeyValues From(IReadOnlyCollection<IssueMatchFacts> features) =>
        new(
            features.Count,
            JiraValueCount.Tally(features.Select(f => f.Components)),
            features.Count(f => JiraListValues.Parse(f.Components).Count == 0),
            JiraValueCount.Tally(features.Select(f => f.Labels)),
            features.Count(f => JiraListValues.Parse(f.Labels).Count == 0));
}
