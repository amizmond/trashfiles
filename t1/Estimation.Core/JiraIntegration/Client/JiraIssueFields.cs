namespace Estimation.Core.JiraIntegration.Client;

/// <summary>
/// Canonical Jira field identifiers used both when requesting fields from the REST
/// search/get endpoints and when parsing the JSON responses. Keep one source of truth
/// here so that adding a new custom field is a single-line change.
/// </summary>
public static class JiraIssueFields
{
    public const string Summary = "summary";
    public const string Description = "description";
    public const string Status = "status";
    public const string IssueType = "issuetype";
    public const string Labels = "labels";
    public const string Updated = "updated";
    public const string Assignee = "assignee";
    public const string Priority = "priority";

    /// <summary>
    /// Builds the comma-separated <c>fields=</c> value for /rest/api/2/search and
    /// /rest/api/2/issue requests, combining the built-in fields above with the
    /// caller's configured custom-field IDs.
    /// </summary>
    public static string BuildFieldList(JiraSettings settings)
    {
        var fields = new List<string>
        {
            Summary,
            Description,
            Status,
            IssueType,
            Labels,
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
        TryAdd(fields, settings.AcceptanceCriteriaCustomFieldId);
        TryAdd(fields, settings.NavigatorIdCustomFieldId);
        TryAdd(fields, settings.FeatureLinkCustomFieldId);
        return string.Join(",", fields);
    }

    /// <summary>
    /// Minimal <c>fields=</c> list for sprint-metrics snapshots: the engine runs over every
    /// issue of every sprint each cycle, so it must not drag descriptions, labels, or avatars.
    /// </summary>
    public static string BuildSprintSnapshotFieldList(JiraSettings settings)
    {
        var fields = new List<string>
        {
            Summary,
            Status,
            IssueType,
        };
        TryAdd(fields, settings.StoryPointsCustomFieldId);
        TryAdd(fields, settings.SprintCustomFieldId);
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

/// <summary>
/// Field-mask keys used by <see cref="JiraUpdateIssueRequest.FieldsToUpdate"/> to declare
/// which properties the caller intends to write. Without an explicit mask, only non-null
/// values are sent so that absent properties cannot accidentally clear server-side data.
/// </summary>
public static class JiraUpdateFields
{
    public const string Summary = "summary";
    public const string Description = "description";
    public const string AcceptanceCriteria = "acceptanceCriteria";
    public const string NavigatorId = "navigatorId";
    public const string FeatureName = "featureName";
    public const string RagExplain = "ragExplain";
    public const string Labels = "labels";
    public const string ParentLink = "parentLink";
    public const string PlanningIncrement = "planningIncrement";
    public const string TargetStart = "targetStart";
    public const string TargetEnd = "targetEnd";
    public const string StoryPoints = "storyPoints";
    public const string GfedTeam = "gfedTeam";
    public const string Assignee = "assignee";
}
