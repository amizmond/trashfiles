using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.JiraIntegration.Client;

public class JiraLabelCache
{
    public const int MaxErrorLength = 500;

    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string CacheKey { get; set; } = null!;

    [Required]
    public string LabelsJson { get; set; } = null!;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    [MaxLength(MaxErrorLength)]
    public string? LastError { get; set; }
}
