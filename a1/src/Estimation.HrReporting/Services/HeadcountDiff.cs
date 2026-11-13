using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Services;

public enum HeadcountChangeKind
{
    Changed,
    Joiner,
    Leaver,
    Unchanged
}

public sealed record HeadcountFieldChange(HeadcountColumn Column, string? OldValue, string? NewValue);

public sealed class HeadcountDiffRow
{
    public HeadcountDiffRow(
        HeadcountChangeKind kind, HeadcountRow? a, HeadcountRow? b, IReadOnlyList<HeadcountFieldChange> changes)
    {
        Kind = kind;
        A = a;
        B = b;
        Changes = changes;
    }

    public HeadcountChangeKind Kind { get; }

    public HeadcountRow? A { get; }

    public HeadcountRow? B { get; }

    public IReadOnlyList<HeadcountFieldChange> Changes { get; }

    public HeadcountRow Current => B ?? A!;

    public string Brid => Current.Brid;

    public bool HasChange(HeadcountColumn column) => Changes.Any(c => c.Column == column);
}

public sealed class HeadcountDiffResult
{
    public HeadcountDiffResult(IReadOnlyList<HeadcountDiffRow> rows, IReadOnlyCollection<HeadcountColumn> comparedColumns)
    {
        Rows = rows;
        ComparedColumns = comparedColumns;
        Counts = Enum.GetValues<HeadcountChangeKind>().ToDictionary(k => k, k => rows.Count(r => r.Kind == k));
        ChangesByColumn = rows
            .SelectMany(r => r.Changes)
            .GroupBy(c => c.Column)
            .OrderBy(g => IndexOf(g.Key))
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public IReadOnlyList<HeadcountDiffRow> Rows { get; }

    public IReadOnlyCollection<HeadcountColumn> ComparedColumns { get; }

    public IReadOnlyDictionary<HeadcountChangeKind, int> Counts { get; }

    public IReadOnlyDictionary<HeadcountColumn, int> ChangesByColumn { get; }

    public int Count(HeadcountChangeKind kind) => Counts[kind];

    public bool HasDifferences => Rows.Any(r => r.Kind != HeadcountChangeKind.Unchanged);

    private static int IndexOf(HeadcountColumn column)
    {
        for (var i = 0; i < HeadcountColumns.All.Count; i++)
        {
            if (HeadcountColumns.All[i] == column)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}

public static class HeadcountDiff
{
    public static IReadOnlyList<HeadcountColumn> DefaultCompared { get; } =
        HeadcountColumns.Comparable.Except(HeadcountColumns.IgnoredByDefault).ToList();

    public static HeadcountDiffResult Compare(
        IEnumerable<HeadcountRow> sideA,
        IEnumerable<HeadcountRow> sideB,
        IReadOnlyCollection<HeadcountColumn>? compared = null)
    {
        var columns = (compared ?? DefaultCompared)
            .Where(c => c != HeadcountColumns.Brid)
            .Distinct()
            .ToList();

        var byBridA = new Dictionary<string, HeadcountRow>(StringComparer.OrdinalIgnoreCase);
        var orderA = new List<HeadcountRow>();
        foreach (var row in sideA)
        {
            if (byBridA.TryAdd(row.Brid, row))
            {
                orderA.Add(row);
            }
        }

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<HeadcountDiffRow>();

        foreach (var b in sideB)
        {
            if (!matched.Add(b.Brid))
            {
                continue;
            }

            if (!byBridA.TryGetValue(b.Brid, out var a))
            {
                rows.Add(new HeadcountDiffRow(HeadcountChangeKind.Joiner, null, b, []));
                continue;
            }

            var changes = DiffFields(a, b, columns);
            rows.Add(new HeadcountDiffRow(
                changes.Count == 0 ? HeadcountChangeKind.Unchanged : HeadcountChangeKind.Changed,
                a,
                b,
                changes));
        }

        foreach (var a in orderA.Where(a => !matched.Contains(a.Brid)))
        {
            rows.Add(new HeadcountDiffRow(HeadcountChangeKind.Leaver, a, null, []));
        }

        return new HeadcountDiffResult(rows, columns);
    }

    private static List<HeadcountFieldChange> DiffFields(HeadcountRow a, HeadcountRow b, List<HeadcountColumn> columns)
    {
        var changes = new List<HeadcountFieldChange>();

        foreach (var column in columns)
        {
            var oldValue = column.Format(a);
            var newValue = column.Format(b);
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                changes.Add(new HeadcountFieldChange(column, oldValue, newValue));
            }
        }

        return changes;
    }

    public static string KindLabel(HeadcountChangeKind kind) => kind switch
    {
        HeadcountChangeKind.Changed => "Changed",
        HeadcountChangeKind.Joiner => "Joiner",
        HeadcountChangeKind.Leaver => "Leaver",
        _ => "Unchanged"
    };
}
