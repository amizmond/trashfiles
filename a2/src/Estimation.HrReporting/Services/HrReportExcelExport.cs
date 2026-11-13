using Estimation.Excel;

namespace Estimation.HrReporting.Services;

public static class HrReportExcelExport
{
    private const uint PercentFormatId = 10;
    private const uint DecimalFormatId = 2;
    private const int MaxSheetNameLength = 31;
    private static readonly char[] InvalidSheetNameChars = ['[', ']', ':', '*', '?', '/', '\\'];

    public static byte[] BuildMetrics(IReadOnlyList<MetricsTable> tables, string caption)
    {
        var builder = new ExcelWorkbookBuilder();
        var styles = new Styles(builder);
        var sheet = builder.AddSheet("Metrics");
        SetWidths(sheet, tables.Select(t => t.Columns.Count).DefaultIfEmpty(0).Max());

        sheet.AddRow().Text(caption);
        foreach (var table in tables)
        {
            sheet.AddRow();
            WriteTable(sheet, table, styles);
        }

        return builder.ToArray();
    }

    public static byte[] BuildTeamsAnalysis(TeamsAnalysisResult result, IReadOnlyList<MetricsTable> summary, string caption)
    {
        var builder = new ExcelWorkbookBuilder();
        var styles = new Styles(builder);

        var summarySheet = builder.AddSheet("Summary");
        SetWidths(summarySheet, summary.Select(t => t.Columns.Count).DefaultIfEmpty(0).Max());
        summarySheet.AddRow().Text(caption);
        foreach (var table in summary)
        {
            summarySheet.AddRow();
            WriteTable(summarySheet, table, styles);
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Summary" };
        foreach (var domain in result.Domains)
        {
            var sheet = builder.AddSheet(SheetName(domain.Display, used));
            WriteDomain(sheet, domain, result.Countries, styles);
        }

        return builder.ToArray();
    }

    internal static string SheetName(string name, ISet<string> used)
    {
        var clean = new string(name.Where(c => !InvalidSheetNameChars.Contains(c)).ToArray()).Trim().Trim('\'');
        if (clean.Length == 0)
        {
            clean = "Sheet";
        }

        var candidate = Cut(clean, MaxSheetNameLength);
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = Cut(clean, MaxSheetNameLength - suffix.Length) + suffix;
        }

        return candidate;
    }

    private static void WriteTable(ExcelSheetBuilder sheet, MetricsTable table, Styles styles)
    {
        sheet.AddRow().StyledText(table.Title, styles.Bold);
        if (!string.IsNullOrWhiteSpace(table.Note))
        {
            sheet.AddRow().Text(table.Note);
        }

        sheet.WriteColoredHeader(new[] { table.RowHeader }.Concat(table.Columns));

        foreach (var row in table.Rows)
        {
            var bold = row.Kind is MetricsRowKind.Total or MetricsRowKind.Group;
            var label = row.Level > 0 ? new string(' ', row.Level * 4) + row.Label : row.Label;
            var excelRow = bold ? sheet.AddRow().StyledText(label, styles.Bold) : sheet.AddRow().Text(label);

            for (var i = 0; i < row.Values.Count; i++)
            {
                var value = row.Values[i];
                var percent = table.IsPercent(row, i);
                var blankZero = value == 0 && !percent && row.Kind is MetricsRowKind.Value or MetricsRowKind.Group;
                if (value is null || blankZero)
                {
                    excelRow.Skip();
                    continue;
                }

                var style = percent
                    ? (bold ? styles.BoldPercent : styles.Percent)
                    : value != decimal.Truncate(value.Value)
                        ? styles.Decimal
                        : bold ? styles.Bold : styles.General;
                excelRow.StyledNumber((double)value.Value, style);
            }
        }
    }

    private static void WriteDomain(
        ExcelSheetBuilder sheet, TeamsAnalysisDomain domain, IReadOnlyList<string> countries, Styles styles)
    {
        var headers = new List<string> { "Train", "Team" };
        headers.AddRange(countries);
        headers.AddRange(["Total", "Locations", "Friction"]);
        headers.AddRange(countries.Select(c => $"{c} %"));
        headers.Add("Team Size");

        var widths = new List<double> { 32, 32 };
        widths.AddRange(countries.Select(_ => 14d));
        widths.AddRange([10, 11, 10]);
        widths.AddRange(countries.Select(_ => 14d));
        widths.Add(11);
        sheet.SetColumnWidths(widths.ToArray());

        sheet.WriteColoredHeader(headers).FreezeTopRow();
        sheet.SetAutoFilter(headers.Count, domain.Teams.Count);

        foreach (var team in domain.Teams)
        {
            var row = sheet.AddRow()
                .Text(team.Train)
                .Text(team.Team.Length == 0 ? HeadcountMetrics.Blank : team.Team);
            foreach (var count in team.CountryCounts)
            {
                row.Number(count == 0 ? null : count);
            }

            row.Number(team.Total).Number(team.Locations).Text(team.Friction);
            for (var i = 0; i < countries.Count; i++)
            {
                row.StyledNumber((double)team.Share(i), styles.Percent);
            }

            row.Text(team.Size);
        }

        if (domain.Hidden.Count > 0)
        {
            sheet.AddRow();
            sheet.AddRow().StyledText("Hidden in this domain", styles.Bold);
            foreach (var hidden in domain.Hidden)
            {
                sheet.AddRow()
                    .Text(hidden.Exclusion.Kind.ToString())
                    .Text(hidden.Exclusion.Value.Length == 0 ? HeadcountMetrics.Blank : hidden.Exclusion.Value)
                    .Number(hidden.People);
            }
        }
    }

    private static string Cut(string value, int length) =>
        (value.Length <= length ? value : value[..length]).TrimEnd();

    private static void SetWidths(ExcelSheetBuilder sheet, int columns) =>
        sheet.SetColumnWidths(new[] { 48d }.Concat(Enumerable.Repeat(18d, columns)).ToArray());

    private sealed class Styles
    {
        public Styles(ExcelWorkbookBuilder builder)
        {
            General = builder.GetOrCreateCellStyle(bold: false);
            Bold = builder.GetOrCreateCellStyle(bold: true);
            Percent = builder.GetOrCreateCellStyle(bold: false, PercentFormatId);
            BoldPercent = builder.GetOrCreateCellStyle(bold: true, PercentFormatId);
            Decimal = builder.GetOrCreateCellStyle(bold: false, DecimalFormatId);
        }

        public uint General { get; }

        public uint Bold { get; }

        public uint Percent { get; }

        public uint BoldPercent { get; }

        public uint Decimal { get; }
    }
}
