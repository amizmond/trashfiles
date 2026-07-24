using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.JiraIntegration.Models;

/// <summary>
/// Single-row settings for the sprint-metrics snapshot engine. The engine reuses the Jira
/// sync service account from <see cref="JiraSyncSettings"/>; report viewers never need a
/// Jira connection of their own.
/// </summary>
public class SprintMetricsSyncSettings
{
    public int Id { get; set; }

    public bool Enabled { get; set; }

    public int CycleCooldownMinutes { get; set; } = 60;

    /// <summary>Max historical sprints backfilled per cycle (newest first).</summary>
    public int BackfillBatchSize { get; set; } = 25;

    /// <summary>Issue types included in snapshots and metrics.</summary>
    [MaxLength(500)]
    public string IssueTypesCsv { get; set; } = "Task,Story,Bug";

    /// <summary>
    /// Optional explicit done-status names. Empty means an issue counts as done when its
    /// Jira status category is "done".
    /// </summary>
    [MaxLength(2000)]
    public string? DoneStatusesCsv { get; set; }

    /// <summary>
    /// Sprints that ended before this date are never captured live; they belong exclusively
    /// to the bounded backfill path. Set once when the engine is first enabled.
    /// </summary>
    public DateTime? EnablementFloorUtc { get; set; }

    public DateTime? LastRunAt { get; set; }

    public DateTime? NextRunAt { get; set; }

    /// <summary>Last probe verdict for /rest/agile/1.0 (null = never probed).</summary>
    public bool? AgileApiAvailable { get; set; }

    /// <summary>Last probe verdict for the greenhopper sprintreport endpoint (null = never probed / undecided).</summary>
    public bool? SprintReportAvailable { get; set; }

    public DateTime? LastProbedAt { get; set; }

    [MaxLength(2000)]
    public string? LastProbeMessage { get; set; }
}
