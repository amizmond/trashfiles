namespace Estimation.Core.JiraIntegration.Models;

public sealed record LinkedIssue(string JiraLinkId, string TypeName, string Relation, bool IsOutward, string Key)
{
    public int? FeatureId { get; init; }

    public string? Name { get; init; }

    public string? Status { get; init; }

    public bool InTool => FeatureId is not null;
}
