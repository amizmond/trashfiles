using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.JiraIntegration.Models;

public class SprintMetricsSyncHistory
{
    public int Id { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "Running";

    [MaxLength(256)]
    public string? TriggeredBy { get; set; }

    public int BaselinesCaptured { get; set; }

    public int FinalsComputed { get; set; }

    public int Backfilled { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public string? Message { get; set; }
}
