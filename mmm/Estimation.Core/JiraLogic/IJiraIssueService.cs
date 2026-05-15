namespace Estimation.Core.JiraLogic;

public class JiraCreateIssueRequest
{
    public string ProjectKey { get; set; } = string.Empty;
    public string IssueType { get; set; } = JiraIssueTypes.Feature;
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FeatureName { get; set; }
    public List<string>? Labels { get; set; }
    public string? BusinessOutcomeKey { get; set; }
    public string? ParentJiraKey { get; set; }
    public string? PlanningIncrement { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }
}

public class JiraUpdateIssueRequest
{
    public string Summary { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FeatureName { get; set; }
    public string? IssueType { get; set; }
    public List<string>? Labels { get; set; }
    public string? BusinessOutcomeKey { get; set; }
    public string? ParentJiraKey { get; set; }
    public string? PlanningIncrement { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }
}

public class JiraIssueResponse
{
    public string Key { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? IssueType { get; set; }
    public string? FeatureName { get; set; }
    public List<string>? Labels { get; set; }
    public string? ParentLink { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }
    public string? GfedTeam { get; set; }
    public string? PlanningIncrement { get; set; }
}

public interface IJiraIssueService
{
    Task<string> CreateIssueAsync(string userName, JiraCreateIssueRequest request);
    Task UpdateIssueAsync(string userName, string issueKey, JiraUpdateIssueRequest request);
    Task<JiraIssueResponse?> GetIssueAsync(string userName, string issueKey);
    Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql);
    Task<List<JiraIssueResponse>> GetIssuesByKeysAsync(string userName, List<string> issueKeys);
}
