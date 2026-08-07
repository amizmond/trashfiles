using Estimation.Core.Features.Services;
using Estimation.Core.Train.Services;

namespace Estimation.Core.JiraIntegration.Client;

/// <summary>
/// Extension methods that project a <see cref="JiraIssueResponse"/> (the wire-format DTO) into
/// the entity-specific sync items consumed by *Service.SyncFromJiraAsync. UI call sites used
/// to inline this mapping with subtle drift between pages — this is the single source of truth.
/// </summary>
public static class JiraSyncItemMapping
{
    public static JiraFeatureSyncItem ToFeatureSyncItem(
        this JiraIssueResponse j,
        HashSet<string>? propertyMask = null)
    {
        return new JiraFeatureSyncItem(
            JiraKey: j.Key,
            Summary: j.Summary,
            Description: j.Description,
            AcceptanceCriteria: j.AcceptanceCriteria,
            NavigatorId: j.NavigatorId,
            IssueType: j.IssueType,
            Labels: JoinLabels(j.Labels),
            FeatureName: j.FeatureName,
            RagExplain: j.RagExplain,
            ParentLink: j.ParentLink,
            Status: j.Status,
            JiraUpdated: j.Updated,
            TargetStart: j.TargetStart,
            TargetEnd: j.TargetEnd,
            StoryPoints: j.StoryPoints,
            GfedTeam: j.GfedTeam,
            PlanningIncrement: j.PlanningIncrement)
        {
            PropertyMask = propertyMask,
        };
    }

    public static JiraEpicSyncItem ToEpicSyncItem(
        this JiraIssueResponse j,
        HashSet<string>? propertyMask = null)
    {
        return new JiraEpicSyncItem(
            JiraKey: j.Key,
            Summary: j.Summary,
            Description: j.Description,
            AcceptanceCriteria: j.AcceptanceCriteria,
            NavigatorId: j.NavigatorId,
            IssueType: j.IssueType,
            Labels: JoinLabels(j.Labels),
            ParentLink: j.ParentLink,
            Status: j.Status,
            JiraUpdated: j.Updated,
            TargetStart: j.TargetStart,
            TargetEnd: j.TargetEnd,
            StoryPoints: j.StoryPoints)
        {
            PropertyMask = propertyMask,
        };
    }

    public static JiraSyncItem ToStrategicObjectiveSyncItem(
        this JiraIssueResponse j,
        HashSet<string>? propertyMask = null)
    {
        return new JiraSyncItem(
            JiraKey: j.Key,
            Summary: j.Summary,
            Description: j.Description,
            AcceptanceCriteria: j.AcceptanceCriteria,
            NavigatorId: j.NavigatorId,
            IssueType: j.IssueType,
            Labels: JoinLabels(j.Labels),
            Status: j.Status,
            JiraUpdated: j.Updated,
            TargetStart: j.TargetStart,
            TargetEnd: j.TargetEnd,
            StoryPoints: j.StoryPoints)
        {
            PropertyMask = propertyMask,
        };
    }

    public static string? JoinLabels(List<string>? labels)
    {
        return labels is { Count: > 0 } ? string.Join(",", labels) : null;
    }

    public static string? JoinComponents(List<string>? components)
    {
        return components is { Count: > 0 } ? string.Join(",", components) : null;
    }

    /// <summary>
    /// Derives the Jira project key from an issue key by taking the prefix before the first
    /// dash (e.g. <c>PROJ-123</c> → <c>PROJ</c>). Returns null when the key is missing or
    /// has no dash.
    /// </summary>
    public static string? ProjectKeyFromJiraKey(string? jiraKey)
    {
        if (string.IsNullOrWhiteSpace(jiraKey))
        {
            return null;
        }
        var dash = jiraKey.IndexOf('-');
        return dash > 0 ? jiraKey[..dash] : null;
    }
}
