using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class HrReportExcelExportTests
{
    private static readonly HeadcountRow[] People =
    [
        new() { Brid = "B1", ImhL07 = "RFT - Market Risk", ColleagueNonPermType = "Permanent/Regular", Country = "India", Train = "Analytics", Team = "Delta", ArtNonArt = "ART" },
        new() { Brid = "B2", ImhL07 = "RFT - Market Risk", ColleagueNonPermType = "Graduate", Country = "USA", Train = "Analytics", Team = "Delta", ArtNonArt = "ART" },
        new() { Brid = "B3", ImhL07 = "RFT: Capital/Core", ColleagueNonPermType = "Permanent/Regular", Country = "India", Train = "Core", Team = "Aluminium", ArtNonArt = "ART" }
    ];

    [Fact]
    public void Metrics_are_written_as_stacked_tables_with_real_percentages()
    {
        var bytes = HrReportExcelExport.BuildMetrics(HeadcountMetrics.Build(People), "caption");

        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Equal(["Metrics"], SheetNames(document));
        var text = HeadcountFileParserTestsAccess.ReadSheetText(document, "Metrics");
        Assert.Equal("caption", text[0][0]);
        Assert.Contains(text, r => r.FirstOrDefault() == "Colleague / Non-Perm Type" && r.Count == 1);
        Assert.Contains(text, r => r.FirstOrDefault() == "Job Family Group and Job" && r.Count == 1);

        var percentStyles = PercentStyleIndexes(document);
        var permShare = text.FindIndex(r => r.FirstOrDefault() == "Perm % incl. fixed price");
        var cell = CellAt(document, "Metrics", permShare + 1, "D");
        Assert.Contains(cell.StyleIndex!.Value, percentStyles);
        Assert.Equal("1", cell.CellValue!.Text);
    }

    [Fact]
    public void Teams_analysis_gets_a_summary_and_a_sheet_per_domain_with_safe_names()
    {
        var result = TeamsAnalysis.Build(People, []);

        var bytes = HrReportExcelExport.BuildTeamsAnalysis(result, TeamsAnalysis.Summary(result), "caption");

        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Equal(["Summary", "RFT - Market Risk", "RFT CapitalCore"], SheetNames(document));
        var market = HeadcountFileParserTestsAccess.ReadSheetText(document, "RFT - Market Risk");
        Assert.Equal(["Train", "Team", "India", "USA", "Total", "Locations", "Friction", "India %", "USA %", "Team Size"], market[0]);
        Assert.Equal(["Analytics", "Delta", "1", "1", "2", "2", "high"], market[1].Take(7));
    }

    [Fact]
    public void Sheet_names_are_cut_to_31_characters_and_kept_unique()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var first = HrReportExcelExport.SheetName("RFT - Counterparty Credit Risk and More", used);
        var second = HrReportExcelExport.SheetName("RFT - Counterparty Credit Risk and More", used);
        var blank = HrReportExcelExport.SheetName("[]:*?/\\", used);

        Assert.Equal("RFT - Counterparty Credit Risk", first);
        Assert.Equal("RFT - Counterparty Credit R (2)", second);
        Assert.Equal("Sheet", blank);
        Assert.All(new[] { first, second, blank }, n => Assert.True(n.Length <= 31));
    }

    private static List<string> SheetNames(SpreadsheetDocument document) =>
        document.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value!).ToList();

    private static HashSet<uint> PercentStyleIndexes(SpreadsheetDocument document) =>
        document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!.GetFirstChild<CellFormats>()!
            .Elements<CellFormat>()
            .Select((format, index) => (format, index))
            .Where(x => x.format.NumberFormatId?.Value == 10)
            .Select(x => (uint)x.index)
            .ToHashSet();

    private static Cell CellAt(SpreadsheetDocument document, string sheetName, int rowNumber, string column)
    {
        var sheet = document.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name!.Value == sheetName);
        var part = (WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!.Value!);
        return part.Worksheet!.Descendants<Cell>().Single(c => c.CellReference!.Value == $"{column}{rowNumber}");
    }
}
