namespace Estimation.Core.JiraIntegration.Client.JiraSync;

/// <summary>
/// Logical Jira field names referenced by <see cref="JiraSyncAttribute.Field"/>. These are the
/// normalized fields produced by <see cref="JiraIssueParser"/> / exposed on
/// <see cref="JiraIssueResponse"/> — NOT raw Jira custom-field ids. The environment-specific
/// custom-field ids stay in <see cref="JiraSettings"/>; an attribute just names the logical field
/// it is fed from, so a field-id change never touches the model.
/// </summary>
public static class JiraSyncFields
{
    // Scalar fields (copied directly onto the entity by JiraSyncApplier).
    public const string IssueType = "IssueType";
    public const string Summary = "Summary";
    public const string Description = "Description";
    public const string AcceptanceCriteria = "AcceptanceCriteria";
    public const string NavigatorId = "NavigatorId";
    public const string Labels = "Labels";
    public const string Components = "Components";
    public const string Status = "Status";
    public const string JiraUpdated = "JiraUpdated";
    public const string TargetStart = "TargetStart";
    public const string TargetEnd = "TargetEnd";
    public const string StoryPoints = "StoryPoints";
    public const string FeatureName = "FeatureName";
    public const string RagExplain = "RagExplain";

    // Relationship / shaped fields resolved by DB-backed converters (wired in Phase C).
    public const string ParentLink = "ParentLink";
    public const string GfedTeam = "GfedTeam";
    public const string PlanningIncrement = "PlanningIncrement";
}
