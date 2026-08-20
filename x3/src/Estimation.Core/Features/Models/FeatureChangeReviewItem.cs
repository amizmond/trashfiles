using System.ComponentModel.DataAnnotations;
using Estimation.Core.Features.Services;

namespace Estimation.Core.Features.Models;

public enum FeatureChangeDecision
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class FeatureChangeReviewItem
{
    public int Id { get; set; }

    public int ReviewId { get; set; }

    [Required]
    [MaxLength(100)]
    public string FeatureKey { get; set; } = null!;

    [MaxLength(100)]
    public string? JiraId { get; set; }

    [MaxLength(255)]
    public string? FeatureName { get; set; }

    [MaxLength(255)]
    public string? Summary { get; set; }

    public FeatureDeltaChangeKind ChangeKind { get; set; }

    // Field-level changes captured when the review was created, serialized as a JSON
    // array of FeatureDeltaFieldChange. Null for Added and Removed rows.
    public string? ChangesJson { get; set; }

    public FeatureChangeDecision Decision { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    [MaxLength(256)]
    public string? DecidedBy { get; set; }

    public DateTime? DecidedAt { get; set; }

    public virtual FeatureChangeReview Review { get; set; } = null!;
}
