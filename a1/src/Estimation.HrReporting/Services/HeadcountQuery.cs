using System.Globalization;
using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Services;

public sealed class HeadcountColumnFilter
{
    public List<string> Values { get; set; } = [];

    public bool Not { get; set; }

    public bool IsActive => Values.Count > 0 || Not;
}

public sealed class HeadcountFilter
{
    public string? Search { get; set; }

    public Dictionary<HeadcountColumn, HeadcountColumnFilter> Columns { get; } = new();

    public int ActiveCount =>
        (string.IsNullOrWhiteSpace(Search) ? 0 : 1) + Columns.Values.Count(f => f.IsActive);

    public HeadcountColumnFilter For(HeadcountColumn column)
    {
        if (!Columns.TryGetValue(column, out var filter))
        {
            filter = new HeadcountColumnFilter();
            Columns[column] = filter;
        }

        return filter;
    }

    public void Clear()
    {
        Search = null;
        Columns.Clear();
    }
}

public sealed record HeadcountTotals(int People, decimal Hc, decimal Fte)
{
    public static readonly HeadcountTotals Empty = new(0, 0, 0);
}

public static class HeadcountQuery
{
    private static readonly IComparer<object?> ValueComparer = Comparer<object?>.Create(CompareValues);

    public static List<HeadcountRow> Apply(IEnumerable<HeadcountRow> rows, HeadcountFilter filter)
    {
        var tokens = SearchTokens(filter.Search);
        var active = ActiveFilters(filter);

        return rows
            .Where(r => MatchesSearch(r, tokens) && active.All(f => Matches(r, f.Column, f.Filter)))
            .ToList();
    }

    public static bool Matches(HeadcountRow row, HeadcountColumn column, HeadcountColumnFilter filter)
    {
        var value = column.Format(row);

        if (filter.Values.Count == 0)
        {
            return !filter.Not || value is null;
        }

        var contains = value is not null && filter.Values.Contains(value, StringComparer.OrdinalIgnoreCase);
        return filter.Not ? !contains : contains;
    }

    public static IEnumerable<HeadcountRow> Sort(IEnumerable<HeadcountRow> rows, HeadcountColumn? column, bool descending)
    {
        column ??= HeadcountColumns.EmployeeName;

        var keyed = rows
            .Select(r => (Row: r, Value: column.GetValue(r)))
            .OrderBy(k => k.Value is null);

        keyed = descending
            ? keyed.ThenByDescending(k => k.Value, ValueComparer)
            : keyed.ThenBy(k => k.Value, ValueComparer);

        return keyed
            .ThenBy(k => k.Row.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(k => k.Row.Brid, StringComparer.OrdinalIgnoreCase)
            .Select(k => k.Row);
    }

    public static Dictionary<HeadcountColumn, Dictionary<string, int>> Facets(
        IEnumerable<HeadcountRow> rows, HeadcountFilter filter)
    {
        var tokens = SearchTokens(filter.Search);
        var active = ActiveFilters(filter);
        var facets = HeadcountColumns.Filterable.ToDictionary(
            c => c,
            _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        foreach (var row in rows)
        {
            if (!MatchesSearch(row, tokens))
            {
                continue;
            }

            HeadcountColumn? failed = null;
            var failures = 0;
            foreach (var (column, columnFilter) in active)
            {
                if (Matches(row, column, columnFilter))
                {
                    continue;
                }

                failed = column;
                if (++failures > 1)
                {
                    break;
                }
            }

            if (failures > 1)
            {
                continue;
            }

            foreach (var (column, counts) in facets)
            {
                if (failures == 1 && column != failed)
                {
                    continue;
                }

                var value = column.Format(row);
                if (value is not null)
                {
                    counts[value] = counts.GetValueOrDefault(value) + 1;
                }
            }
        }

        foreach (var (column, columnFilter) in filter.Columns)
        {
            if (!facets.TryGetValue(column, out var counts))
            {
                continue;
            }

            foreach (var selected in columnFilter.Values)
            {
                counts.TryAdd(selected, 0);
            }
        }

        return facets;
    }

    public static List<string> OrderedValues(HeadcountColumn column, IEnumerable<string> values)
    {
        if (column.Kind == HeadcountValueKind.Number)
        {
            return values
                .OrderBy(v => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : decimal.MaxValue)
                .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return values.OrderBy(v => (object)v, ValueComparer).ToList();
    }

    public static HeadcountTotals Totals(IEnumerable<HeadcountRow> rows)
    {
        var people = 0;
        decimal hc = 0;
        decimal fte = 0;

        foreach (var row in rows)
        {
            people++;
            hc += row.Hc ?? 0;
            fte += row.Fte ?? 0;
        }

        return new HeadcountTotals(people, hc, fte);
    }

    private static List<(HeadcountColumn Column, HeadcountColumnFilter Filter)> ActiveFilters(HeadcountFilter filter) =>
        filter.Columns
            .Where(f => f.Value.IsActive)
            .Select(f => (f.Key, f.Value))
            .ToList();

    private static string[] SearchTokens(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? []
            : search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchesSearch(HeadcountRow row, string[] tokens)
    {
        foreach (var token in tokens)
        {
            var found = false;
            foreach (var column in HeadcountColumns.SearchColumns)
            {
                if (column.Format(row)?.Contains(token, StringComparison.OrdinalIgnoreCase) == true)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareValues(object? x, object? y)
    {
        switch (x, y)
        {
            case (null, null):
                return 0;
            case (null, _):
                return 1;
            case (_, null):
                return -1;
            case (decimal a, decimal b):
                return a.CompareTo(b);
            case (DateTime a, DateTime b):
                return a.CompareTo(b);
            case (string a, string b):
                if (decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var na)
                    && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var nb))
                {
                    var byNumber = na.CompareTo(nb);
                    if (byNumber != 0)
                    {
                        return byNumber;
                    }
                }

                return StringComparer.OrdinalIgnoreCase.Compare(a, b);
            default:
                return 0;
        }
    }
}
