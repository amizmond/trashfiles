namespace Estimation.HrReporting.Models;

public enum TeamsAnalysisExclusionKind
{
    Train = 1,
    Team = 2
}

public class TeamsAnalysisExclusion
{
    public const int DomainMaxLength = 200;
    public const int ValueMaxLength = 200;

    public int Id { get; set; }

    public string Domain { get; set; } = string.Empty;

    public TeamsAnalysisExclusionKind Kind { get; set; }

    public string Value { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string? CreatedBy { get; set; }
}
