using System.ComponentModel.DataAnnotations;
using Estimation.Core.PlanningIncrement.Models;

namespace Estimation.Core.Train.Models;

public class CapitalProjectSprint
{
    public int Id { get; set; }

    public int CapitalProjectId { get; set; }
    public virtual CapitalProject CapitalProject { get; set; } = null!;

    public int PiId { get; set; }
    public virtual Pi Pi { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string Name { get; set; } = null!;

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public DateTime UatStart { get; set; }

    public DateTime UatEnd { get; set; }

    [MaxLength(50)]
    public string? FixVersion { get; set; }

    public bool IsIpSprint { get; set; }
}
