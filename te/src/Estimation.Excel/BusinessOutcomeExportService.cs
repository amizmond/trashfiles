namespace Estimation.Excel;

public class BusinessOutcomeExportRow
{
    public string? JiraId { get; set; }
    public string? Summary { get; set; }
    public string? Status { get; set; }
    public int? Ranking { get; set; }
    public string? RankingText { get; set; }
    public string? RagStatus { get; set; }
    public string? RagExplain { get; set; }
    public string? CapitalProjects { get; set; }
    public string? PortfolioEpicJiraId { get; set; }
    public string? PortfolioEpicName { get; set; }
    public string? StrategicObjectiveJiraIds { get; set; }
    public string? Labels { get; set; }
}

public static class BusinessOutcomeExportService
{
    public static readonly string[] Headers =
    {
        "Jira ID",
        "Summary",
        "Status",
        "Ranking",
        "Rag Status",
        "Rag Explain",
        "ARTs involved",
        "Epic Id",
        "Epic",
        "Strategic Objective Jira Id",
        "Labels",
    };

    public static byte[] Generate(List<BusinessOutcomeExportRow> rows)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet("Business Outcomes");

        sheet.SetColumnWidths(18, 50, 15, 24, 14, 35, 35, 18, 40, 30, 30);
        sheet.WriteColoredHeader(Headers).FreezeTopRow();
        sheet.SetAutoFilter(Headers.Length, rows.Count);

        foreach (var row in rows)
        {
            var line = sheet.AddRow()
                .Text(row.JiraId)
                .Text(row.Summary)
                .Text(row.Status);
            (row.Ranking.HasValue ? line.Number(row.Ranking) : line.Text(row.RankingText, skipIfEmpty: true))
                .Text(row.RagStatus)
                .Text(row.RagExplain)
                .Text(row.CapitalProjects)
                .Text(row.PortfolioEpicJiraId)
                .Text(row.PortfolioEpicName)
                .Text(row.StrategicObjectiveJiraIds)
                .Text(row.Labels);
        }

        return workbook.ToArray();
    }
}
