namespace Estimation.HrReporting;

public static class HrReportingRoutes
{
    public const string PageKey = "HrHeadcount";

    public const string NavDisplayName = "Headcount";

    public const string NavGroup = "HR Reporting";

    public const int NavSortOrder = 50;

    public const int ScopeMode = 1;

    public const int AppPageId = 36;

    public const string MetricsPageKey = "HrMetrics";

    public const string TeamsAnalysisPageKey = "HrTeamsAnalysis";

    public const string OrgHealthPageKey = "HrOrgHealth";

    public const string Headcount = "/hr-reporting/headcount";

    public const string UploadPreview = $"{Headcount}/upload";

    public const string Compare = $"{Headcount}/compare";

    public const string Person = $"{Headcount}/person";

    public const string Metrics = "/hr-reporting/metrics";

    public const string TeamsAnalysis = "/hr-reporting/teams-analysis";

    public const string OrgHealth = "/hr-reporting/org-health";

    public static readonly IReadOnlyList<NavPage> NavPages =
    [
        new(AppPageId, PageKey, NavDisplayName, NavSortOrder),
        new(37, MetricsPageKey, "Metrics", 51),
        new(38, TeamsAnalysisPageKey, "Teams Analysis", 52),
        new(39, OrgHealthPageKey, "Organization Health", 53)
    ];

    public static string OrgHealthUrl(int? newer, int? older)
    {
        var query = new List<string>();
        if (newer is not null)
        {
            query.Add($"b={newer}");
        }

        if (older is not null)
        {
            query.Add($"a={older}");
        }

        return query.Count == 0 ? OrgHealth : $"{OrgHealth}?{string.Join('&', query)}";
    }

    public static string CompareUrl(int? a, int? b)
    {
        var query = new List<string>();
        if (a is not null)
        {
            query.Add($"a={a}");
        }

        if (b is not null)
        {
            query.Add($"b={b}");
        }

        return query.Count == 0 ? Compare : $"{Compare}?{string.Join('&', query)}";
    }

    public static string HeadcountUrl(int? uploadId) => WithUpload(Headcount, uploadId);

    public static string PersonUrl(int? uploadId, string brid)
    {
        var url = $"{Person}?brid={Uri.EscapeDataString(brid)}";
        return uploadId is null ? url : $"{url}&upload={uploadId}";
    }

    public static string WithUpload(string route, int? uploadId) =>
        uploadId is null ? route : $"{route}?upload={uploadId}";

    public sealed record NavPage(int Id, string Key, string DisplayName, int SortOrder);
}
