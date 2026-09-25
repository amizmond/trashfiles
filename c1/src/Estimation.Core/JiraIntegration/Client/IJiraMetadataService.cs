namespace Estimation.Core.JiraIntegration.Client;

public class JiraLabel
{
    public string Name { get; set; } = string.Empty;
}

public class JiraStatus
{
    public string Name { get; set; } = string.Empty;
}

public class JiraProject
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public interface IJiraMetadataService
{
    Task<List<JiraLabel>> GetLabelsAsync(string userName, string? projectKey = null);
    Task<List<JiraStatus>> GetStatusesAsync(string userName, string projectKey);
    Task<JiraProject?> GetProjectAsync(string userName, string projectKey);
}
