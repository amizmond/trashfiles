namespace Estimation.HrReporting.Data;

internal sealed class SourceAppPage
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Group { get; set; }

    public bool IsAdminOnly { get; set; }

    public int SortOrder { get; set; }

    public int ScopeMode { get; set; }
}
