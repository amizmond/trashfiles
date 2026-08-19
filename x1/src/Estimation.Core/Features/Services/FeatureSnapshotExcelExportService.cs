using Estimation.Core.Features.Models;
using Estimation.Excel;

namespace Estimation.Core.Features.Services;

// Exports one snapshot on its own — the same columns a delta shows, minus the A/B pairing and the
// change marker, since there is nothing to compare against.
public static class FeatureSnapshotExcelExportService
{
    private const string FeaturesSheetName = "Features";
    private const string InfoSheetName = "Info";

    private const string JiraIdHeader = "Jira ID";
    private const double JiraIdWidth = 18;

    public record SnapshotInfo(
        string SnapshotName,
        string ArtName,
        string PiName,
        string CreatedAt,
        string? CreatedBy,
        bool IsAutomatic);

    public static byte[] Build(IReadOnlyList<FeatureSnapshotItem> items, SnapshotInfo info)
    {
        var fields = FeatureSnapshotColumns.Context
            .Concat(FeatureSnapshotColumns.Comparable)
            .ToArray();

        var headers = new List<string> { JiraIdHeader };
        var widths = new List<double> { JiraIdWidth };

        foreach (var field in fields)
        {
            headers.Add(field);
            widths.Add(FeatureSnapshotColumns.WidthFor(field));
        }

        var builder = new ExcelWorkbookBuilder();

        var sheet = builder.AddSheet(FeaturesSheetName);
        sheet.SetColumnWidths(widths.ToArray());
        sheet.WriteColoredHeader(headers).FreezeTopRow();
        sheet.SetAutoFilter(headers.Count, items.Count);

        foreach (var item in items)
        {
            var row = sheet.AddRow().Text(item.JiraId);

            foreach (var field in fields)
            {
                WriteValue(row, item, field);
            }
        }

        WriteInfoSheet(builder, info, items.Count);

        return builder.ToArray();
    }

    private static void WriteInfoSheet(ExcelWorkbookBuilder builder, SnapshotInfo snapshot, int featureCount)
    {
        var infoRows = new (string Field, string? Text, int? Number)[]
        {
            ("Snapshot", snapshot.SnapshotName, null),
            ("ART", snapshot.ArtName, null),
            ("PI", snapshot.PiName, null),
            ("Created", snapshot.CreatedAt, null),
            ("Created by", snapshot.CreatedBy, null),
            ("Source", snapshot.IsAutomatic ? "PI locked" : "Manual", null),
            ("Features exported", null, featureCount)
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
    }

    // Dates and story points go in as real dates and numbers rather than the text a delta uses, so
    // the header filter offers date and number predicates instead of a list of strings.
    private static void WriteValue(ExcelRowBuilder row, FeatureSnapshotItem item, string field)
    {
        switch (field)
        {
            case FeatureDeltaFields.StoryPoints:
                row.Number(item.StoryPoints);
                break;

            case FeatureDeltaFields.TargetStart:
                row.Date(item.TargetStart);
                break;

            case FeatureDeltaFields.TargetEnd:
                row.Date(item.TargetEnd);
                break;

            default:
                row.Text(FeatureSnapshotDeltaService.FieldValue(item, field));
                break;
        }
    }
}
