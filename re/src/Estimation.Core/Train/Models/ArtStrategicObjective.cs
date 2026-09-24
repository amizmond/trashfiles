namespace Estimation.Core.Train.Models;

public class ArtStrategicObjective
{
    public int CapitalProjectId { get; set; }
    public virtual Art Art { get; set; } = null!;

    public int StrategicObjectiveId { get; set; }
    public virtual StrategicObjective StrategicObjective { get; set; } = null!;
}
