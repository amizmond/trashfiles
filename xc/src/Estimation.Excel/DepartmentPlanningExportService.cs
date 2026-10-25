namespace Estimation.Excel;

public sealed record DepartmentPlanningExportRow(
    string Level,
    string? StrategicObjective,
    string? PortfolioEpic,
    string? BusinessOutcome,
    string? BusinessEpic,
    string? JiraId,
    string Title,
    string? Status,
    string? Rag,
    string? Rank,
    string? Line,
    string? Arts,
    int Teams,
    int BusinessEpics,
    int Done,
    double ProgressPercent,
    double Demand,
    double Planned,
    double NotPlanned,
    double? Capacity,
    double? ShareOfCapacityPercent,
    int Dependencies,
    int DependenciesAtRisk,
    int DataQualityIssues,
    DateTime? TargetEnd);

public sealed record DepartmentPlanningExportInfo(
    string Department,
    string Pi,
    string View,
    string Filters,
    double Capacity,
    DateTime ExportedUtc);

public static class DepartmentPlanningExportService
{
    public const string SheetName = "Department planning";
    public const string InfoSheetName = "Info";

    public static readonly string[] Headers =
    [
        "Level",
        "Strategic Objective",
        "Portfolio Epic",
        "Business Outcome",
        "Business Epic",
        "Jira ID",
        "Title",
        "Status",
        "RAG",
        "Rank",
        "Line",
        "ARTs",
        "Teams",
        "Business Epics",
        "Done",
        "Progress %",
        "Demand (SP)",
        "Planned (SP)",
        "Not planned (SP)",
        "Capacity (SP)",
        "Share of capacity %",
        "Dependencies",
        "Dependencies at risk",
        "Data quality issues",
        "Target end",
    ];

    public static byte[] Generate(DepartmentPlanningExportInfo info, IReadOnlyList<DepartmentPlanningExportRow> rows)
    {
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet(SheetName);
        sheet.SetColumnWidths(18, 34, 34, 34, 34, 14, 50, 16, 8, 12, 8, 18, 7, 9, 7, 10, 12, 12, 14, 13, 12, 13, 13, 12, 12);
        sheet.WriteColoredHeader(Headers).FreezeTopRow();
        sheet.SetAutoFilter(Headers.Length, rows.Count);

        foreach (var row in rows)
        {
            sheet.AddRow()
                .Text(row.Level)
                .Text(row.StrategicObjective)
                .Text(row.PortfolioEpic)
                .Text(row.BusinessOutcome)
                .Text(row.BusinessEpic)
                .Text(row.JiraId)
                .Text(row.Title)
                .Text(row.Status)
                .Text(row.Rag)
                .Text(row.Rank)
                .Text(row.Line)
                .Text(row.Arts)
                .Number(row.Teams)
                .Number(row.BusinessEpics)
                .Number(row.Done)
                .Number(Math.Round(row.ProgressPercent, 1))
                .Number(row.Demand)
                .Number(row.Planned)
                .Number(row.NotPlanned)
                .Number(row.Capacity)
                .Number(row.ShareOfCapacityPercent is { } share ? Math.Round(share, 1) : null)
                .Number(row.Dependencies)
                .Number(row.DependenciesAtRisk)
                .Number(row.DataQualityIssues)
                .Date(row.TargetEnd);
        }

        var infoSheet = workbook.AddSheet(InfoSheetName);
        infoSheet.SetColumnWidths(22, 60);
        infoSheet.WriteColoredHeader(new[] { "Field", "Value" });
        infoSheet.AddRow().Text("Department").Text(info.Department);
        infoSheet.AddRow().Text("Planning Increment").Text(info.Pi);
        infoSheet.AddRow().Text("View").Text(info.View);
        infoSheet.AddRow().Text("Filters").Text(info.Filters);
        infoSheet.AddRow().Text("Capacity (SP)").Number(info.Capacity);
        infoSheet.AddRow().Text("Exported (UTC)").Text(info.ExportedUtc.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));

        return workbook.ToArray();
    }
}
