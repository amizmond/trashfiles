namespace Estimation.Core.JiraIntegration.Models;

public enum SprintMetricsDataSource
{
    /// <summary>Metrics computed by diffing the app's own baseline and final JQL snapshots.</summary>
    SnapshotDiff = 0,

    /// <summary>Metrics backfilled from Jira's own sprint report (greenhopper endpoint), frozen at close.</summary>
    GreenhopperBackfill = 1,

    /// <summary>
    /// Historical sprint with no baseline and no sprint report: only the at-close membership
    /// from a single name/id JQL search — delivered and not-completed only.
    /// </summary>
    PartialNameJql = 2,
}
