namespace Estimation.Core.JiraLogic;

/// <summary>
/// Canonical property keys used to opt-in specific fields during a partial Jira sync.
/// Diff producers emit these strings as <c>DiffItem.PropertyName</c>, the dialog round-trips
/// them as a per-issue mask, and each <c>SyncFromJiraAsync</c> implementation consults the
/// mask to decide which fields to write.
/// </summary>
public static class JiraSyncProperties
{
    public const string Summary = "Summary";
    public const string Description = "Description";
    public const string Labels = "Labels";
    public const string Status = "Status";
    public const string TargetStart = "Target Start";
    public const string TargetEnd = "Target End";
    public const string StoryPoints = "Story Points";
    public const string Pi = "PI";
    public const string Teams = "Teams";
}
