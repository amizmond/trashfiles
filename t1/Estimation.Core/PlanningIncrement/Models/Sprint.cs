using System.ComponentModel.DataAnnotations;
using Estimation.Core.Resources.Models;

namespace Estimation.Core.PlanningIncrement.Models;

public class Sprint
{
    public int Id { get; set; }

    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;

    public int? PiId { get; set; }
    public virtual Pi? Pi { get; set; }

    [Required]
    [MaxLength(70)]
    public string Name { get; set; } = null!;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [MaxLength(7)]
    public string? ColorHex { get; set; }

    public bool? IsIpSprint { get; set; }

    [MaxLength(250)]
    public string? Comment { get; set; }

    /// <summary>
    /// Jira sprint id discovered from the team's board. When set, sprint issues are fetched
    /// with the unambiguous <c>sprint = &lt;id&gt;</c> JQL instead of the name-based form.
    /// </summary>
    public int? JiraSprintId { get; set; }

    /// <summary>Jira sprint state at last discovery: future, active, or closed.</summary>
    [MaxLength(20)]
    public string? JiraState { get; set; }

    /// <summary>Jira's actual sprint activation instant (UTC); authoritative over <see cref="StartDate"/> for metric boundaries.</summary>
    public DateTime? JiraStartDate { get; set; }

    /// <summary>Jira's actual sprint completion instant (UTC).</summary>
    public DateTime? JiraCompleteDate { get; set; }
}
