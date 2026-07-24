using System.ComponentModel.DataAnnotations;
using Estimation.Core.PlanningIncrement.Models;

namespace Estimation.Core.JiraIntegration.Models;

/// <summary>
/// Persisted per-sprint Jira metrics snapshot (one row per local <see cref="Sprint"/>).
/// Story-point values are stored as decimals exactly as Jira returns them; percentages are
/// never stored — they are derived at read time so definition changes re-render history.
/// All "at start" values use the baseline snapshot, "at end" values the final snapshot,
/// which is frozen once captured (retro-edits after close are absorbed only by an explicit
/// manual re-snapshot).
/// </summary>
public class SprintJiraMetrics
{
    public int Id { get; set; }

    public int SprintId { get; set; }
    public virtual Sprint Sprint { get; set; } = null!;

    public SprintMetricsStatus Status { get; set; }

    public SprintMetricsDataSource? DataSource { get; set; }

    /// <summary>Σ SP-at-start over the baseline (committed) issue set.</summary>
    public decimal? CommittedSp { get; set; }

    /// <summary>Σ SP-at-start over committed issues done at close.</summary>
    public decimal? CompletedFromCommittedSp { get; set; }

    /// <summary>Σ SP-at-start over committed issues neither done nor removed at close.</summary>
    public decimal? NotCompletedFromCommittedSp { get; set; }

    /// <summary>Σ SP-at-end over ALL issues done at close (committed and added).</summary>
    public decimal? DeliveredSp { get; set; }

    /// <summary>Σ SP-at-end over issues added after sprint start and present at close.</summary>
    public decimal? AddedSp { get; set; }

    /// <summary>Σ SP-at-start over committed issues no longer in the sprint at close.</summary>
    public decimal? RemovedSp { get; set; }

    /// <summary>Net Σ(SP-at-end − SP-at-start) over issues estimated at both start and close.</summary>
    public decimal? ReEstimationNetSp { get; set; }

    public int? ReEstimatedIssueCount { get; set; }

    /// <summary>Issues committed without an estimate that received one during the sprint.</summary>
    public int? LateEstimatedIssueCount { get; set; }

    /// <summary>Σ SP-at-end over late-estimated issues (excluded from <see cref="ReEstimationNetSp"/>).</summary>
    public decimal? LateEstimatedSp { get; set; }

    /// <summary>Issues in the final set with no estimate at close (data-quality signal).</summary>
    public int? UnestimatedIssueCount { get; set; }

    /// <summary>Issues in the final set that were already in at least one earlier sprint.</summary>
    public int? CarryOverIssueCount { get; set; }

    public DateTime? BaselineCapturedAt { get; set; }

    public DateTime? FinalCapturedAt { get; set; }

    [MaxLength(1000)]
    public string? FailReason { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public virtual IList<SprintJiraMetricsIssue> Issues { get; set; } = [];
}
