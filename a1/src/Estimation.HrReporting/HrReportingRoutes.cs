namespace Estimation.HrReporting;

public static class HrReportingRoutes
{
    public const string PageKey = "HrHeadcount";

    public const string NavDisplayName = "Headcount";

    public const string NavGroup = "HR Reporting";

    public const int NavSortOrder = 50;

    public const int ScopeMode = 1;

    public const int AppPageId = 36;

    public const string Headcount = "/hr-reporting/headcount";

    public const string UploadPreview = $"{Headcount}/upload";

    public const string Compare = $"{Headcount}/compare";

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

    public static string HeadcountUrl(int? uploadId) =>
        uploadId is null ? Headcount : $"{Headcount}?upload={uploadId}";
}
