using MudBlazor;

namespace Estimation.Components.Shared;

public static class JiraIssueTypeHelper
{
    public static Color IssueTypeColor(string? issueType)
    {
        return (issueType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "bug" => Color.Error,
            "task" => Color.Info,
            "story" => Color.Success,
            "feature" => Color.Primary,
            _ => Color.Warning,
        };
    }
}
