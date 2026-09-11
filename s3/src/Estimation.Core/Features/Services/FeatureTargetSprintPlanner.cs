using Estimation.Core.PlanningIncrement.Models;

namespace Estimation.Core.Features.Services;

public static class FeatureTargetSprintPlanner
{
    public static List<Pi> ResolvePis(int? featurePiId, string? featureLabels, IEnumerable<Pi> pis)
    {
        var all = pis as IList<Pi> ?? pis.ToList();

        return all
            .Where(p => p.Id == featurePiId
                || (!string.IsNullOrWhiteSpace(featureLabels)
                    && PiLabelMatching.Matches(PiLabelMatching.ParseLabels(p.FeatureLabels), p.LabelMatchMode, featureLabels)))
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderBy(p => p.StartDate is null)
            .ThenBy(p => p.StartDate)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public static bool BelongsToPi(Sprint sprint, Pi pi)
    {
        if (sprint.PiId is not null)
        {
            return sprint.PiId == pi.Id;
        }

        if (pi.StartDate is null || pi.EndDate is null)
        {
            return false;
        }

        return sprint.StartDate.Date <= pi.EndDate.Value.Date && sprint.EndDate.Date >= pi.StartDate.Value.Date;
    }

    public static List<Sprint> SprintsForPi(IEnumerable<Sprint> teamSprints, Pi pi) =>
        teamSprints
            .Where(s => BelongsToPi(s, pi))
            .OrderBy(s => s.StartDate)
            .ToList();

    public static (DateTime? Start, DateTime? End) ComputeTargets(IEnumerable<Sprint> selected)
    {
        var list = selected as IList<Sprint> ?? selected.ToList();
        if (list.Count == 0)
        {
            return (null, null);
        }

        return (list.Min(s => s.StartDate.Date), list.Max(s => s.UatEnd.Date));
    }
}
