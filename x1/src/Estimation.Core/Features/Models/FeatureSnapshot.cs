using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.Features.Models;

public class FeatureSnapshot
{
    public int Id { get; set; }

    public int? CapitalProjectId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ArtName { get; set; } = null!;

    [MaxLength(10)]
    public string? ArtJiraKey { get; set; }

    [Required]
    [MaxLength(100)]
    public string PiName { get; set; } = null!;

    [MaxLength(200)]
    public string? Name { get; set; }

    public bool IncludedPiLabelMatches { get; set; }

    public bool IsAutomatic { get; set; }

    public DateTime CreatedAt { get; set; }

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public int FeatureCount { get; set; }

    public virtual IList<FeatureSnapshotItem> Items { get; set; } = [];
}
