using System.ComponentModel.DataAnnotations;
using Estimation.Core.Resources.Models;

namespace Estimation.Core.Train.Models;

public class Art
{
    public int Id { get; set; }

    [MaxLength(10)]
    public string? JiraKey { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? DepartmentId { get; set; }

    public virtual Department? Department { get; set; }

    public virtual IList<ArtTeam> CapitalProjectTeams { get; set; } = [];

    public virtual IList<ArtStrategicObjective> CapitalProjectStrategicObjectives { get; set; } = [];
}
