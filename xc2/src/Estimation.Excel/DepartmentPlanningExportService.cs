namespace Estimation.Excel;

public sealed record DepartmentPlanningExportRow
{
    public required string Level { get; init; }

    public string? Art { get; init; }

    public string? StrategicObjective { get; init; }

    public string? PortfolioEpic { get; init; }

    public string? BusinessOutcome { get; init; }

    public string? BusinessEpic { get; init; }

    public string? JiraId { get; init; }

    public required string Title { get; init; }

    public string? Status { get; init; }

    public string? Rag { get; init; }

    public string? Ranks { get; init; }

    public int? Rank { get; init; }

    public string? Line { get; init; }

    public string? Arts { get; init; }

    public string? AlsoUnder { get; init; }

    public int? Teams { get; init; }

    public int? BusinessEpics { get; init; }

    public int? Done { get; init; }

    public double? ProgressPercent { get; init; }

    public double? Demand { get; init; }

    public double? Planned { get; init; }

    public double? NotPlanned { get; init; }

    public double? Drawdown { get; init; }

    public string? OverCapacity { get; init; }

    public double? Capacity { get; init; }

    public double? ShareOfCapacityPercent { get; init; }

    public int? Dependencies { get; init; }

    public int? DependenciesAtRisk { get; init; }

    public int? HygieneFailingFeatures { get; init; }

    public string? Funding { get; init; }

    public DateTime? TargetEnd { get; init; }

    public IReadOnlyList<double?> TeamPlanned { get; init; } = [];

    public IReadOnlyList<double?> TeamDrawdown { get; init; } = [];
}

public sealed record DepartmentPlanningExportInfo(
    string Department,
    string Pi,
    string View,
    string Filters,
    double Capacity,
    DateTime ExportedUtc,
    bool Drawdown,
    IReadOnlyList<string> TeamNames);

public static class DepartmentPlanningExportService
{
    public const string SheetName = "Department planning";
    public const string InfoSheetName = "Info";

    private const double TeamColumnWidth = 16;

    public static readonly string[] HierarchyHeaders =
    [
        "Level",
        "Strategic Objective",
        "Portfolio Epic",
        "Business Outcome",
        "Feature",
        "Jira ID",
        "Title",
        "Status",
        "RAG",
        "Rank",
        "Line",
        "ARTs",
        "Also under",
        "Teams",
        "Features",
        "Done",
        "Progress %",
        "Demand (SP)",
        "Planned (SP)",
        "Not planned (SP)",
        "Share of capacity %",
        "Dependencies",
        "Dependencies at risk",
        "Hygiene issues",
        "Funding",
        "Target end",
    ];

    public static readonly string[] DrawdownHeaders =
    [
        "Level",
        "ART",
        "Portfolio Epic",
        "Business Outcome",
        "Feature",
        "Jira ID",
        "Title",
        "Status",
        "RAG",
        "Rank",
        "Line",
        "Teams",
        "Features",
        "Done",
        "Progress %",
        "Demand (SP)",
        "Planned (SP)",
        "Not planned (SP)",
        "Drawdown (SP)",
        "Over capacity",
        "Capacity (SP)",
        "Share of capacity %",
        "Dependencies",
        "Dependencies at risk",
        "Hygiene issues",
        "Funding",
        "Target end",
    ];

    private static readonly double[] HierarchyWidths =
        [18, 34, 34, 34, 34, 14, 50, 16, 8, 12, 8, 18, 18, 7, 9, 7, 10, 12, 12, 14, 12, 13, 13, 12, 14, 12];

    private static readonly double[] DrawdownWidths =
        [18, 24, 34, 34, 34, 14, 50, 16, 8, 8, 8, 7, 9, 7, 10, 12, 12, 14, 14, 14, 13, 12, 13, 13, 12, 14, 12];

    public static List<string> Headers(DepartmentPlanningExportInfo info) => info.Drawdown
        ? [.. DrawdownHeaders, .. info.TeamNames.SelectMany(team => new[] { $"{team} planned (SP)", $"{team} drawdown (SP)" })]
        : [.. HierarchyHeaders, .. info.TeamNames.Select(team => $"{team} planned (SP)")];

