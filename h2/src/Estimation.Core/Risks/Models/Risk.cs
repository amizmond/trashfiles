using System.ComponentModel.DataAnnotations;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;

namespace Estimation.Core.Risks.Models;

public class Risk
{
    public const int MaxSummaryLength = 1000;
    public const int MaxOwnerLength = 150;

    public int Id { get; set; }

    public int PiId { get; set; }
    public virtual Pi Pi { get; set; } = null!;

    public RiskCategory Category { get; set; }

    public RiskSeverity Severity { get; set; }

    public RiskStatus Status { get; set; }

    [MaxLength(MaxSummaryLength)]
    public string? Summary { get; set; }

    [MaxLength(MaxOwnerLength)]
    public string? Owner { get; set; }

    public DateTime DateRaised { get; set; }

    public DateTime? DueBy { get; set; }

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime? DateUpdated { get; set; }

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }

    public virtual IList<RiskCapitalProject> RiskCapitalProjects { get; set; } = [];

    public virtual IList<RiskFeature> RiskFeatures { get; set; } = [];
}
