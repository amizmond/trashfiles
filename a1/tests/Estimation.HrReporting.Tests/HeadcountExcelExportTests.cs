using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Estimation.HrReporting.Tests.Infrastructure;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class HeadcountExcelExportTests
{
    [Fact]
    public void Export_can_be_uploaded_again_without_differences()
    {
        var workbook = TestWorkbook.WithAllColumns()
            .AddPerson("G001", v =>
            {
                v["Length of Service Date"] = new DateTime(2018, 6, 4);
                v["Length of Service In Years"] = 8;
                v["FTE"] = 0.75;
                v["Org Unit ID"] = "000123";
                v["Actions"] = "Change line manager";
            })
            .AddPerson("G002", v => v["Vendor"] = "-");
        var original = HeadcountFileParser.Parse(workbook.ToStream(), "headcount.xlsx");
        Assert.True(original.CanSave, string.Join(Environment.NewLine, original.Errors));

        var exported = HeadcountExcelExport.BuildHeadcount(original.Rows);
        var reparsed = HeadcountFileParser.Parse(new MemoryStream(exported), "export.xlsx");

        Assert.True(reparsed.CanSave, string.Join(Environment.NewLine, reparsed.Errors));
        var diff = HeadcountDiff.Compare(original.Rows, reparsed.Rows, HeadcountColumns.Comparable);
        Assert.False(diff.HasDifferences);
        Assert.Equal(2, diff.Count(HeadcountChangeKind.Unchanged));
    }

    [Fact]
    public void Compare_export_has_summary_changes_joiners_and_leavers()
    {
        HeadcountRow[] a =
        [
            new() { Brid = "B1", EmployeeName = "Anna", Team = "Argon" },
            new() { Brid = "B2", EmployeeName = "Ben", Team = "Argon" }
        ];
        HeadcountRow[] b =
        [
            new() { Brid = "B1", EmployeeName = "Anna", Team = "Neon", City = "Prague" },
            new() { Brid = "B3", EmployeeName = "Cleo", Team = "Argon" }
        ];
        var diff = HeadcountDiff.Compare(a, b);

        var bytes = HeadcountExcelExport.BuildDiff(diff, diff.Rows, "2026-10-31", "2026-11-30");

        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheets = document.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Select(s => s.Name!.Value).ToList();
        Assert.Equal(["Summary", "Changes", "Joiners", "Leavers"], sheets);

        var changes = HeadcountFileParserTestsAccess.ReadSheetText(document, "Changes");
        Assert.Equal(["Change", "BRID", "Employee Name", "Field", "Old value", "New value", "Train", "Team", "Line Manager Name"], changes[0]);
        Assert.Contains(changes, r => r.Take(6).SequenceEqual(["Changed", "B1", "Anna", "City", "", "Prague"]));
        Assert.Contains(changes, r => r.Take(6).SequenceEqual(["Changed", "B1", "Anna", "Team", "Argon", "Neon"]));
        Assert.Contains(changes, r => r.Take(3).SequenceEqual(["Joiner", "B3", "Cleo"]));
        Assert.Contains(changes, r => r.Take(3).SequenceEqual(["Leaver", "B2", "Ben"]));
    }
}

internal static class HeadcountFileParserTestsAccess
{
    public static List<List<string>> ReadSheetText(SpreadsheetDocument document, string sheetName)
    {
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name?.Value == sheetName);
        return HeadcountWorkbookReader.ReadRows(workbookPart, sheet)
            .Select(row =>
            {
                var width = row.Cells.Count == 0 ? 0 : row.Cells.Keys.Max() + 1;
                return Enumerable.Range(0, width).Select(i => row.Get(i)?.Text ?? string.Empty).ToList();
            })
            .ToList();
    }
}
