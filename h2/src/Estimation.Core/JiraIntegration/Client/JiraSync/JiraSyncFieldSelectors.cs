namespace Estimation.Core.JiraIntegration.Client.JiraSync;

/// <summary>
/// Maps a logical <see cref="JiraSyncFields"/> name to a selector that extracts the entity-ready
/// value from a parsed <see cref="JiraIssueResponse"/>. Selectors return the value in the shape the
/// target property expects (e.g. Labels are already comma-joined), so the applier can set them directly.
/// </summary>
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
            [JiraSyncFields.Status] = j => j.Status,
            [JiraSyncFields.JiraUpdated] = j => j.Updated,
            [JiraSyncFields.TargetStart] = j => j.TargetStart,
            [JiraSyncFields.TargetEnd] = j => j.TargetEnd,
            [JiraSyncFields.StoryPoints] = j => j.StoryPoints,
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
