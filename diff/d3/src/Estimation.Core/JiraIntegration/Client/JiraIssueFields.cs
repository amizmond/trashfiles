namespace Estimation.Core.JiraIntegration.Client;

public static class JiraIssueFields
{
    public const string Summary = "summary";
    public const string Description = "description";
    public const string Status = "status";
    public const string IssueType = "issuetype";
    public const string Labels = "labels";
    public const string Components = "components";
    public const string FixVersions = "fixVersions";
    public const string Updated = "updated";
    public const string Assignee = "assignee";
    public const string Priority = "priority";
    public const string IssueLinks = "issuelinks";

    public static string BuildFieldList(JiraSettings settings, bool includeLinks = false)
    {
        var fields = new List<string>
        {
            Summary,
            Description,
            Status,
            IssueType,
            Labels,
            Components,
            FixVersions,
            Updated,
            Assignee,
            Priority,
        };
        TryAdd(fields, settings.FeatureNameCustomFieldId);
        TryAdd(fields, settings.ParentLinkCustomFieldId);
        TryAdd(fields, settings.TargetStartCustomFieldId);
        TryAdd(fields, settings.TargetEndCustomFieldId);
        TryAdd(fields, settings.StoryPointsCustomFieldId);
        TryAdd(fields, settings.GfedTeamCustomFieldId);
        TryAdd(fields, settings.PlanningIncrementCustomFieldId);
        TryAdd(fields, settings.RagExplainCustomFieldId);
        TryAdd(fields, settings.RagStatusCustomFieldId);
        TryAdd(fields, settings.AcceptanceCriteriaCustomFieldId);
        TryAdd(fields, settings.NavigatorIdCustomFieldId);
        TryAdd(fields, settings.FeatureLinkCustomFieldId);
        if (includeLinks)
        {
            fields.Add(IssueLinks);
        }
        return string.Join(",", fields);
    }

    private static void TryAdd(List<string> list, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            list.Add(value);
        }
    }
}

public static class JiraUpdateFields
{
    public const string Summary = "summary";
    public const string Description = "description";
    public const string AcceptanceCriteria = "acceptanceCriteria";
    public const string NavigatorId = "navigatorId";
    public const string FeatureName = "featureName";
    public const string RagExplain = "ragExplain";
    public const string RagStatus = "ragStatus";
    public const string Labels = "labels";
    public const string FixVersions = "fixVersions";
    public const string ParentLink = "parentLink";
    public const string PlanningIncrement = "planningIncrement";
    public const string TargetStart = "targetStart";
    public const string TargetEnd = "targetEnd";
    public const string StoryPoints = "storyPoints";
    public const string GfedTeam = "gfedTeam";
    public const string Assignee = "assignee";
}
