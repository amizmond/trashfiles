using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Estimation.Core.Features.Hygiene.Models;

/// <summary>
/// One hygiene rule of an ART: a field, a check and the check's parameters. Rules are pass or fail;
/// a feature that fails any enabled rule of its ART is not healthy.
/// </summary>
public class FeatureHygieneRule
{
    public const int MaxParametersLength = 4000;
    public const int MaxMessageLength = 500;

    public int Id { get; set; }

    /// <summary>The ART the rule belongs to.</summary>
    public int CapitalProjectId { get; set; }

    /// <summary>Always null today: rules apply to every PI of the ART. Reserved for PI overrides.</summary>
    public int? PiId { get; set; }

    public HygieneField Field { get; set; }

    public HygieneCheck Check { get; set; }

    /// <summary>The check's parameters as JSON; see <see cref="HygieneRuleParameters"/>.</summary>
    [MaxLength(MaxParametersLength)]
    public string? ParametersJson { get; set; }

    /// <summary>Optional wording shown next to a failure, for example what the author expects.</summary>
    [MaxLength(MaxMessageLength)]
    public string? Message { get; set; }

    public int SortOrder { get; set; }

    public bool IsEnabled { get; set; } = true;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    [MaxLength(256)]
    public string? ModifiedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    [NotMapped]
    public HygieneRuleParameters Parameters => HygieneRuleParameters.Parse(ParametersJson);

    public FeatureHygieneRule Clone() => (FeatureHygieneRule)MemberwiseClone();
}
