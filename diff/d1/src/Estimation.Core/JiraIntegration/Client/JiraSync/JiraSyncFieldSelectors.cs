namespace Estimation.Core.JiraIntegration.Client.JiraSync;

public static class JiraSyncFieldSelectors
{
    private static readonly Dictionary<string, Func<JiraIssueResponse, object?>> Selectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [JiraSyncFields.IssueType] = j => j.IssueType,
            [JiraSyncFields.Summary] = j => j.Summary,
            [JiraSyncFields.Description] = j => j.Description,
            [JiraSyncFields.AcceptanceCriteria] = j => j.AcceptanceCriteria,
            [JiraSyncFields.NavigatorId] = j => j.NavigatorId,
            [JiraSyncFields.Labels] = j => JiraSyncItemMapping.JoinLabels(j.Labels),
            [JiraSyncFields.Components] = j => JiraSyncItemMapping.JoinComponents(j.Components),
            [JiraSyncFields.FixVersions] = j => JiraSyncItemMapping.JoinFixVersions(j.FixVersions),
            [JiraSyncFields.Status] = j => j.Status,
            [JiraSyncFields.JiraUpdated] = j => j.Updated,
            [JiraSyncFields.TargetStart] = j => j.TargetStart,
            [JiraSyncFields.TargetEnd] = j => j.TargetEnd,
            [JiraSyncFields.StoryPoints] = j => j.StoryPoints,
            [JiraSyncFields.RagStatus] = j => j.RagStatus,
            [JiraSyncFields.FeatureName] = j => j.FeatureName,
            [JiraSyncFields.RagExplain] = j => j.RagExplain,
        };

    public static bool TryGet(string field, out Func<JiraIssueResponse, object?>? selector)
    {
        return Selectors.TryGetValue(field, out selector);
    }

    public static bool IsKnownScalar(string field)
    {
        return Selectors.ContainsKey(field);
    }
}
