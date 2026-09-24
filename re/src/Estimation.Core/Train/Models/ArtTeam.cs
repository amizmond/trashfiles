using Estimation.Core.Resources.Models;

namespace Estimation.Core.Train.Models;

public class ArtTeam
{
    public int CapitalProjectId { get; set; }
    public virtual Art Art { get; set; } = null!;

    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;
}
