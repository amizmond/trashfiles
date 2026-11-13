using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Services;

public enum MetricsRowKind
{
    Value,
    Group,
    Total,
    Derived,
    Percent
}

public sealed class MetricsRow
{
    public MetricsRow(string label, MetricsRowKind kind, IReadOnlyList<decimal?> values, int level = 0)
    {
        Label = label;
        Kind = kind;
        Values = values;
        Level = level;
    }

    public string Label { get; }

    public MetricsRowKind Kind { get; }

    public IReadOnlyList<decimal?> Values { get; }

    public int Level { get; }
}

public sealed class MetricsTable
{
    public MetricsTable(
        string title,
        string rowHeader,
        IReadOnlyList<string> columns,
        IReadOnlyList<MetricsRow> rows,
        string? note = null,
        int? percentColumn = null)
    {
        Title = title;
        RowHeader = rowHeader;
        Columns = columns;
        Rows = rows;
        Note = note;
        PercentColumn = percentColumn;
    }

    public string Title { get; }

    public string RowHeader { get; }

    public IReadOnlyList<string> Columns { get; }

    public IReadOnlyList<MetricsRow> Rows { get; }

    public string? Note { get; }

    public int? PercentColumn { get; }

    public bool IsPercent(MetricsRow row, int column) => row.Kind == MetricsRowKind.Percent || column == PercentColumn;
}

public static class HeadcountMetrics
{
    public const string GrandTotal = "Grand Total";
    public const string Blank = "(blank)";

    public static IReadOnlyList<MetricsTable> Build(IReadOnlyCollection<HeadcountRow> rows)
    {
        var domains = Domains(rows);
        var columns = domains.Select(Display).Append(GrandTotal).ToList();

        return
        [
            ColleagueTypes(rows, domains, columns),
            Shares(rows, domains, columns, "ART / NonART", HeadcountColumns.ArtNonArt.Header, r => r.ArtNonArt, HeadcountRules.Art, "ART %", "Non ART %"),
            Shares(rows, domains, columns, "Eng / NonEng", HeadcountColumns.EngNonEng.Header, r => r.EngNonEng, HeadcountRules.Engineering, "Engineering %", null),
            Countries(rows, domains, columns, "Country", null, withShares: true),
            Countries(
                rows.Where(r => HeadcountRules.IsOneOf(r.ColleagueNonPermType, HeadcountRules.EmployeeTypes)).ToList(),
                domains,
                columns,
                "Country — employees only",
                $"Only {string.Join(", ", HeadcountRules.EmployeeTypes)}.",
                withShares: false),
            JobFamilies(rows, domains, columns)
        ];
    }

