using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.Models;

public class BackupHistory
{
    public int Id { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? FilePath { get; set; }

    public long? FileSizeBytes { get; set; }

    [MaxLength(2000)]
    public string? Message { get; set; }

    [MaxLength(100)]
    public string? TriggeredBy { get; set; }
}
