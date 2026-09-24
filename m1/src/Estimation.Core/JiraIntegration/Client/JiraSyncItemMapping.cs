using Estimation.Core.Features.Services;
using Estimation.Core.Train.Services;

namespace Estimation.Core.JiraIntegration.Client;

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
            Components: JoinComponents(j.Components),
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
            Components: JoinComponents(j.Components),
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
            Components: JoinComponents(j.Components),
            Status: j.Status,
            JiraUpdated: j.Updated,
            TargetStart: j.TargetStart,
            TargetEnd: j.TargetEnd,
            StoryPoints: j.StoryPoints)
        {
            PropertyMask = propertyMask,
        };
    }

    public static IssueMatchFacts ToMatchFacts(this JiraIssueResponse j)
    {
        return new IssueMatchFacts(ProjectKeyFromJiraKey(j.Key), j.Key, JoinLabels(j.Labels), JoinComponents(j.Components));
    }

    public static string? JoinLabels(List<string>? labels)
    {
        return labels is { Count: > 0 } ? string.Join(",", labels) : null;
    }

    public static string? JoinComponents(List<string>? components)
    {
        return components is { Count: > 0 } ? string.Join(",", components) : null;
    }

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
