using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.JiraIntegration.Models;

/// <summary>
/// Per-issue facts behind a <see cref="SprintJiraMetrics"/> row, kept so the report can offer
/// drill-down without touching Jira and so aggregates can be recomputed from raw facts when
/// a metric definition changes.
/// </summary>
public class SprintJiraMetricsIssue
{
    public int Id { get; set; }

    public int SprintJiraMetricsId { get; set; }
    public virtual SprintJiraMetrics SprintJiraMetrics { get; set; } = null!;

    [Required]
    [MaxLength(30)]
    public string IssueKey { get; set; } = null!;

    [MaxLength(50)]
    public string? IssueType { get; set; }

    [MaxLength(500)]
    public string? Summary { get; set; }

    public decimal? SpAtStart { get; set; }

    public decimal? SpAtEnd { get; set; }

    [MaxLength(50)]
    public string? StatusAtEnd { get; set; }

    public bool IsDoneAtEnd { get; set; }

    public SprintIssueOutcome Outcome { get; set; }

    /// <summary>True when the issue was already in at least one earlier sprint (spill-in).</summary>
    public bool? WasCarriedOver { get; set; }
}