    public static byte[] Generate(
        DepartmentPlanningExportInfo info,
        IReadOnlyList<DepartmentPlanningExportRow> rows,
        DepartmentPlanningExportRow? total = null)
    {
        var headers = Headers(info);
        var widths = info.Drawdown ? DrawdownWidths : HierarchyWidths;
        var workbook = new ExcelWorkbookBuilder();
        var sheet = workbook.AddSheet(SheetName);
        sheet.SetColumnWidths([.. widths, .. Enumerable.Repeat(TeamColumnWidth, headers.Count - widths.Length)]);
        sheet.WriteColoredHeader(headers).FreezeTopRow();
        sheet.SetAutoFilter(headers.Count, rows.Count);

        foreach (var row in rows)
        {
            Write(sheet.AddRow(), row, info);
        }

        if (total is not null)
        {
            Write(sheet.AddRow(), total, info);
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

    private static void Write(ExcelRowBuilder excelRow, DepartmentPlanningExportRow row, DepartmentPlanningExportInfo info)
    {
        if (info.Drawdown)
        {
            WriteDrawdown(excelRow, row, info.TeamNames.Count);
        }
        else
        {
            WriteHierarchy(excelRow, row, info.TeamNames.Count);
        }
    }

    private static void WriteHierarchy(ExcelRowBuilder excelRow, DepartmentPlanningExportRow row, int teamCount)
    {
        excelRow
            .Text(row.Level)
            .Text(row.StrategicObjective)
            .Text(row.PortfolioEpic)
            .Text(row.BusinessOutcome)
            .Text(row.BusinessEpic)
            .Text(row.JiraId)
            .Text(row.Title)
            .Text(row.Status)
            .Text(row.Rag)
            .Text(row.Ranks)
            .Text(row.Line)
            .Text(row.Arts)
            .Text(row.AlsoUnder)
            .Number(row.Teams)
            .Number(row.BusinessEpics)
            .Number(row.Done)
            .Number(Round(row.ProgressPercent))
            .Number(row.Demand)
            .Number(row.Planned)
            .Number(row.NotPlanned)
            .Number(Round(row.ShareOfCapacityPercent))
            .Number(row.Dependencies)
            .Number(row.DependenciesAtRisk)
            .Number(row.HygieneFailingFeatures)
            .Text(row.Funding)
            .Date(row.TargetEnd);

        for (var i = 0; i < teamCount; i++)
        {
            excelRow.Number(At(row.TeamPlanned, i));
        }
    }

    private static void WriteDrawdown(ExcelRowBuilder excelRow, DepartmentPlanningExportRow row, int teamCount)
    {
        excelRow
            .Text(row.Level)
            .Text(row.Art)
            .Text(row.PortfolioEpic)
            .Text(row.BusinessOutcome)
            .Text(row.BusinessEpic)
            .Text(row.JiraId)
            .Text(row.Title)
            .Text(row.Status)
            .Text(row.Rag)
            .Number(row.Rank)
            .Text(row.Line)
            .Number(row.Teams)
            .Number(row.BusinessEpics)
            .Number(row.Done)
            .Number(Round(row.ProgressPercent))
            .Number(row.Demand)
            .Number(row.Planned)
            .Number(row.NotPlanned)
            .Number(row.Drawdown)
            .Text(row.OverCapacity)
            .Number(row.Capacity)
            .Number(Round(row.ShareOfCapacityPercent))
            .Number(row.Dependencies)
            .Number(row.DependenciesAtRisk)
            .Number(row.HygieneFailingFeatures)
            .Text(row.Funding)
            .Date(row.TargetEnd);

        for (var i = 0; i < teamCount; i++)
        {
            excelRow.Number(At(row.TeamPlanned, i)).Number(At(row.TeamDrawdown, i));
        }
    }

    private static double? Round(double? value) => value is { } number ? Math.Round(number, 1) : null;

    private static double? At(IReadOnlyList<double?> values, int index) => index < values.Count ? values[index] : null;
}
