namespace Estimation.HrReporting.Models;

public enum OrgHealthComparison
{
    Above = 1,
    AtLeast = 2,
    Below = 3,
    AtMost = 4
}

public class OrgHealthTarget
{
    public const int KpiKeyMaxLength = 300;
    public const int TextMaxLength = 100;

    public int Id { get; set; }

    public string KpiKey { get; set; } = string.Empty;

    public string? Text { get; set; }

    public OrgHealthComparison? Comparison { get; set; }

    public decimal? Value { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }
}
