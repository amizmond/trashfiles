using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;

namespace Estimation.Core.Capacity.Models;

public class ArtPrioritizationOrder
{
    public int Id { get; set; }

    public int CapitalProjectId { get; set; }
    public virtual Art Art { get; set; } = null!;

    public int? PiId { get; set; }
    public virtual Pi? Pi { get; set; }

    public ArtPrioritization Level { get; set; }

    public int? PortfolioEpicId { get; set; }
    public virtual PortfolioEpic? PortfolioEpic { get; set; }

    public int? BusinessOutcomeId { get; set; }
    public virtual BusinessOutcome? BusinessOutcome { get; set; }

    public int SortOrder { get; set; }

    public bool IsIncluded { get; set; } = true;
}
