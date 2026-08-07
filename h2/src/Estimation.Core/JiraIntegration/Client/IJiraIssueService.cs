namespace Estimation.Core.JiraIntegration.Client;

public class JiraCreateIssueRequest
{
    public string ProjectKey { get; set; } = string.Empty;
    public string IssueType { get; set; } = JiraIssueTypes.Feature;
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? NavigatorId { get; set; }
    public string? FeatureName { get; set; }
    public string? RagExplain { get; set; }
    public List<string>? Labels { get; set; }
    public string? BusinessOutcomeKey { get; set; }
    public string? ParentJiraKey { get; set; }
    public string? PlanningIncrement { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }

    /// <summary>
    /// Names to write into Jira's GfedTeam multi-option field. Each entry should already be
    /// the canonical Jira value (<see cref="Team.FullName"/> when present, else
    /// <see cref="Team.Name"/>). Empty/null means the field is not sent on create.
    /// </summary>
    public List<string>? GfedTeams { get; set; }
}

public class JiraUpdateIssueRequest
{
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? NavigatorId { get; set; }
    public string? FeatureName { get; set; }
    public string? RagExplain { get; set; }
    public string? IssueType { get; set; }
    public List<string>? Labels { get; set; }
    public string? BusinessOutcomeKey { get; set; }
    public string? ParentJiraKey { get; set; }
    public string? PlanningIncrement { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }

    /// <summary>
    /// Names to write into Jira's GfedTeam multi-option field. Each entry should already be
    /// the canonical Jira value (<see cref="Team.FullName"/> when present, else
    /// <see cref="Team.Name"/>). When the field-mask includes
    /// <see cref="JiraUpdateFields.GfedTeam"/> and this list is null/empty, the field is
    /// explicitly cleared on Jira.
    /// </summary>
    public List<string>? GfedTeams { get; set; }

    /// <summary>
    /// Jira login (Server/DC <c>name</c> attribute, i.e. the Windows account without domain) to
    /// assign the issue to. With <see cref="JiraUpdateFields.Assignee"/> in the field-mask a null
    /// value unassigns the issue.
    /// </summary>
    public string? AssigneeUserName { get; set; }

    /// <summary>
    /// Optional opt-in mask of <see cref="JiraUpdateFields"/> keys. When set, every listed
    /// field is sent — including with a null value, which clears the field on Jira. When
    /// null/empty, only non-null properties are sent so absent values cannot accidentally
    /// wipe server state.
    /// </summary>
    public HashSet<string>? FieldsToUpdate { get; set; }
}

public class JiraIssueResponse
{
    public string Key { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? NavigatorId { get; set; }
    public string? Status { get; set; }
    public string? IssueType { get; set; }
    public string? FeatureName { get; set; }
    public string? RagExplain { get; set; }
    public List<string>? Labels { get; set; }
    public List<string>? Components { get; set; }
    public string? ParentLink { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }
    public string? GfedTeam { get; set; }
    public string? PlanningIncrement { get; set; }

    /// <summary>Jira priority name (e.g. "Major"); null when the issue has no priority.</summary>
    public string? PriorityName { get; set; }

    /// <summary>Absolute URL of the priority icon; null when not provided.</summary>
    public string? PriorityIconUrl { get; set; }

    /// <summary>Feature Link custom field (issue key of the linked feature); null when not set.</summary>
    public string? FeatureLink { get; set; }

    /// <summary>Jira assignee display name; null when the issue is unassigned.</summary>
    public string? AssigneeDisplayName { get; set; }

    /// <summary>Jira assignee login (Server/DC <c>name</c> attribute); null when unassigned.</summary>
    public string? AssigneeUserName { get; set; }

    /// <summary>
    /// Jira assignee user key (Server/DC <c>key</c> attribute); null when unassigned.
    /// Matches <see cref="Estimation.Core.Resources.Models.HumanResource.EmployeeNumber"/> in this instance.
    /// </summary>
    public string? AssigneeKey { get; set; }

    /// <summary>Absolute URL of the assignee's 48x48 avatar; null when unassigned or not provided.</summary>
    public string? AssigneeAvatarUrl { get; set; }
}

public class JiraTransition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ToStatusName { get; set; } = string.Empty;
}

/// <summary>Result of a capped JQL search: at most the requested number of issues plus Jira's total match count.</summary>
public class JiraSearchPage
{
    public List<JiraIssueResponse> Issues { get; set; } = [];

    /// <summary>Total number of issues matching the JQL on the server (may exceed <see cref="Issues"/>.Count).</summary>
    public int Total { get; set; }

    public bool IsTruncated => Total > Issues.Count;
}

public interface IJiraIssueService
{
    Task<string> CreateIssueAsync(string userName, JiraCreateIssueRequest request);
    Task UpdateIssueAsync(string userName, string issueKey, JiraUpdateIssueRequest request);

    /// <summary>
    /// Convenience overload that updates the issue's fields and, if <paramref name="targetStatusName"/>
    /// is non-empty, performs the appropriate Jira transition. Status changes cannot go through
    /// the regular field-update endpoint and must use /transitions.
    /// </summary>
    Task UpdateIssueWithStatusAsync(string userName, string issueKey, JiraUpdateIssueRequest request, string? targetStatusName);

    Task<JiraIssueResponse?> GetIssueAsync(string userName, string issueKey);
    Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql);

    /// <summary>
    /// Runs a JQL search but stops paging once <paramref name="maxResults"/> issues are collected,
    /// so an overly broad query cannot pull the whole server. The result carries Jira's total
    /// match count so callers can tell the user when the list was cut off.
    /// </summary>
    Task<JiraSearchPage> SearchIssuesPageAsync(string userName, string jql, int maxResults);
    Task<List<JiraIssueResponse>> GetIssuesByKeysAsync(string userName, List<string> issueKeys);
    Task<List<JiraTransition>> GetTransitionsAsync(string userName, string issueKey);
    Task<bool> TransitionToStatusAsync(string userName, string issueKey, string targetStatusName);
}
