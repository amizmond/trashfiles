namespace Estimation.Core.JiraLogic;

public class JiraProjectItem
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class JiraIssueTypeItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class JiraBusinessOutcomeItem
{
    public string Key { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public interface IJiraMetadataService
{
    Task<List<JiraProjectItem>> GetProjectsAsync(string userName);
    Task<List<JiraIssueTypeItem>> GetIssueTypesAsync(string userName, string projectKey);
    Task<List<string>> GetLabelsAsync(string userName, string projectKey);
    Task<List<JiraBusinessOutcomeItem>> GetBusinessOutcomesAsync(string userName, string projectKey);
}
