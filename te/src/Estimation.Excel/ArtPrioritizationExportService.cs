namespace Estimation.Excel;

public sealed record ArtPrioritizationExportRow(
    int Ranking,
    bool IsAboveLine,
    string? JiraId,
    string? Summary,
    string? Status,
    string? EpicJiraId = null,
    string? EpicSummary = null);

public sealed record ArtPrioritizationExportInfo(
    string ArtName,
    string LevelName,
    string ScopeName,
    DateTime ExportedUtc,
    bool IsBusinessOutcomeLevel);

public static class ArtPrioritizationExportService
{
    public const string PortfolioEpicSheetName = "Portfolio Epics";
    public const string BusinessOutcomeSheetName = "Business Outcomes";
    public const string InfoSheetName = "Info";

    public const string RankingHeader = "Ranking";
    public const string LineHeader = "Line";
    public const string JiraIdHeader = "Jira ID";
    public const string SummaryHeader = "Summary";
    public const string StatusHeader = "Status";
    public const string EpicJiraIdHeader = "Epic Jira ID";
    public const string EpicHeader = "Epic";

    public const string AboveLine = "Above";
    public const string BelowLine = "Below";

    public const string InfoFieldHeader = "Field";
    public const string InfoValueHeader = "Value";
    public const string InfoArt = "ART";
    public const string InfoLevel = "Level";
    public const string InfoScope = "Scope";
    public const string InfoExported = "Exported (UTC)";

    public static readonly string[] PortfolioEpicHeaders =
    {
        RankingHeader,
        LineHeader,
        JiraIdHeader,
        SummaryHeader,
        StatusHeader,
    };

    public static readonly string[] BusinessOutcomeHeaders =
    {
        RankingHeader,
        LineHeader,
        JiraIdHeader,
        SummaryHeader,
        StatusHeader,
        EpicJiraIdHeader,
        EpicHeader,
    };

    public static string SheetName(bool isBusinessOutcomeLevel) =>
        isBusinessOutcomeLevel ? BusinessOutcomeSheetName : PortfolioEpicSheetName;

    public static byte[] Generate(ArtPrioritizationExportInfo info, IReadOnlyList<ArtPrioritizationExportRow> rows)
    {
        var workbook = new ExcelWorkbookBuilder();
        var headers = info.IsBusinessOutcomeLevel ? BusinessOutcomeHeaders : PortfolioEpicHeaders;
        var sheet = workbook.AddSheet(SheetName(info.IsBusinessOutcomeLevel));

        if (info.IsBusinessOutcomeLevel)
        {
            sheet.SetColumnWidths(10, 10, 18, 60, 15, 18, 50);
        }
        else
        {
            sheet.SetColumnWidths(10, 10, 18, 60, 15);
        }

        sheet.WriteColoredHeader(headers).FreezeTopRow();
        sheet.SetAutoFilter(headers.Length, rows.Count);
        sheet.AddWholeNumberValidation(
            0,
            2,
            Math.Max(2, rows.Count + 1),
            "Invalid ranking",
            "Ranking must be a whole number of 1 or more.",
            minimum: 1);

        foreach (var row in rows)
        {
            var cells = sheet.AddRow()
                .Number(row.Ranking)
                .Text(row.IsAboveLine ? AboveLine : BelowLine)
                .Text(row.JiraId)
                .Text(row.Summary)
                .Text(row.Status);

            if (info.IsBusinessOutcomeLevel)
            {
                cells.Text(row.EpicJiraId).Text(row.EpicSummary);
            }
        }

        var infoSheet = workbook.AddSheet(InfoSheetName);
        infoSheet.SetColumnWidths(18, 40);
        infoSheet.WriteColoredHeader(new[] { InfoFieldHeader, InfoValueHeader });
        infoSheet.AddRow().Text(InfoArt).Text(info.ArtName);
        infoSheet.AddRow().Text(InfoLevel).Text(info.LevelName);
        infoSheet.AddRow().Text(InfoScope).Text(info.ScopeName);
        infoSheet.AddRow().Text(InfoExported).Text(info.ExportedUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));

        return workbook.ToArray();
    }
}