    public static IReadOnlyList<string> Domains(IEnumerable<HeadcountRow> rows) =>
        rows.Select(r => Key(r.ImhL07))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d.Length == 0)
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string Key(string? value) => value?.Trim() ?? string.Empty;

    public static string Display(string key) => key.Length == 0 ? Blank : key;

    private static MetricsTable ColleagueTypes(
        IReadOnlyCollection<HeadcountRow> rows, IReadOnlyList<string> domains, IReadOnlyList<string> columns)
    {
        var table = Crosstab(rows, domains, r => r.ColleagueNonPermType);
        var total = Totals(table, domains.Count);
        var fixedPrice = Sum(table, domains.Count, [HeadcountRules.FixedPriceType]);
        var perm = Sum(table, domains.Count, HeadcountRules.PermTypes);
        var totalExcl = Subtract(total, fixedPrice);

        var result = Alphabetical(table).Select(k => Row(k, MetricsRowKind.Value, table[k])).ToList();
        result.Add(Row(GrandTotal, MetricsRowKind.Total, total));
        result.Add(Row("Total excl. fixed price", MetricsRowKind.Derived, totalExcl));
        result.Add(Row("Perm", MetricsRowKind.Derived, perm));
        result.Add(new MetricsRow("Perm % excl. fixed price", MetricsRowKind.Percent, Divide(perm, totalExcl)));
        result.Add(new MetricsRow("Perm % incl. fixed price", MetricsRowKind.Percent, Divide(perm, total)));

        return new MetricsTable(
            "Colleague / Non-Perm Type",
            HeadcountColumns.ColleagueNonPermType.Header,
            columns,
            result,
            $"Perm = {string.Join(", ", HeadcountRules.PermTypes)}. Fixed price = {HeadcountRules.FixedPriceType}.");
    }

    private static MetricsTable Shares(
        IReadOnlyCollection<HeadcountRow> rows,
        IReadOnlyList<string> domains,
        IReadOnlyList<string> columns,
        string title,
        string rowHeader,
        Func<HeadcountRow, string?> key,
        string share,
        string shareLabel,
        string? restLabel)
    {
        var table = Crosstab(rows, domains, key);
        var total = Totals(table, domains.Count);
        var part = Sum(table, domains.Count, [share]);
        var ratio = Divide(part, total);

        var result = ByCount(table).Select(k => Row(k, MetricsRowKind.Value, table[k])).ToList();
        result.Add(Row(GrandTotal, MetricsRowKind.Total, total));
        result.Add(new MetricsRow(shareLabel, MetricsRowKind.Percent, ratio));
        if (restLabel is not null)
        {
            result.Add(new MetricsRow(restLabel, MetricsRowKind.Percent, ratio.Select(v => 1 - v).ToList()));
        }

        return new MetricsTable(title, rowHeader, columns, result);
    }

    private static MetricsTable Countries(
        IReadOnlyCollection<HeadcountRow> rows,
        IReadOnlyList<string> domains,
        IReadOnlyList<string> columns,
        string title,
        string? note,
        bool withShares)
    {
        var table = Crosstab(rows, domains, r => r.Country);
        var total = Totals(table, domains.Count);
        var order = ByCount(table);

        var result = order.Select(k => Row(k, MetricsRowKind.Value, table[k])).ToList();
        result.Add(Row(GrandTotal, MetricsRowKind.Total, total));
        if (withShares)
        {
            result.AddRange(order.Select(k =>
                new MetricsRow($"{k} %", MetricsRowKind.Percent, Divide(table[k], total))));
        }

        return new MetricsTable(title, HeadcountColumns.Country.Header, columns, result, note);
    }

    private static MetricsTable JobFamilies(
        IReadOnlyCollection<HeadcountRow> rows, IReadOnlyList<string> domains, IReadOnlyList<string> columns)
    {
        var result = new List<MetricsRow>();

        foreach (var group in rows
                     .GroupBy(r => Display(Key(r.JobFamilyGroup)), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key == Blank)
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var jobs = Crosstab(group.ToList(), domains, r => r.Job);
            result.Add(Row(group.Key, MetricsRowKind.Group, Totals(jobs, domains.Count)));
            result.AddRange(Alphabetical(jobs).Select(k => Row(k, MetricsRowKind.Value, jobs[k], level: 1)));
        }

        var all = Crosstab(rows, domains, _ => GrandTotal);
        result.Add(Row(GrandTotal, MetricsRowKind.Total, all.Count == 0 ? new int[domains.Count + 1] : all[GrandTotal]));

        return new MetricsTable(
            "Job Family Group and Job",
            $"{HeadcountColumns.JobFamilyGroup.Header} / {HeadcountColumns.Job.Header}",
            columns,
            result);
    }

    private static Dictionary<string, int[]> Crosstab(
        IEnumerable<HeadcountRow> rows, IReadOnlyList<string> domains, Func<HeadcountRow, string?> key)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < domains.Count; i++)
        {
            index[domains[i]] = i;
        }

        var result = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var label = Display(Key(key(row)));
            if (!result.TryGetValue(label, out var counts))
            {
                counts = new int[domains.Count + 1];
                result[label] = counts;
            }

            if (index.TryGetValue(Key(row.ImhL07), out var column))
            {
                counts[column]++;
            }

            counts[domains.Count]++;
        }

        return result;
    }

    private static int[] Totals(Dictionary<string, int[]> table, int domainCount)
    {
        var totals = new int[domainCount + 1];
        foreach (var counts in table.Values)
        {
            for (var i = 0; i < totals.Length; i++)
            {
                totals[i] += counts[i];
            }
        }

        return totals;
    }

    private static int[] Sum(Dictionary<string, int[]> table, int domainCount, IEnumerable<string> keys)
    {
        var sum = new int[domainCount + 1];
        foreach (var key in keys)
        {
            if (!table.TryGetValue(key, out var counts))
            {
                continue;
            }

            for (var i = 0; i < sum.Length; i++)
            {
                sum[i] += counts[i];
            }
        }

        return sum;
    }

    private static int[] Subtract(int[] left, int[] right) => left.Select((v, i) => v - right[i]).ToArray();

    private static List<decimal?> Divide(int[] part, int[] whole) =>
        part.Select((v, i) => whole[i] == 0 ? (decimal?)null : (decimal)v / whole[i]).ToList();

    private static IEnumerable<string> Alphabetical(Dictionary<string, int[]> table) =>
        table.Keys.OrderBy(k => k == Blank).ThenBy(k => k, StringComparer.OrdinalIgnoreCase);

    private static List<string> ByCount(Dictionary<string, int[]> table) =>
        table.OrderByDescending(p => p.Value[^1])
            .ThenBy(p => p.Key == Blank)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Key)
            .ToList();

    private static MetricsRow Row(string label, MetricsRowKind kind, int[] counts, int level = 0) =>
        new(label, kind, counts.Select(c => (decimal?)c).ToList(), level);
}
