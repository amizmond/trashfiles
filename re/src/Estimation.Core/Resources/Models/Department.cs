using System.ComponentModel.DataAnnotations;
using Estimation.Core.Train.Models;

namespace Estimation.Core.Resources.Models;

public class Department
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public virtual IList<Art> CapitalProjects { get; set; } = [];
}
