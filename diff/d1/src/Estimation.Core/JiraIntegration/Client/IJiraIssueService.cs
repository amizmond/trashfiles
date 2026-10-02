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
    public List<string>? FixVersions { get; set; }
    public string? BusinessOutcomeKey { get; set; }
    public string? ParentJiraKey { get; set; }
    public string? PlanningIncrement { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }

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
    public string? RagStatus { get; set; }
    public string? IssueType { get; set; }
    public List<string>? Labels { get; set; }
    public List<string>? FixVersions { get; set; }
    public string? BusinessOutcomeKey { get; set; }
    public string? ParentJiraKey { get; set; }
    public string? PlanningIncrement { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }

    public List<string>? GfedTeams { get; set; }

    public string? AssigneeUserName { get; set; }

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
    public string? RagStatus { get; set; }
    public List<string>? Labels { get; set; }
    public List<string>? Components { get; set; }
    public List<string>? FixVersions { get; set; }
    public string? ParentLink { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }
    public string? GfedTeam { get; set; }
    public string? PlanningIncrement { get; set; }

    public string? PriorityName { get; set; }

    public string? PriorityIconUrl { get; set; }

    public string? FeatureLink { get; set; }

    public string? AssigneeDisplayName { get; set; }

    public string? AssigneeUserName { get; set; }

    public string? AssigneeKey { get; set; }

    public string? AssigneeAvatarUrl { get; set; }
}

public class JiraTransition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ToStatusName { get; set; } = string.Empty;
}

public class JiraSearchPage
{
    public List<JiraIssueResponse> Issues { get; set; } = [];

    public int Total { get; set; }

    public bool IsTruncated => Total > Issues.Count;
}

public interface IJiraIssueService
{
    Task<string> CreateIssueAsync(string userName, JiraCreateIssueRequest request);
    Task UpdateIssueAsync(string userName, string issueKey, JiraUpdateIssueRequest request);

    Task UpdateIssueWithStatusAsync(string userName, string issueKey, JiraUpdateIssueRequest request, string? targetStatusName);

    Task<JiraIssueResponse?> GetIssueAsync(string userName, string issueKey);
    Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql);

    Task<JiraSearchPage> SearchIssuesPageAsync(string userName, string jql, int maxResults);
    Task<List<JiraIssueResponse>> GetIssuesByKeysAsync(string userName, List<string> issueKeys);
    Task<List<JiraTransition>> GetTransitionsAsync(string userName, string issueKey);
    Task<bool> TransitionToStatusAsync(string userName, string issueKey, string targetStatusName);
}
