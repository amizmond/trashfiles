using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.Features.Models;

public enum FeatureChangeReviewStatus
{
    Open = 0,
    Completed = 1
}

public class FeatureChangeReview
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

    public int BaselineSnapshotId { get; set; }

    public int ReviewSnapshotId { get; set; }

    public FeatureChangeReviewStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime? CompletedAt { get; set; }

    [MaxLength(256)]
    public string? CompletedBy { get; set; }

    public virtual FeatureSnapshot BaselineSnapshot { get; set; } = null!;

    public virtual FeatureSnapshot ReviewSnapshot { get; set; } = null!;

    public virtual IList<FeatureChangeReviewItem> Items { get; set; } = [];
}
