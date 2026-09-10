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

    public bool IsArtSharedTeam { get; set; }

    [MaxLength(50)]
    public string? JiraName { get; set; }

    public virtual IList<TeamMember> TeamMembers { get; set; } = [];
    public virtual IList<FeatureTeam> FeatureTeams { get; set; } = [];
    public virtual IList<CapitalProjectTeam> CapitalProjectTeams { get; set; } = [];
    public virtual IList<TeamTechnologyStack> TeamTechnologyStacks { get; set; } = [];
}
