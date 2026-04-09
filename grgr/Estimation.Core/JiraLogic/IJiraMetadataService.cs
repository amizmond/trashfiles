namespace Estimation.Core.JiraLogic;

public class JiraLabel
{
    public string Name { get; set; } = string.Empty;
}

public interface IJiraMetadataService
{
    Task<List<JiraLabel>> GetLabelsAsync(string userName, string? projectKey = null);
}
