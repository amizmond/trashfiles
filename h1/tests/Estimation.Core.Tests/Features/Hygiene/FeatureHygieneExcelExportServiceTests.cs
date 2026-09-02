using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Hygiene.Services;
using Estimation.Excel;
using Xunit;

namespace Estimation.Core.Tests.Features.Hygiene;

public class FeatureHygieneExcelExportServiceTests
{
    private const string FeaturesSheet = "Features";
    private const string RulesSheet = "Rules";
    private const string InfoSheet = "Info";

    private static readonly FeatureHygieneRule DescriptionRule = new()
    {
        Id = 1,
        Field = HygieneField.Description,
        Check = HygieneCheck.NotEmpty,
        ParametersJson = "{}",
        IsEnabled = true
    };

    private static readonly FeatureHygieneRule StoryPointsRule = new()
    {
        Id = 2,
        Field = HygieneField.StoryPoints,
        Check = HygieneCheck.NotGreaterThan,
        ParametersJson = new HygieneRuleParameters { Number = 21 }.ToJson(),
        IsEnabled = true
    };

    private static readonly FeatureHygieneRule DisabledRule = new()
    {
        Id = 3,
        Field = HygieneField.Teams,
        Check = HygieneCheck.NotEmpty,
        ParametersJson = "{}",
        IsEnabled = false
    };

    private static FeatureHygieneReport Report()
    {
        var failing = new FeatureHygieneRow(1, "PAY-1", "Checkout", "Faster checkout", "To Do", "Gold", "PI 26.2",
        [
            new HygieneFailure(1, HygieneField.Description, HygieneCheck.NotEmpty, "Description is not empty", "empty", null, null),
            new HygieneFailure(2, HygieneField.StoryPoints, HygieneCheck.NotGreaterThan, "Story points is not greater than 21", "34 > 21", "Split it", "34")
        ]);

        var healthy = new FeatureHygieneRow(2, "PAY-2", "Refunds", "Refund flow", "Done", null, "PI 26.2", []);

        return new FeatureHygieneReport(1, "Payments ART", "PAY", 2, "PI 26.2",
            [DescriptionRule, StoryPointsRule, DisabledRule], [failing, healthy]);
    }

    [Fact]
    public void The_workbook_has_features_rules_and_info_sheets()
    {
        var report = Report();
        using var stream = new MemoryStream(FeatureHygieneExcelExportService.Build(report, report.Rows));

        Assert.Equal([FeaturesSheet, RulesSheet, InfoSheet], WorkbookSheetNames(stream));
    }

    [Fact]
    public void Enabled_rules_become_columns_and_failures_fill_them()
    {
        var report = Report();
        using var stream = new MemoryStream(FeatureHygieneExcelExportService.Build(report, report.Rows));
        var (headers, rows) = ExcelSheetReader.Read(stream, FeaturesSheet);

        Assert.Equal(
            ["Jira ID", "Feature name", "Summary", "Jira status", "Teams", "PI", "Healthy", "Failed checks",
             "Description is not empty", "Story points is not greater than 21"],
            headers);

        Assert.Equal(2, rows.Count);
        Assert.Equal("PAY-1", rows[0][0]);
        Assert.Equal("No", rows[0][6]);
        Assert.Equal("2", rows[0][7]);
        Assert.Equal("empty", rows[0][8]);
        Assert.Equal("34 > 21 — 34", rows[0][9]);

        Assert.Equal("PAY-2", rows[1][0]);
        Assert.Equal("Yes", rows[1][6]);
        Assert.Equal("0", rows[1][7]);
        Assert.Equal(string.Empty, rows[1][8]);
    }

    [Fact]
    public void The_rules_sheet_lists_every_enabled_rule_with_its_failure_count()
    {
        var report = Report();
        using var stream = new MemoryStream(FeatureHygieneExcelExportService.Build(report, report.Rows));
        var (headers, rows) = ExcelSheetReader.Read(stream, RulesSheet);

        Assert.Equal(["#", "Rule", "Field", "Check", "Parameters", "Message", "Features failing"], headers);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Description is not empty", rows[0][1]);
        Assert.Equal("1", rows[0][6]);
        Assert.Equal("Not greater than", rows[1][3]);
        Assert.Equal("21", rows[1][4]);
        Assert.Equal("1", rows[1][6]);
    }

    [Fact]
    public void Only_the_given_rows_are_exported_but_the_info_sheet_keeps_the_report_totals()
    {
        var report = Report();
        var shown = report.Rows.Where(r => !r.IsHealthy).ToList();
        using var stream = new MemoryStream(FeatureHygieneExcelExportService.Build(report, shown));

        var (_, featureRows) = ExcelSheetReader.Read(stream, FeaturesSheet);
        Assert.Single(featureRows);

        stream.Position = 0;
        var (_, infoRows) = ExcelSheetReader.Read(stream, InfoSheet);
        var info = infoRows.ToDictionary(r => r[0], r => r[1]);

        Assert.Equal("Payments ART", info["ART"]);
        Assert.Equal("PI 26.2", info["PI"]);
        Assert.Equal("2", info["Features in the PI"]);
        Assert.Equal("1", info["Healthy"]);
        Assert.Equal("1", info["Not healthy"]);
        Assert.Equal("1", info["Rows exported"]);
    }

    private static List<string> WorkbookSheetNames(Stream stream)
    {
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, false);

        return doc.WorkbookPart!.Workbook
            .GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.Sheets>()!
            .Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
            .Select(s => s.Name!.Value!)
            .ToList();
    }
}
