using System.Globalization;
using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Models;

namespace Estimation.Core.Features.Hygiene.Services;

/// <summary>Reads the value a rule looks at from a feature, by field kind.</summary>
public static class HygieneFieldReader
{
    public static string? Text(Feature feature, HygieneField field) => field switch
    {
        HygieneField.Summary => feature.Summary,
        HygieneField.Name => feature.Name,
        HygieneField.Description => feature.Description,
        HygieneField.AcceptanceCriteria => feature.AcceptanceCriteria,
        HygieneField.RagExplain => feature.RagExplain,
        HygieneField.Dependencies => feature.Dependencies,
        HygieneField.NavigatorId => feature.NavigatorId,
        HygieneField.Labels => feature.Labels,
        _ => null
    };

    public static decimal? Number(Feature feature, HygieneField field) => field switch
    {
        HygieneField.StoryPoints => feature.StoryPoints,
        HygieneField.Ranking => feature.Ranking,
        HygieneField.ConfidencePercentage => feature.ConfidencePercentage,
        _ => null
    };

    public static DateTime? Date(Feature feature, HygieneField field) => field switch
    {
        HygieneField.TargetStart => feature.TargetStart,
        HygieneField.TargetEnd => feature.TargetEnd,
        HygieneField.DateExpected => feature.DateExpected,
        _ => null
    };

    public static bool ReferenceIsEmpty(Feature feature, HygieneField field) => field switch
    {
        HygieneField.BusinessOutcome => feature.BusinessOutcomeId is null && feature.BusinessOutcome is null,
        HygieneField.Pi => feature.PiId is null && feature.Pi is null,
        HygieneField.PiObjective => feature.PiObjectiveId is null && feature.PiObjective is null,
        HygieneField.Teams => feature.FeatureTeams is null || feature.FeatureTeams.Count == 0,
        _ => true
    };

    public static string? Choice(Feature feature, HygieneField field) => field switch
    {
        HygieneField.RequirementStatus => feature.RequirementStatus?.Name,
        HygieneField.TechnicalApproval => feature.TechnicalApproval?.Name,
        HygieneField.FundingStatus => feature.UnfundedOption?.Name,
        HygieneField.Status => feature.Status,
        _ => null
    };

    public static bool Flag(Feature feature, HygieneField field) => field switch
    {
        HygieneField.ExternalDependencies => feature.ExternalDependencies,
        HygieneField.IsLinkedToJira => feature.IsLinkedToTheJira == true,
        _ => false
    };

    /// <summary>The value as a person would read it, for the failure tooltip and the export.</summary>
    public static string? Describe(Feature feature, HygieneField field)
    {
        switch (HygieneFieldCatalog.KindOf(field))
        {
            case HygieneFieldKind.Text:
                return HygieneText.Excerpt(Text(feature, field));

            case HygieneFieldKind.Number:
                return Number(feature, field) is { } number
                    ? HygieneRuleParameters.FormatNumber(number)
                    : null;

            case HygieneFieldKind.Date:
                return Date(feature, field) is { } date
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null;

            case HygieneFieldKind.Reference:
                return DescribeReference(feature, field);

            case HygieneFieldKind.Choice:
                return Trimmed(Choice(feature, field));

            case HygieneFieldKind.Flag:
                return Flag(feature, field) ? "yes" : "no";

            default:
                return null;
        }
    }

    private static string? DescribeReference(Feature feature, HygieneField field)
    {
        switch (field)
        {
            case HygieneField.BusinessOutcome:
            {
                var bo = feature.BusinessOutcome;

                if (bo is null)
                {
                    return feature.BusinessOutcomeId is null ? null : $"#{feature.BusinessOutcomeId}";
                }

                var name = Trimmed(bo.Summary);
                var jiraId = Trimmed(bo.JiraId);
                return name is null ? jiraId : jiraId is null ? name : $"{jiraId} — {name}";
            }

            case HygieneField.Pi:
                return Trimmed(feature.Pi?.Name) ?? (feature.PiId is null ? null : $"#{feature.PiId}");

            case HygieneField.PiObjective:
                return Trimmed(feature.PiObjective?.Name) ?? (feature.PiObjectiveId is null ? null : $"#{feature.PiObjectiveId}");

            case HygieneField.Teams:
            {
                var names = (feature.FeatureTeams ?? [])
                    .Select(ft => Trimmed(ft.Team?.Name))
                    .Where(n => n is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return names.Count == 0 ? null : string.Join(", ", names);
            }

            default:
                return null;
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
