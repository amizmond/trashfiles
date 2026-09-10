using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;

namespace Estimation.Core.Features.Services;

public static class FeatureTargetSprintPlanner
{
    public static List<Pi> ResolvePiCandidates(int? featurePiId, string? featureLabels, IEnumerable<Pi> pis)
    {
        var all = pis as IList<Pi> ?? pis.ToList();

        if (featurePiId is not null)
        {
            var assigned = all.FirstOrDefault(p => p.Id == featurePiId.Value);
            return assigned is null ? [] : [assigned];
        }

        if (string.IsNullOrWhiteSpace(featureLabels))
        {
            return [];
        }

        return all
            .Where(p => PiLabelMatching.Matches(
                PiLabelMatching.ParseLabels(p.FeatureLabels), p.LabelMatchMode, featureLabels))
            .OrderByDescending(p => p.StartDate)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public static HashSet<int> DeriveSelection(
        IEnumerable<CapitalProjectSprint> sprints, DateTime? targetStart, DateTime? targetEnd)
    {
        if (targetStart is null || targetEnd is null)
        {
            return [];
        }

        var from = targetStart.Value.Date;
        var to = targetEnd.Value.Date;
        if (to < from)
        {
            return [];
        }

        return sprints
            .Where(s => s.StartDate.Date >= from && s.EndDate.Date <= to)
            .Select(s => s.Id)
            .ToHashSet();
    }

    public static (DateTime? Start, DateTime? End) ComputeRange(IEnumerable<CapitalProjectSprint> selected)
    {
        var list = selected as IList<CapitalProjectSprint> ?? selected.ToList();
        if (list.Count == 0)
        {
            return (null, null);
        }

        return (list.Min(s => s.StartDate.Date), list.Max(s => s.EndDate.Date));
    }

    public static int CountSpanned(IEnumerable<CapitalProjectSprint> all, DateTime? start, DateTime? end)
    {
        if (start is null || end is null)
        {
            return 0;
        }

        var from = start.Value.Date;
        var to = end.Value.Date;
        return all.Count(s => s.StartDate.Date >= from && s.EndDate.Date <= to);
    }
}
