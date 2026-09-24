using System.ComponentModel.DataAnnotations;

using Estimation.Core.Train.Models;

namespace Estimation.Core.JiraIntegration.Models;

public class JiraSyncProjectSettings
{
    public int Id { get; set; }

    public int CapitalProjectId { get; set; }
    public virtual Art? Art { get; set; }

    public bool CreateNotExisted { get; set; }

    public bool UpdateExisted { get; set; }

    [MaxLength(1000)]
    public string? IssueTypesCsv { get; set; }

    [MaxLength(2000)]
    public string? LabelsCsv { get; set; }

    [MaxLength(2000)]
    public string? StatusesCsv { get; set; }

    [MaxLength(2000)]
    public string? ExcludeCreateStatusesCsv { get; set; }

    public JiraSyncDateFilterMode DateFilterMode { get; set; } = JiraSyncDateFilterMode.Updated;

    public DateTime? SinceFloorUtc { get; set; }

    public DateTime? LastSyncedWatermarkUtc { get; set; }
}
