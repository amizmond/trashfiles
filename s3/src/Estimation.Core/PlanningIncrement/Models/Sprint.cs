using System.ComponentModel.DataAnnotations;
using Estimation.Core.Resources.Models;
using Estimation.Core.Train.Models;

namespace Estimation.Core.PlanningIncrement.Models;

public class Sprint
{
    public int Id { get; set; }

    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;

    public int? PiId { get; set; }
    public virtual Pi? Pi { get; set; }

    public int? SourceArtSprintId { get; set; }
    public virtual CapitalProjectSprint? SourceArtSprint { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public DateTime UatStart { get; set; }

    public DateTime UatEnd { get; set; }

    [MaxLength(50)]
    public string? FixVersion { get; set; }

    [MaxLength(7)]
    public string? ColorHex { get; set; }

    public bool? IsIpSprint { get; set; }

    [MaxLength(250)]
    public string? Comment { get; set; }
}
