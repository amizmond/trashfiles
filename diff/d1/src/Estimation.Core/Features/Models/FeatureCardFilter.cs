namespace Estimation.Core.Features.Models;

public sealed class FeatureCardFilter
{
    private static readonly char[] LabelSeparators = { ',' };

    public string? Search { get; set; }

    public IReadOnlyCollection<string> StrategicObjectives { get; set; } = Array.Empty<string>();
    public bool StrategicObjectiveNot { get; set; }

    public IReadOnlyCollection<string> PortfolioEpics { get; set; } = Array.Empty<string>();
    public bool PortfolioEpicNot { get; set; }

    public IReadOnlyCollection<string> BusinessOutcomes { get; set; } = Array.Empty<string>();
    public bool BusinessOutcomeNot { get; set; }

    public IReadOnlyCollection<string> Statuses { get; set; } = Array.Empty<string>();
    public bool StatusNot { get; set; }

    public IReadOnlyCollection<string> RagStatuses { get; set; } = Array.Empty<string>();
    public bool RagStatusNot { get; set; }

    public IReadOnlyCollection<string> Labels { get; set; } = Array.Empty<string>();
    public bool LabelsNot { get; set; }

    public IReadOnlyCollection<string> RequirementStatuses { get; set; } = Array.Empty<string>();
    public bool RequirementStatusNot { get; set; }

    public IReadOnlyCollection<string> TechnicalApprovals { get; set; } = Array.Empty<string>();
    public bool TechnicalApprovalNot { get; set; }

    public IReadOnlyCollection<string> FundingStatuses { get; set; } = Array.Empty<string>();
    public bool FundingStatusNot { get; set; }

    public const string HygieneHealthy = "healthy";
    public const string HygieneUnhealthy = "unhealthy";
    public const string HygieneUnchecked = "unchecked";

    public string? Hygiene { get; set; }

    public int ActiveCount
    {
        get
        {
            var count = string.IsNullOrWhiteSpace(Search) ? 0 : 1;
            count += string.IsNullOrWhiteSpace(Hygiene) ? 0 : 1;
            count += DimensionActive(StrategicObjectives, StrategicObjectiveNot);
            count += DimensionActive(PortfolioEpics, PortfolioEpicNot);
            count += DimensionActive(BusinessOutcomes, BusinessOutcomeNot);
            count += DimensionActive(Statuses, StatusNot);
            count += DimensionActive(RagStatuses, RagStatusNot);
            count += DimensionActive(Labels, LabelsNot);
            count += DimensionActive(RequirementStatuses, RequirementStatusNot);
            count += DimensionActive(TechnicalApprovals, TechnicalApprovalNot);
            count += DimensionActive(FundingStatuses, FundingStatusNot);
            return count;
        }
    }

    public bool IsActive => ActiveCount > 0;

    public void Clear()
    {
        Search = null;
        StrategicObjectives = Array.Empty<string>();
        StrategicObjectiveNot = false;
        PortfolioEpics = Array.Empty<string>();
        PortfolioEpicNot = false;
        BusinessOutcomes = Array.Empty<string>();
        BusinessOutcomeNot = false;
        Statuses = Array.Empty<string>();
        StatusNot = false;
        RagStatuses = Array.Empty<string>();
        RagStatusNot = false;
        Labels = Array.Empty<string>();
        LabelsNot = false;
        RequirementStatuses = Array.Empty<string>();
        RequirementStatusNot = false;
        TechnicalApprovals = Array.Empty<string>();
        TechnicalApprovalNot = false;
        FundingStatuses = Array.Empty<string>();
        FundingStatusNot = false;
        Hygiene = null;
    }

    public List<T> Apply<T>(IEnumerable<T> rows) where T : IFeatureFilterRow =>
        rows.Where(r => Matches(r)).ToList();

    public bool Matches(IFeatureFilterRow r)
    {
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            if (!(Contains(r.JiraId, s)
                || Contains(r.Name, s)
                || Contains(r.Summary, s)
                || Contains(r.BusinessOutcomeJiraId, s)
                || Contains(r.BusinessOutcomeSummary, s)
                || Contains(r.PortfolioEpicJiraId, s)
                || Contains(r.PortfolioEpicSummary, s)))
            {
                return false;
            }
        }

        if (!MatchesMany(StrategicObjectives, StrategicObjectiveNot, r.StrategicObjectives.Select(o => o.JiraId)))
        {
            return false;
        }
        if (!MatchesSingle(PortfolioEpics, PortfolioEpicNot, r.PortfolioEpicJiraId))
        {
            return false;
        }
        if (!MatchesSingle(BusinessOutcomes, BusinessOutcomeNot, r.BusinessOutcomeJiraId))
        {
            return false;
        }
        if (!MatchesSingle(Statuses, StatusNot, r.Status))
        {
            return false;
        }
        if (!MatchesSingle(RagStatuses, RagStatusNot, r.RagStatus))
        {
            return false;
        }
        if (!MatchesMany(Labels, LabelsNot, SplitLabels(r.Labels)))
        {
            return false;
        }
        if (!MatchesSingle(RequirementStatuses, RequirementStatusNot, r.RequirementStatus))
        {
            return false;
        }
        if (!MatchesSingle(TechnicalApprovals, TechnicalApprovalNot, r.TechnicalApproval))
        {
            return false;
        }
        if (!MatchesSingle(FundingStatuses, FundingStatusNot, r.FundingStatus))
        {
            return false;
        }

        if (!MatchesHygiene(r.HygieneFailureCount))
        {
            return false;
        }

        return true;
    }

    private bool MatchesHygiene(int? failures) => Hygiene switch
    {
        HygieneHealthy => failures == 0,
        HygieneUnhealthy => failures > 0,
        HygieneUnchecked => failures is null,
        _ => true
    };

    public static IEnumerable<string> SplitLabels(string? labels) =>
        string.IsNullOrWhiteSpace(labels)
            ? Enumerable.Empty<string>()
            : labels.Split(LabelSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int DimensionActive(IReadOnlyCollection<string> selected, bool not) =>
        selected.Count > 0 || not ? 1 : 0;

    private static bool Contains(string? value, string needle) =>
        value?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false;

    private static bool MatchesSingle(IReadOnlyCollection<string> selected, bool not, string? value)
    {
        var hasValue = !string.IsNullOrWhiteSpace(value);
        if (not)
        {
            return selected.Count > 0
                ? !(hasValue && selected.Contains(value!, StringComparer.OrdinalIgnoreCase))
                : !hasValue;
        }
        return selected.Count == 0
            || (hasValue && selected.Contains(value!, StringComparer.OrdinalIgnoreCase));
    }

    private static bool MatchesMany(IReadOnlyCollection<string> selected, bool not, IEnumerable<string> values)
    {
        var list = values.ToList();
        if (not)
        {
            return selected.Count > 0
                ? !list.Any(v => selected.Contains(v, StringComparer.OrdinalIgnoreCase))
                : list.Count == 0;
        }
        return selected.Count == 0
            || list.Any(v => selected.Contains(v, StringComparer.OrdinalIgnoreCase));
    }
}
