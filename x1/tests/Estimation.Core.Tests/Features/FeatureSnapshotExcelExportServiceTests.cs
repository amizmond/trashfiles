using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Excel;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureSnapshotExcelExportServiceTests
{
    private const string FeaturesSheet = "Features";
    private const string InfoSheet = "Info";

    private static readonly FeatureSnapshotExcelExportService.SnapshotInfo Info = new(
        "PI 26.1 — 18 Aug 2026 09:15",
        "Payments ART",
        "PI 26.1",
        "18 Aug 2026 09:15",
        "DOMAIN\tester",
        IsAutomatic: false);

    private static FeatureSnapshotItem Item(
        int featureId,
        string jiraId,
        int? storyPoints = 3,
        string? summary = "A feature",
        DateTime? targetStart = null) =>
        new()
        {
            FeatureId = featureId,
            JiraId = jiraId,
            ArtName = "Payments ART",
            PiName = "PI 26.1",
            StoryPoints = storyPoints,
            Summary = summary,
            TargetStart = targetStart
        };

    private static byte[] Export(params FeatureSnapshotItem[] items) =>
        FeatureSnapshotExcelExportService.Build(items, Info);

    private static Worksheet Sheet(SpreadsheetDocument doc, string name)
    {
        var sheet = doc.WorkbookPart!.Workbook
            .GetFirstChild<Sheets>()!
            .Elements<Sheet>()
            .Single(s => s.Name!.Value == name);

        return ((WorksheetPart)doc.WorkbookPart!.GetPartById(sheet.Id!.Value!)).Worksheet;
    }

    private static Cell CellUnder(SpreadsheetDocument doc, string header, int dataRow = 1)
    {
        var rows = Sheet(doc, FeaturesSheet).Descendants<Row>().ToList();

        var headerCell = rows[0].Elements<Cell>().Single(c => c.CellValue?.Text == header);
        var column = new string(headerCell.CellReference!.Value!.TakeWhile(char.IsLetter).ToArray());

        return rows[dataRow].Elements<Cell>().Single(c => c.CellReference!.Value == $"{column}{dataRow + 1}");
    }

    [Fact]
    public void The_header_carries_every_delta_field_once_and_no_comparison_columns()
    {
        using var stream = new MemoryStream(Export(Item(1, "FEAT-1")));
        var (headers, _) = ExcelSheetReader.Read(stream, FeaturesSheet);

        var expected = new[] { "Jira ID" }
            .Concat(FeatureSnapshotColumns.Context)
            .Concat(FeatureSnapshotColumns.Comparable)
            .ToList();

        Assert.Equal(expected, headers);
        Assert.DoesNotContain("Change", headers);
        Assert.DoesNotContain(headers, h => h.EndsWith(" A") || h.EndsWith(" B"));
    }

    [Fact]
    public void Every_feature_of_the_snapshot_becomes_a_row()
    {
        using var stream = new MemoryStream(Export(Item(1, "FEAT-1"), Item(2, "FEAT-2"), Item(3, "FEAT-3")));
        var (headers, rows) = ExcelSheetReader.Read(stream, FeaturesSheet);
        var map = ExcelSheetReader.BuildColumnMap(headers);

        Assert.Equal(3, rows.Count);
        Assert.Equal(["FEAT-1", "FEAT-2", "FEAT-3"], rows.Select(r => ExcelSheetReader.GetCell(r, map, "Jira ID")));
    }

    [Fact]
    public void Captured_values_land_under_their_own_column()
    {
        using var stream = new MemoryStream(Export(Item(1, "FEAT-1", summary: "Ledger rework")));
        var (headers, rows) = ExcelSheetReader.Read(stream, FeaturesSheet);
        var map = ExcelSheetReader.BuildColumnMap(headers);

        var row = Assert.Single(rows);

        Assert.Equal("Ledger rework", ExcelSheetReader.GetCell(row, map, FeatureDeltaFields.Summary));
        Assert.Equal("Payments ART", ExcelSheetReader.GetCell(row, map, FeatureDeltaFields.Art));
        Assert.Equal("PI 26.1", ExcelSheetReader.GetCell(row, map, FeatureDeltaFields.Pi));
    }

    [Fact]
    public void Every_column_carries_a_filter_dropdown_over_the_exported_rows()
    {
        using var stream = new MemoryStream(Export(Item(1, "FEAT-1"), Item(2, "FEAT-2")));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var reference = Sheet(doc, FeaturesSheet).GetFirstChild<AutoFilter>()?.Reference?.Value;

        Assert.NotNull(reference);
        Assert.StartsWith("A1:", reference);
        Assert.EndsWith("3", reference);
    }

    [Fact]
    public void Story_points_are_written_as_a_number_so_the_filter_offers_number_predicates()
    {
        using var stream = new MemoryStream(Export(Item(1, "FEAT-1", storyPoints: 8)));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var cell = CellUnder(doc, FeatureDeltaFields.StoryPoints);

        Assert.NotEqual(CellValues.SharedString, cell.DataType?.Value);
        Assert.Equal("8", cell.CellValue?.Text);
    }

    [Fact]
    public void Target_dates_are_written_as_dates_rather_than_text()
    {
        using var stream = new MemoryStream(Export(Item(1, "FEAT-1", targetStart: new DateTime(2026, 8, 17))));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var cell = CellUnder(doc, FeatureDeltaFields.TargetStart);

        Assert.NotEqual(CellValues.SharedString, cell.DataType?.Value);
        Assert.Equal(new DateTime(2026, 8, 17), DateTime.FromOADate(double.Parse(cell.CellValue!.Text)));
    }

    [Fact]
    public void The_info_sheet_records_what_the_snapshot_is()
    {
        using var stream = new MemoryStream(Export(Item(1, "FEAT-1"), Item(2, "FEAT-2")));
        var (_, rows) = ExcelSheetReader.Read(stream, InfoSheet);

        Assert.Contains(rows, r => r.Count > 1 && r[0] == "Snapshot" && r[1] == "PI 26.1 — 18 Aug 2026 09:15");
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "ART" && r[1] == "Payments ART");
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "PI" && r[1] == "PI 26.1");
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "Created" && r[1] == "18 Aug 2026 09:15");
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "Created by" && r[1] == "DOMAIN\tester");
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "Source" && r[1] == "Manual");
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "Features exported" && r[1] == "2");
    }

    [Fact]
    public void A_pi_lock_baseline_is_labelled_as_such()
    {
        var bytes = FeatureSnapshotExcelExportService.Build(
            [Item(1, "FEAT-1")],
            Info with { IsAutomatic = true });

        using var stream = new MemoryStream(bytes);
        var (_, rows) = ExcelSheetReader.Read(stream, InfoSheet);

        Assert.Contains(rows, r => r.Count > 1 && r[0] == "Source" && r[1] == "PI locked");
    }

    [Fact]
    public void An_empty_snapshot_still_produces_a_usable_sheet()
    {
        using var stream = new MemoryStream(Export());
        var (headers, rows) = ExcelSheetReader.Read(stream, FeaturesSheet);

        Assert.NotEmpty(headers);
        Assert.Empty(rows);
    }
}
