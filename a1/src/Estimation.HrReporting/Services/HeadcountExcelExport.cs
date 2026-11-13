using Estimation.Excel;
using Estimation.HrReporting.Models;

namespace Estimation.HrReporting.Services;

public static class HeadcountExcelExport
{
    private const string LeaverFillColor = "#FDE8E8";
    private const string JoinerFillColor = "#E6F4EA";
    private const string ChangedFillColor = "#FFF4CE";

    public static byte[] BuildHeadcount(IReadOnlyCollection<HeadcountRow> rows)
    {
        var builder = new ExcelWorkbookBuilder();
        WriteRowsSheet(builder, HeadcountFileParser.SheetName, rows);
        return builder.ToArray();
    }

    public static byte[] BuildDiff(
        HeadcountDiffResult result,
        IReadOnlyCollection<HeadcountDiffRow> rows,
        string labelA,
        string labelB)
    {
        var builder = new ExcelWorkbookBuilder();
        var styles = new Dictionary<HeadcountChangeKind, uint>
        {
            [HeadcountChangeKind.Changed] = builder.GetOrCreateFillStyle(ChangedFillColor),
            [HeadcountChangeKind.Joiner] = builder.GetOrCreateFillStyle(JoinerFillColor),
            [HeadcountChangeKind.Leaver] = builder.GetOrCreateFillStyle(LeaverFillColor)
        };

        WriteSummarySheet(builder, result, rows.Count, labelA, labelB);
        WriteChangesSheet(builder, rows, styles);

        var joiners = rows.Where(r => r.Kind == HeadcountChangeKind.Joiner).Select(r => r.B!).ToList();
        if (joiners.Count > 0)
        {
            WriteRowsSheet(builder, "Joiners", joiners);
        }

        var leavers = rows.Where(r => r.Kind == HeadcountChangeKind.Leaver).Select(r => r.A!).ToList();
        if (leavers.Count > 0)
        {
            WriteRowsSheet(builder, "Leavers", leavers);
        }

        return builder.ToArray();
    }

    private static void WriteRowsSheet(ExcelWorkbookBuilder builder, string name, IReadOnlyCollection<HeadcountRow> rows)
    {
        var sheet = builder.AddSheet(name);
        sheet.SetColumnWidths(HeadcountColumns.All.Select(c => c.Width).ToArray());
        sheet.WriteColoredHeader(HeadcountColumns.All.Select(c => c.Header)).FreezeTopRow();
        sheet.SetAutoFilter(HeadcountColumns.All.Count, rows.Count);

        foreach (var row in rows)
        {
            var excelRow = sheet.AddRow();
            foreach (var column in HeadcountColumns.All)
            {
                WriteValue(excelRow, column.GetValue(row));
            }
        }
    }

    private static void WriteSummarySheet(
        ExcelWorkbookBuilder builder, HeadcountDiffResult result, int exported, string labelA, string labelB)
    {
        var sheet = builder.AddSheet("Summary");
        sheet.SetColumnWidths(32, 60);
        sheet.WriteColoredHeader(["Compare", "Value"]);

        void Line(string label, string? value) => sheet.AddRow().Text(label).Text(value);

        Line("A (older)", labelA);
        Line("B (newer)", labelB);
        Line("Ignored fields", IgnoredFields(result));
        Line("Joiners", result.Count(HeadcountChangeKind.Joiner).ToString());
        Line("Leavers", result.Count(HeadcountChangeKind.Leaver).ToString());
        Line("Changed", result.Count(HeadcountChangeKind.Changed).ToString());
        Line("Unchanged", result.Count(HeadcountChangeKind.Unchanged).ToString());
        Line("People in this export", exported.ToString());

        foreach (var (column, count) in result.ChangesByColumn)
        {
            Line($"Changed: {column.Header}", count.ToString());
        }
    }

    private static void WriteChangesSheet(
        ExcelWorkbookBuilder builder, IReadOnlyCollection<HeadcountDiffRow> rows, Dictionary<HeadcountChangeKind, uint> styles)
    {
        string[] headers =
        [
            "Change",
            HeadcountColumns.Brid.Header,
            HeadcountColumns.EmployeeName.Header,
            "Field",
            "Old value",
            "New value",
            HeadcountColumns.Train.Header,
            HeadcountColumns.Team.Header,
            HeadcountColumns.LineManagerName.Header
        ];

        var lines = rows.Sum(r => Math.Max(1, r.Changes.Count));
        var sheet = builder.AddSheet("Changes");
        sheet.SetColumnWidths(12, 14, 28, 26, 30, 30, 22, 22, 26);
        sheet.WriteColoredHeader(headers).FreezeTopRow();
        sheet.SetAutoFilter(headers.Length, lines);

        foreach (var row in rows)
        {
            var current = row.Current;
            if (row.Changes.Count == 0)
            {
                WriteChangeLine(sheet, row, current, null, styles);
                continue;
            }

            foreach (var change in row.Changes)
            {
                WriteChangeLine(sheet, row, current, change, styles);
            }
        }
    }

    private static void WriteChangeLine(
        ExcelSheetBuilder sheet,
        HeadcountDiffRow row,
        HeadcountRow current,
        HeadcountFieldChange? change,
        Dictionary<HeadcountChangeKind, uint> styles)
    {
        var excelRow = sheet.AddRow();
        var label = HeadcountDiff.KindLabel(row.Kind);

        if (styles.TryGetValue(row.Kind, out var style))
        {
            excelRow.StyledText(label, style);
        }
        else
        {
            excelRow.Text(label);
        }

        excelRow
            .Text(current.Brid)
            .Text(current.EmployeeName, skipIfEmpty: true)
            .Text(change?.Column.Header, skipIfEmpty: true)
            .Text(change?.OldValue, skipIfEmpty: true)
            .Text(change?.NewValue, skipIfEmpty: true)
            .Text(current.Train, skipIfEmpty: true)
            .Text(current.Team, skipIfEmpty: true)
            .Text(current.LineManagerName, skipIfEmpty: true);
    }

    private static string IgnoredFields(HeadcountDiffResult result)
    {
        var ignored = HeadcountColumns.Comparable
            .Where(c => !result.ComparedColumns.Contains(c))
            .Select(c => c.Header)
            .ToList();
        return ignored.Count == 0 ? "None" : string.Join(", ", ignored);
    }

    private static void WriteValue(ExcelRowBuilder row, object? value)
    {
        switch (value)
        {
            case string text:
                row.Text(text, skipIfEmpty: true);
                break;
            case DateTime date:
                row.Date(date);
                break;
            case decimal number:
                row.Number((double)number);
                break;
            default:
                row.Skip();
                break;
        }
    }
}
