using Estimation.Core.JiraIntegration.Client.JiraSync;

using Estimation.Core.JiraIntegration.Models;

namespace Estimation.Core.Train.Models;

public class StrategicObjective : JiraIssue
{
    public int Id { get; set; }

    [JiraSync(JiraSyncFields.ParentLink, Converter = typeof(StrategicObjectiveParentConverter))]
    public virtual IList<ArtStrategicObjective> CapitalProjectStrategicObjectives { get; set; } = [];

    public virtual IList<StrategicObjectivePortfolioEpic> StrategicObjectivePortfolioEpics { get; set; } = [];
}
