using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Excel;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureDeltaExcelExportServiceTests
{
    private const string DeltaSheet = "Delta";
    private const string InfoSheet = "Info";

    private const string ArtColumn = "C";
    private const string PiColumn = "D";
    private const string SummaryColumnA = "E";
    private const string SummaryColumnB = "F";

    private readonly FeatureSnapshotDeltaService _delta = new();

    private static FeatureSnapshotItem Item(
        int featureId,
        string jiraId,
        string? piName = "PI 26.1",
        int? storyPoints = 3,
        string? summary = "A feature") =>
        new()
        {
            FeatureId = featureId,
            JiraId = jiraId,
            ArtName = "Payments ART",
            PiName = piName,
            StoryPoints = storyPoints,
            Summary = summary
        };

    private byte[] Export(IReadOnlyList<FeatureSnapshotItem> a, IReadOnlyList<FeatureSnapshotItem> b) =>
        FeatureDeltaExcelExportService.Build(
            _delta.Compare(a, b).Rows,
            "Payments ART",
            "PI 26.1",
            "Snapshot — 14 Jul 2026 09:00",
            "Now — 11 Aug 2026 10:15");

    private static Worksheet Sheet(SpreadsheetDocument doc, string name)
    {
        var sheet = doc.WorkbookPart!.Workbook!
            .GetFirstChild<Sheets>()!
            .Elements<Sheet>()
            .Single(s => s.Name!.Value == name);

        return ((WorksheetPart)doc.WorkbookPart!.GetPartById(sheet.Id!.Value!)).Worksheet!;
    }

    private static Cell? CellAt(SpreadsheetDocument doc, string sheetName, string reference) =>
        Sheet(doc, sheetName)
            .GetFirstChild<SheetData>()!
            .Elements<Row>()
            .SelectMany(r => r.Elements<Cell>())
            .SingleOrDefault(c => c.CellReference!.Value == reference);

    private static string? FillColorOf(SpreadsheetDocument doc, Cell cell)
    {
        if (cell.StyleIndex is null)
        {
            return null;
        }

        var styles = doc.WorkbookPart!.WorkbookStylesPart!.Stylesheet!;
        var format = (CellFormat)styles.GetFirstChild<CellFormats>()!.ElementAt((int)cell.StyleIndex.Value);

        if (format.FillId is null)
        {
            return null;
        }

        var fill = (Fill)styles.GetFirstChild<Fills>()!.ElementAt((int)format.FillId.Value);
        return fill.PatternFill?.ForegroundColor?.Rgb?.Value;
    }

    [Fact]
    public void The_workbook_has_a_delta_sheet_and_an_info_sheet()
    {
        using var stream = new MemoryStream(Export([Item(1, "FEAT-1")], [Item(1, "FEAT-1")]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var names = doc.WorkbookPart!.Workbook!
            .GetFirstChild<Sheets>()!
            .Elements<Sheet>()
            .Select(s => s.Name!.Value)
            .ToList();

        Assert.Equal(new[] { DeltaSheet, InfoSheet }, names);
    }

    [Fact]
    public void Art_and_pi_get_a_single_column_because_they_cannot_change()
    {
        using var stream = new MemoryStream(Export([Item(1, "FEAT-1")], [Item(1, "FEAT-1")]));
        var (headers, _) = ExcelSheetReader.Read(stream, DeltaSheet);

        Assert.Single(headers, h => h == FeatureDeltaFields.Art);
        Assert.Single(headers, h => h == FeatureDeltaFields.Pi);
        Assert.DoesNotContain(FeatureDeltaFields.Art + " A", headers);
        Assert.DoesNotContain(FeatureDeltaFields.Art + " B", headers);
        Assert.DoesNotContain(FeatureDeltaFields.Pi + " A", headers);
        Assert.DoesNotContain(FeatureDeltaFields.Pi + " B", headers);
    }

    [Fact]
    public void A_single_column_shows_the_b_side_value_and_is_never_painted()
    {
        var bytes = Export(
            [Item(1, "FEAT-1", piName: "PI 26.1")],
            [Item(1, "FEAT-1", piName: "PI 26.2")]);

        using (var read = new MemoryStream(bytes))
        {
            var (headers, rows) = ExcelSheetReader.Read(read, DeltaSheet);

            Assert.Equal(FeatureDeltaFields.Pi, headers[3]);
            Assert.Equal("PI 26.2", rows[0][3]);
        }

        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, false);

        Assert.Null(FillColorOf(doc, CellAt(doc, DeltaSheet, PiColumn + "2")!));
        Assert.Null(FillColorOf(doc, CellAt(doc, DeltaSheet, ArtColumn + "2")!));
    }

    [Fact]
    public void Comparable_fields_get_an_a_and_a_b_column()
    {
        using var stream = new MemoryStream(Export([Item(1, "FEAT-1")], [Item(1, "FEAT-1")]));
        var (headers, _) = ExcelSheetReader.Read(stream, DeltaSheet);

        Assert.Equal(
            new[] { "Change", "Jira ID", FeatureDeltaFields.Art, FeatureDeltaFields.Pi },
            headers.Take(4));

        foreach (var field in new[]
        {
            FeatureDeltaFields.Summary, FeatureDeltaFields.TargetStart,
            FeatureDeltaFields.StoryPoints, FeatureDeltaFields.RagExplain
        })
        {
            Assert.Contains(field + " A", headers);
            Assert.Contains(field + " B", headers);
        }

        Assert.DoesNotContain("Changed fields", headers);
    }

    [Fact]
    public void Fix_versions_are_left_out_of_the_delta()
    {
        using var stream = new MemoryStream(Export([Item(1, "FEAT-1")], [Item(1, "FEAT-1")]));
        var (headers, _) = ExcelSheetReader.Read(stream, DeltaSheet);

        Assert.DoesNotContain(headers, h => h.StartsWith(FeatureDeltaFields.FixVersions));
    }

    [Fact]
    public void The_header_row_is_bold_and_filled()
    {
        using var stream = new MemoryStream(Export([Item(1, "FEAT-1")], [Item(1, "FEAT-1")]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var styles = doc.WorkbookPart!.WorkbookStylesPart!.Stylesheet!;
        var headerCells = Sheet(doc, DeltaSheet).Descendants<Row>().First().Elements<Cell>().ToList();

        Assert.NotEmpty(headerCells);
        Assert.All(headerCells, cell =>
        {
            var format = (CellFormat)styles.GetFirstChild<CellFormats>()!.ElementAt((int)cell.StyleIndex!.Value);
            var font = (Font)styles.GetFirstChild<Fonts>()!.ElementAt((int)format.FontId!.Value);
            var fill = (Fill)styles.GetFirstChild<Fills>()!.ElementAt((int)format.FillId!.Value);

            Assert.NotNull(font.Bold);
            Assert.Equal(PatternValues.Solid, fill.PatternFill?.PatternType?.Value);
        });
    }

    [Fact]
    public void The_header_row_stays_pinned_while_the_sheet_scrolls()
    {
        using var stream = new MemoryStream(Export([Item(1, "FEAT-1")], [Item(1, "FEAT-1")]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var pane = Sheet(doc, DeltaSheet).GetFirstChild<SheetViews>()?.GetFirstChild<SheetView>()?.GetFirstChild<Pane>();

        Assert.NotNull(pane);
        Assert.Equal(PaneStateValues.Frozen, pane!.State?.Value);
        Assert.Equal("A2", pane.TopLeftCell?.Value);
    }

    [Fact]
    public void Every_column_carries_a_filter_dropdown_over_the_exported_rows()
    {
        using var stream = new MemoryStream(Export(
            [Item(1, "FEAT-1"), Item(2, "FEAT-2")],
            [Item(1, "FEAT-1"), Item(2, "FEAT-2")]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var reference = Sheet(doc, DeltaSheet).GetFirstChild<AutoFilter>()?.Reference?.Value;

        Assert.NotNull(reference);
        Assert.StartsWith("A1:", reference);
        Assert.EndsWith("3", reference);
    }

    [Fact]
    public void A_changed_field_paints_the_a_cell_and_the_b_cell_in_different_colours()
    {
        using var stream = new MemoryStream(Export(
            [Item(1, "FEAT-1", summary: "Before")],
            [Item(1, "FEAT-1", summary: "After")]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var cellA = CellAt(doc, DeltaSheet, SummaryColumnA + "2")!;
        var cellB = CellAt(doc, DeltaSheet, SummaryColumnB + "2")!;

        var colorA = FillColorOf(doc, cellA);
        var colorB = FillColorOf(doc, cellB);

        Assert.NotNull(colorA);
        Assert.NotNull(colorB);
        Assert.NotEqual(colorA, colorB);
    }

    [Fact]
    public void An_unchanged_field_is_left_unpainted()
    {
        using var stream = new MemoryStream(Export(
            [Item(1, "FEAT-1", summary: "Same")],
            [Item(1, "FEAT-1", summary: "Same")]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        Assert.Null(FillColorOf(doc, CellAt(doc, DeltaSheet, SummaryColumnA + "2")!));
        Assert.Null(FillColorOf(doc, CellAt(doc, DeltaSheet, SummaryColumnB + "2")!));
    }

    [Fact]
    public void The_change_cell_is_painted_for_added_and_removed_rows()
    {
        using var stream = new MemoryStream(Export([Item(1, "FEAT-1")], [Item(2, "FEAT-2")]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var first = CellAt(doc, DeltaSheet, "A2")!;
        var second = CellAt(doc, DeltaSheet, "A3")!;

        Assert.NotNull(FillColorOf(doc, first));
        Assert.NotNull(FillColorOf(doc, second));
        Assert.NotEqual(FillColorOf(doc, first), FillColorOf(doc, second));
    }

    [Fact]
    public void Story_points_are_written_as_numbers_so_excel_sorts_them()
    {
        using var stream = new MemoryStream(Export(
            [Item(1, "FEAT-1", storyPoints: 3)],
            [Item(1, "FEAT-1", storyPoints: 10)]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var headers = Sheet(doc, DeltaSheet).Descendants<Row>().First().Elements<Cell>().ToList();
        var index = headers.FindIndex(c => c.CellValue?.Text == FeatureDeltaFields.StoryPoints + " A");

        var cell = Sheet(doc, DeltaSheet).Descendants<Row>().ElementAt(1).Elements<Cell>().ElementAt(index);

        Assert.NotEqual(CellValues.SharedString, cell.DataType?.Value);
        Assert.Equal("3", cell.CellValue?.Text);
    }

    [Fact]
    public void The_info_sheet_records_the_two_compared_sides()
    {
        using var stream = new MemoryStream(Export([Item(1, "FEAT-1")], [Item(1, "FEAT-1")]));
        var (_, rows) = ExcelSheetReader.Read(stream, InfoSheet);

        Assert.Contains(rows, r => r.Count > 1 && r[0] == "ART" && r[1] == "Payments ART");
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "PI" && r[1] == "PI 26.1");
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "A" && r[1].StartsWith("Snapshot", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Count > 1 && r[0] == "B" && r[1].StartsWith("Now", StringComparison.Ordinal));
    }

    [Fact]
    public void The_workbook_is_schema_valid()
    {
        using var stream = new MemoryStream(Export(
            [Item(1, "FEAT-1", piName: "PI 26.1")],
            [Item(1, "FEAT-1", piName: "PI 26.2"), Item(2, "FEAT-2")]));
        using var doc = SpreadsheetDocument.Open(stream, false);

        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(doc)
            .Select(e => $"{e.Path?.XPath}: {e.Description}")
            .ToList();

        Assert.Empty(errors);
    }
}
