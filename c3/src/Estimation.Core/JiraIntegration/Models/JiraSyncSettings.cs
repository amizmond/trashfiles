using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.JiraIntegration.Models;

public class JiraSyncSettings
{
    public const string DefaultServiceAccountUserName = "__jira-sync-service__";

    public int Id { get; set; }

    public bool Enabled { get; set; }

    public int CycleCooldownMinutes { get; set; } = 5;

    [MaxLength(256)]
    public string ServiceAccountUserName { get; set; } = DefaultServiceAccountUserName;

    [MaxLength(100)]
    public string? JiraTimeZoneId { get; set; }

    public bool LabelsSyncEnabled { get; set; } = true;

    public DateTime? LastRunAt { get; set; }

    public DateTime? NextRunAt { get; set; }
}
