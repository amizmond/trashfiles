using Estimation.Core.Resources.Models;

namespace Estimation.Core.Features.Models;

public class FeatureTeam
{
    public int FeatureId { get; set; }
    public virtual Feature Feature { get; set; } = null!;

    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;

    public int? StoryPoints { get; set; }

    public bool? IsPrimary { get; set; }

    public virtual IList<FeatureTeamTechnologyStack> TechnologyStacks { get; set; } = [];

    public virtual IList<FeatureTeamSprint> Sprints { get; set; } = [];
}
