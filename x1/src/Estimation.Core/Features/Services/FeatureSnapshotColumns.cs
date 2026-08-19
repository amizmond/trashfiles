namespace Estimation.Core.Features.Services;

// The column layout shared by the two snapshot exports. A delta pairs every comparable field into
// an A and a B column while a single-snapshot export writes one; keeping the order and the widths
// in one place means a new snapshot field cannot land in one export and be forgotten in the other.
public static class FeatureSnapshotColumns
{
    // Where the feature sits. Identical on both sides of a delta, so never paired.
    public static readonly string[] Context =
    [
        FeatureDeltaFields.Art,
        FeatureDeltaFields.Pi
    ];

    // Everything a snapshot can capture a changed value for.
    public static readonly string[] Comparable =
    [
        FeatureDeltaFields.Summary,
        FeatureDeltaFields.Name,
        FeatureDeltaFields.Teams,
        FeatureDeltaFields.Labels,
        FeatureDeltaFields.BusinessOutcome,
        FeatureDeltaFields.TargetStart,
        FeatureDeltaFields.TargetEnd,
        FeatureDeltaFields.StoryPoints,
        FeatureDeltaFields.RequirementStatus,
        FeatureDeltaFields.FundingStatus,
        FeatureDeltaFields.PiObjective,
        FeatureDeltaFields.RagExplain,
        FeatureDeltaFields.AcceptanceCriteria
    ];

    public static double WidthFor(string field) => field switch
    {
        FeatureDeltaFields.Summary => 45,
        FeatureDeltaFields.Name => 35,
        FeatureDeltaFields.AcceptanceCriteria => 45,
        FeatureDeltaFields.PiObjective => 35,
        FeatureDeltaFields.RagExplain => 35,
        FeatureDeltaFields.Teams => 25,
        FeatureDeltaFields.Labels => 25,
        FeatureDeltaFields.BusinessOutcome => 30,
        FeatureDeltaFields.Art => 28,
        FeatureDeltaFields.StoryPoints => 10,
        _ => 18
    };
}
