using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.HashApprovals.Models;

/// <summary>
/// "This state of this feature is approved." The state is identified by a hash of the feature's
/// compared fields, so the approval is recognised by every delta whose B side is that state and
/// silently stops matching as soon as the feature changes again. Withdrawing is a soft delete
/// so the history of who approved what stays readable.
/// </summary>
public class FeatureStateApproval
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string ArtName { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string PiName { get; set; } = null!;

    /// <summary>The delta row key: the normalized Jira ID, or "#FeatureId" when the feature has none.</summary>
    [Required]
    [MaxLength(100)]
    public string FeatureKey { get; set; } = null!;

    [MaxLength(100)]
    public string? JiraId { get; set; }

    [MaxLength(255)]
    public string? FeatureName { get; set; }

    /// <summary>"v1:" + SHA-256 of <see cref="StateJson"/>, or "v1:REMOVED" for an approved removal.</summary>
    [Required]
    [MaxLength(80)]
    public string StateHash { get; set; } = null!;

    /// <summary>The canonical field values that were approved (null for a removal). Audit and re-hashing.</summary>
    public string? StateJson { get; set; }

    /// <summary>The field changes on screen when the approval was given (context only).</summary>
    public string? ChangesJson { get; set; }

    /// <summary>The snapshot the delta was computed against when approving (context only, no FK).</summary>
    public int? BaselineSnapshotId { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    [MaxLength(256)]
    public string? ApprovedBy { get; set; }

    public DateTime ApprovedAt { get; set; }

    [MaxLength(256)]
    public string? WithdrawnBy { get; set; }

    public DateTime? WithdrawnAt { get; set; }
}
