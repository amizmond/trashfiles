using System.ComponentModel.DataAnnotations;

using Estimation.Core.JiraIntegration.Client.JiraSync;

namespace Estimation.Core.JiraIntegration.Models;

public abstract class JiraIssue
{
    [MaxLength(100)]
    public string? JiraId { get; set; }

    [MaxLength(10)]
    public string? ProjectKey { get; set; }

    [MaxLength(60)]
    [JiraSync(JiraSyncFields.IssueType)]
    public string? IssueType { get; set; }

    [Required]
    [MaxLength(255)]
    [JiraSync(JiraSyncFields.Summary, Required = true)]
    public string Summary { get; set; } = null!;

    [MaxLength(32767)]
    [JiraSync(JiraSyncFields.Description)]
    public string? Description { get; set; }

    [MaxLength(32767)]
    [JiraSync(JiraSyncFields.AcceptanceCriteria)]
    public string? AcceptanceCriteria { get; set; }

    [MaxLength(255)]
    [JiraSync(JiraSyncFields.NavigatorId)]
    public string? NavigatorId { get; set; }

    [MaxLength(4000)]
    [JiraSync(JiraSyncFields.Labels)]
    public string? Labels { get; set; }

    [MaxLength(4000)]
    [JiraSync(JiraSyncFields.Components)]
    public string? Components { get; set; }

    [MaxLength(50)]
    [JiraSync(JiraSyncFields.Status)]
    public string? Status { get; set; }

    [JiraSync(JiraSyncFields.JiraUpdated)]
    public DateTime? JiraUpdated { get; set; }

    [JiraSync(JiraSyncFields.TargetStart)]
    public DateTime? TargetStart { get; set; } // customfield_19002

    [JiraSync(JiraSyncFields.TargetEnd)]
    public DateTime? TargetEnd { get; set; } // customfield_19003

    [JiraSync(JiraSyncFields.StoryPoints)]
    public int? StoryPoints { get; set; } // customfield_10003
}
