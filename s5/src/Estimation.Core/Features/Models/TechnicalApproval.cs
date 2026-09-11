using System.ComponentModel.DataAnnotations;

namespace Estimation.Core.Features.Models;

public class TechnicalApproval
{
    public const int ApprovedId = 1;
    public const int RequiredApproveId = 2;
    public const int NotApplicableId = 3;

    public const int DefaultId = NotApplicableId;

    public int Id { get; set; }

    [Required]
    [MaxLength(30)]
    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }

    public virtual IList<Feature> Features { get; set; } = [];
}
