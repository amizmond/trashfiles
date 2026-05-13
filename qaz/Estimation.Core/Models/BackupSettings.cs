using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.Models;

public class BackupSettings
{
    public int Id { get; set; }

    public bool Enabled { get; set; }

    [Required]
    [MaxLength(500)]
    public string BackupFolderPath { get; set; } = string.Empty;

    public BackupScheduleType ScheduleType { get; set; } = BackupScheduleType.Daily;

    public int IntervalHours { get; set; } = 24;

    public TimeOnly DailyTime { get; set; } = new TimeOnly(2, 0);

    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;

    public int RetentionCount { get; set; } = 7;

    public DateTime? LastBackupAt { get; set; }

    public DateTime? NextBackupAt { get; set; }
}
