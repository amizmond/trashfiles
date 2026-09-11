using Estimation.Core.PlanningIncrement.Models;

namespace Estimation.Core.Features.Models;

public class FeatureTeamSprint
{
    public int FeatureId { get; set; }

    public int TeamId { get; set; }

    public virtual FeatureTeam FeatureTeam { get; set; } = null!;

    public int SprintId { get; set; }

    public virtual Sprint Sprint { get; set; } = null!;
}
