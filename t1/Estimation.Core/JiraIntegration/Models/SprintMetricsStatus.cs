namespace Estimation.Core.JiraIntegration.Models;

public enum SprintMetricsStatus
{
    /// <summary>Only the sprint-start (committed) snapshot exists; final metrics are not yet computed.</summary>
    BaselineOnly = 0,

    /// <summary>Baseline and final snapshots exist; every metric is computed.</summary>
    Complete = 1,

    /// <summary>
    /// Only partially computable (e.g. a historical sprint with no baseline and no Jira sprint
    /// report available): delivered/not-completed are present, scope-change metrics are not.
    /// </summary>
    Partial = 2,

    /// <summary>The last capture attempt failed; see <see cref="SprintJiraMetrics.FailReason"/>.</summary>
    Failed = 3,
}
