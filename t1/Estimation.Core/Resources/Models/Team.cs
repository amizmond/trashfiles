using System.ComponentModel.DataAnnotations;
using Estimation.Core.Features.Models;
using Estimation.Core.Train.Models;

namespace Estimation.Core.Resources.Models;

public class Team
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(70)]
    public string? FullName { get; set; }

    [MaxLength(50)]
    public string? OptionalTeamTag { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    /// <summary>
    /// Jira Agile board (rapid view) this team plans on. Enables sprint-ID discovery and the
    /// greenhopper sprint report for the sprint-metrics engine; null keeps name-based JQL only.
    /// </summary>
    public int? JiraBoardId { get; set; }

    public virtual IList<TeamMember> TeamMembers { get; set; } = [];
    public virtual IList<FeatureTeam> FeatureTeams { get; set; } = [];
    public virtual IList<CapitalProjectTeam> CapitalProjectTeams { get; set; } = [];
    public virtual IList<TeamTechnologyStack> TeamTechnologyStacks { get; set; } = [];
}
