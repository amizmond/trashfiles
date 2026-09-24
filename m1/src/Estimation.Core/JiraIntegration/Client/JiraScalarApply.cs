
using Estimation.Core.JiraIntegration.Models;

namespace Estimation.Core.JiraIntegration.Client;

public interface IJiraScalarSyncItem
{
    string JiraKey { get; }
    string? Summary { get; }
    string? Description { get; }
    string? AcceptanceCriteria { get; }
    string? NavigatorId { get; }
    string? IssueType { get; }
    string? Labels { get; }
    string? Components { get; }
    string? Status { get; }
    DateTime? JiraUpdated { get; }
    DateTime? TargetStart { get; }
    DateTime? TargetEnd { get; }
    int? StoryPoints { get; }
    HashSet<string>? PropertyMask { get; }
}

public static class JiraScalarApply
{
    public static bool ShouldWrite(HashSet<string>? mask, string property)
    {
        return mask is null || mask.Contains(property);
    }

    public static void ApplyToExisting(JiraIssue target, IJiraScalarSyncItem item)
    {
        var mask = item.PropertyMask;
        if (ShouldWrite(mask, JiraSyncProperties.Summary))
        {
            target.Summary = item.Summary ?? target.Summary;
        }
        if (ShouldWrite(mask, JiraSyncProperties.Description))
        {
            target.Description = item.Description;
        }
        if (ShouldWrite(mask, JiraSyncProperties.AcceptanceCriteria))
        {
            target.AcceptanceCriteria = item.AcceptanceCriteria;
        }
        if (ShouldWrite(mask, JiraSyncProperties.NavigatorId))
        {
            target.NavigatorId = item.NavigatorId;
        }
        if (ShouldWrite(mask, JiraSyncProperties.Labels))
        {
            target.Labels = item.Labels;
        }
        if (ShouldWrite(mask, JiraSyncProperties.Components))
        {
            target.Components = item.Components;
        }
        if (ShouldWrite(mask, JiraSyncProperties.Status))
        {
            target.Status = item.Status;
        }
        target.JiraUpdated = item.JiraUpdated;
        if (ShouldWrite(mask, JiraSyncProperties.TargetStart))
        {
            target.TargetStart = item.TargetStart;
        }
        if (ShouldWrite(mask, JiraSyncProperties.TargetEnd))
        {
            target.TargetEnd = item.TargetEnd;
        }
        if (ShouldWrite(mask, JiraSyncProperties.StoryPoints))
        {
            target.StoryPoints = item.StoryPoints;
        }
    }

    public static void ApplyToNew(JiraIssue target, IJiraScalarSyncItem item, string? projectKey)
    {
        target.JiraId = item.JiraKey;
        target.ProjectKey = JiraSyncItemMapping.ProjectKeyFromJiraKey(item.JiraKey) ?? projectKey;
        target.IssueType = item.IssueType;
        target.Summary = item.Summary ?? item.JiraKey;
        target.Description = item.Description;
        target.AcceptanceCriteria = item.AcceptanceCriteria;
        target.NavigatorId = item.NavigatorId;
        target.Labels = item.Labels;
        target.Components = item.Components;
        target.Status = item.Status;
        target.JiraUpdated = item.JiraUpdated;
        target.TargetStart = item.TargetStart;
        target.TargetEnd = item.TargetEnd;
        target.StoryPoints = item.StoryPoints;
    }
}
