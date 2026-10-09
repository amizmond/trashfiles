using System.ComponentModel.DataAnnotations;
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client.JiraSync;
using Estimation.Core.JiraIntegration.Models;

namespace Estimation.Core.Train.Models;

public class BusinessOutcome : JiraIssue
{
    public int Id { get; set; }

    [MaxLength(255)]
    [JiraSync(JiraSyncFields.RagExplain)]
    public string? RagExplain { get; set; } // customfield_30300

    public int? PortfolioEpicId { get; set; }

    [JiraSync(JiraSyncFields.ParentLink, Converter = typeof(BusinessOutcomeParentConverter))]
    public virtual PortfolioEpic? PortfolioEpic { get; set; }

    public virtual IList<Feature> Features { get; set; } = [];
}
