using System.ComponentModel.DataAnnotations;

using Estimation.Core.JiraIntegration.Client.JiraSync;

using Estimation.Core.JiraIntegration.Models;

namespace Estimation.Core.Train.Models;

public class PortfolioEpic : JiraIssue
{
    public int Id { get; set; }

    [MaxLength(250)]
    public string? Comments { get; set; }

    [MaxLength(250)]
    public string? L6Owner { get; set; }

    public int? UnfundedOptionId { get; set; }
    public virtual UnfundedOption? UnfundedOption { get; set; }
    [JiraSync(JiraSyncFields.ParentLink, Converter = typeof(PortfolioEpicParentConverter))]
    public virtual IList<StrategicObjectivePortfolioEpic> StrategicObjectivePortfolioEpics { get; set; } = [];

    public virtual IList<BusinessOutcome> BusinessOutcomes { get; set; } = [];
}
