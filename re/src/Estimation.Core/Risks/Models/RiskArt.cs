using Estimation.Core.Train.Models;

namespace Estimation.Core.Risks.Models;

public class RiskArt
{
    public int RiskId { get; set; }
    public virtual Risk Risk { get; set; } = null!;

    public int CapitalProjectId { get; set; }
    public virtual Art Art { get; set; } = null!;
}
