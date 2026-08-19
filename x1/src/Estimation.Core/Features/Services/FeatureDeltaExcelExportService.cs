using Estimation.Core.Features.Models;
using Estimation.Excel;

namespace Estimation.Core.Features.Services;

public static class FeatureDeltaExcelExportService
{
    private const string DeltaSheetName = "Delta";
    private const string InfoSheetName = "Info";

    private const string RemovedFillColor = "#FDE8E8";
    private const string AddedFillColor = "#E6F4EA";
    private const string ChangedFillColor = "#FFF4CE";

    private const string SideA = " A";
    private const string SideB = " B";

    public static byte[] Build(
        IReadOnlyList<FeatureDeltaRow> rows,
        string artName,
        string piName,
        string labelA,
        string labelB)
    {
        var headers = new List<string> { "Change", "Jira ID" };
        var widths = new List<double> { 12, 18 };

        foreach (var field in FeatureSnapshotColumns.Context)
        {
            headers.Add(field);
            widths.Add(FeatureSnapshotColumns.WidthFor(field));
        }

        foreach (var field in FeatureSnapshotColumns.Comparable)
        {
            headers.Add(field + SideA);
            headers.Add(field + SideB);
            widths.Add(FeatureSnapshotColumns.WidthFor(field));
            widths.Add(FeatureSnapshotColumns.WidthFor(field));
        }

        var builder = new ExcelWorkbookBuilder();

        var removedStyle = builder.GetOrCreateFillStyle(RemovedFillColor);
        var addedStyle = builder.GetOrCreateFillStyle(AddedFillColor);
        var changedStyle = builder.GetOrCreateFillStyle(ChangedFillColor);

        var sheet = builder.AddSheet(DeltaSheetName);
        sheet.SetColumnWidths(widths.ToArray());
        sheet.WriteColoredHeader(headers).FreezeTopRow();
        sheet.SetAutoFilter(headers.Count, rows.Count);

        foreach (var row in rows)
        {
            var excelRow = sheet.AddRow();

            var kindStyle = row.Kind switch
            {
                FeatureDeltaChangeKind.Changed => (uint?)changedStyle,
                FeatureDeltaChangeKind.Added => addedStyle,
                FeatureDeltaChangeKind.Removed => removedStyle,
                _ => null
            };

            if (kindStyle is { } style)
            {
                excelRow.StyledText(FeatureSnapshotDeltaService.KindLabel(row.Kind), style);
            }
            else
            {
                excelRow.Text(FeatureSnapshotDeltaService.KindLabel(row.Kind));
            }

            excelRow.Text(row.JiraId);

            foreach (var field in FeatureSnapshotColumns.Context)
            {
                WriteValue(excelRow, row.Current, field, null);
            }

            foreach (var field in FeatureSnapshotColumns.Comparable)
            {
                var changed = row.HasChange(field);
                WriteValue(excelRow, row.A, field, changed ? removedStyle : null);
                WriteValue(excelRow, row.B, field, changed ? addedStyle : null);
            }
        }

        var infoRows = new (string Field, string? Text, int? Number)[]
        {
            ("ART", artName, null),
            ("PI", piName, null),
            ("A", labelA, null),
            ("B", labelB, null),
            ("Rows exported", null, rows.Count),
            ("Changed", null, rows.Count(r => r.Kind == FeatureDeltaChangeKind.Changed)),
            ("Added", null, rows.Count(r => r.Kind == FeatureDeltaChangeKind.Added)),
            ("Removed", null, rows.Count(r => r.Kind == FeatureDeltaChangeKind.Removed)),
            ("Unchanged", null, rows.Count(r => r.Kind == FeatureDeltaChangeKind.Unchanged))
        };

        var infoHeaders = new[] { "Field", "Value" };

        var info = builder.AddSheet(InfoSheetName);
        info.SetColumnWidths(24, 60);
        info.WriteColoredHeader(infoHeaders).FreezeTopRow();
        info.SetAutoFilter(infoHeaders.Length, infoRows.Length);

        foreach (var (field, text, number) in infoRows)
        {
            var infoRow = info.AddRow().Text(field);

            if (number.HasValue)
            {
                infoRow.Number(number.Value);
            }
            else
            {
                infoRow.Text(text);
            }
        }

        return builder.ToArray();
    }

    private static void WriteValue(ExcelRowBuilder excelRow, FeatureSnapshotItem? item, string field, uint? style)
    {
        if (field == FeatureDeltaFields.StoryPoints)
        {
            if (style is { } numberStyle)
            {
                excelRow.StyledNumber(item?.StoryPoints, numberStyle);
            }
            else
            {
                excelRow.Number(item?.StoryPoints);
            }

            return;
        }

        var value = FeatureSnapshotDeltaService.FieldValue(item, field);

        if (style is { } textStyle)
        {
            excelRow.StyledText(value, textStyle);
        }
        else
        {
            excelRow.Text(value);
        }
    }
}
