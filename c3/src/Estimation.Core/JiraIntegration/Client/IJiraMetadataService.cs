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

public sealed record JiraLabelRefreshResult(string ProjectKey, IReadOnlyList<JiraLabel> Labels, string? Error)
{
    public bool Succeeded => Error is null;

    public static JiraLabelRefreshResult Success(string projectKey, IReadOnlyList<JiraLabel> labels) =>
        new(projectKey, labels, null);

    public static JiraLabelRefreshResult Failure(string projectKey, string error) =>
        new(projectKey, Array.Empty<JiraLabel>(), error);
}

public interface IJiraMetadataService
{
    Task<List<JiraLabel>> GetLabelsAsync(string userName, string projectKey);
    Task<JiraLabelRefreshResult> RefreshLabelsAsync(string projectKey, CancellationToken cancellationToken = default);
    Task<List<JiraStatus>> GetStatusesAsync(string userName, string projectKey);
    Task<JiraProject?> GetProjectAsync(string userName, string projectKey);
}
