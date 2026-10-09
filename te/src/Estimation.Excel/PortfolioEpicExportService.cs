namespace Estimation.Excel;

public class PortfolioEpicExportRow
{
    public string? JiraId { get; set; }
    public string? Summary { get; set; }
    public string? Status { get; set; }
    public int? Ranking { get; set; }
    public string? RankingText { get; set; }
    public string? StrategicObjectiveJiraIds { get; set; }
    public string? StrategicObjectiveNames { get; set; }
    public string? Labels { get; set; }
}

public static class PortfolioEpicExportService
{
    public static readonly string[] Headers =
    {
        "Jira ID",
        "Summary",
        "Status",
        "Ranking",
        "Strategic Objective Jira Id",
        "Strategic Objective Name",
        "Labels",
    };

    public static byte[] Generate(List<PortfolioEpicExportRow> rows)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet("Portfolio Epics");

        sheet.SetColumnWidths(18, 50, 15, 24, 30, 50, 30);
        sheet.WriteColoredHeader(Headers).FreezeTopRow();
        sheet.SetAutoFilter(Headers.Length, rows.Count);

        foreach (var row in rows)
        {
            var line = sheet.AddRow()
                .Text(row.JiraId)
                .Text(row.Summary)
                .Text(row.Status);
            (row.Ranking.HasValue ? line.Number(row.Ranking) : line.Text(row.RankingText, skipIfEmpty: true))
                .Text(row.StrategicObjectiveJiraIds)
                .Text(row.StrategicObjectiveNames)
                .Text(row.Labels);
        }

        return workbook.ToArray();
    }
}
