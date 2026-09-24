using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Estimation.Core.Resources.Models;

namespace Estimation.Core.Train.Models;

public class Art
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? DepartmentId { get; set; }

    public virtual Department? Department { get; set; }

    public virtual IList<ArtJiraKey> JiraKeys { get; set; } = [];

    public virtual IList<ArtTeam> CapitalProjectTeams { get; set; } = [];

    public virtual IList<ArtStrategicObjective> CapitalProjectStrategicObjectives { get; set; } = [];

    [NotMapped]
    public IReadOnlyList<string> Keys => JiraKeys.OrderBy(k => k.Id).Select(k => k.JiraKey).ToList();

    [NotMapped]
    public string KeysText => string.Join(", ", Keys);

    [NotMapped]
    public bool HasKeys => JiraKeys.Count > 0;
}
