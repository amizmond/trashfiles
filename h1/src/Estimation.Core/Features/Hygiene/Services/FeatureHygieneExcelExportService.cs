using Estimation.Core.Features.Hygiene.Models;
using Estimation.Excel;

namespace Estimation.Core.Features.Hygiene.Services;

public static class FeatureHygieneExcelExportService
{
    private const string FeaturesSheetName = "Features";
    private const string RulesSheetName = "Rules";
    private const string InfoSheetName = "Info";

    private const string FailedFillColor = "#FDE8E8";
    private const string HealthyFillColor = "#E6F4EA";

    public static byte[] Build(FeatureHygieneReport report, IReadOnlyList<FeatureHygieneRow> rows)
    {
        var rules = report.Rules.Where(r => r.IsEnabled).ToList();

        var headers = new List<string> { "Jira ID", "Feature name", "Summary", "Jira status", "Teams", "PI", "Healthy", "Failed checks" };
        var widths = new List<double> { 14, 32, 48, 14, 24, 12, 10, 14 };

        foreach (var rule in rules)
        {
            headers.Add(HygieneRuleText.Describe(rule));
            widths.Add(32);
        }

        var builder = new ExcelWorkbookBuilder();
        var failedStyle = builder.GetOrCreateFillStyle(FailedFillColor);
        var healthyStyle = builder.GetOrCreateFillStyle(HealthyFillColor);

        var sheet = builder.AddSheet(FeaturesSheetName);
        sheet.SetColumnWidths(widths.ToArray());
        sheet.WriteColoredHeader(headers).FreezeTopRow();
        sheet.SetAutoFilter(headers.Count, rows.Count);

        foreach (var row in rows)
        {
            var excelRow = sheet.AddRow()
                .Text(row.JiraId)
                .Text(row.Name)
                .Text(row.Summary)
                .Text(row.Status)
                .Text(row.Teams)
                .Text(row.PiName);

            if (row.IsHealthy)
            {
                excelRow.StyledText("Yes", healthyStyle);
            }
            else
            {
                excelRow.StyledText("No", failedStyle);
            }

            excelRow.Number(row.Failures.Count);

            foreach (var rule in rules)
            {
                var failure = row.Failures.FirstOrDefault(f => f.RuleId == rule.Id);

                if (failure is null)
                {
                    excelRow.Text(null);
                }
                else
                {
                    excelRow.StyledText(FailureText(failure), failedStyle);
                }
            }
        }

        var rulesHeaders = new[] { "#", "Rule", "Field", "Check", "Parameters", "Message", "Features failing" };
        var rulesSheet = builder.AddSheet(RulesSheetName);
        rulesSheet.SetColumnWidths(5, 60, 22, 22, 40, 40, 16);
        rulesSheet.WriteColoredHeader(rulesHeaders).FreezeTopRow();
        rulesSheet.SetAutoFilter(rulesHeaders.Length, rules.Count);

        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var kind = HygieneFieldCatalog.KindOf(rule.Field);

            rulesSheet.AddRow()
                .Number(index + 1)
                .Text(HygieneRuleText.Describe(rule))
                .Text(HygieneFieldCatalog.DisplayName(rule.Field))
                .Text(HygieneChecks.DisplayName(rule.Check, kind))
                .Text(HygieneRuleText.DescribeParameters(rule.Field, rule.Check, rule.Parameters))
                .Text(rule.Message)
                .Number(rows.Count(r => r.Failures.Any(f => f.RuleId == rule.Id)));
        }

        var infoRows = new (string Field, string? Text, int? Number)[]
        {
            ("ART", report.ArtName, null),
            ("PI", report.PiName, null),
            ("Rules", null, rules.Count),
            ("Features in the PI", null, report.Total),
            ("Healthy", null, report.Healthy),
            ("Not healthy", null, report.Unhealthy),
            ("Rows exported", null, rows.Count)
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

    private static string FailureText(HygieneFailure failure) =>
        failure.ActualValue is null
            ? failure.Reason
            : $"{failure.Reason} — {failure.ActualValue}";
}
