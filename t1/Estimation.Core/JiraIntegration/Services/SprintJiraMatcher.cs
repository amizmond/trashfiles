using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.PlanningIncrement.Models;

namespace Estimation.Core.JiraIntegration.Services;

public enum SprintMatchKind
{
    None = 0,
    ByName = 1,
    ByDates = 2,
}

public record SprintJiraMatch(Sprint Local, JiraAgileSprint? Jira, SprintMatchKind Kind);

/// <summary>
/// Pure matching of local Sprint rows to a board's Jira sprints. Name match wins; duplicate
/// names on the board are disambiguated by date overlap; remaining locals fall back to the
/// best date overlap covering at least half the local sprint. Each Jira sprint is claimed at
/// most once.
/// </summary>
public static class SprintJiraMatcher
{
    public static List<SprintJiraMatch> Match(
        IReadOnlyList<Sprint> localSprints,
        IReadOnlyList<JiraAgileSprint> jiraSprints)
    {
        var byName = jiraSprints
            .GroupBy(s => Normalize(s.Name))
            .ToDictionary(g => g.Key, g => g.ToList());
        var claimed = new HashSet<int>();
        var results = new List<SprintJiraMatch>();
        var unmatched = new List<Sprint>();

        foreach (var local in localSprints)
        {
            JiraAgileSprint? match = null;
            if (byName.TryGetValue(Normalize(local.Name), out var candidates))
            {
                var free = candidates.Where(c => !claimed.Contains(c.Id)).ToList();
                if (free.Count == 1)
                {
                    match = free[0];
                }
                else if (free.Count > 1)
                {
                    match = free
                        .Select(c => (Sprint: c, Overlap: OverlapDays(local, c)))
                        .Where(x => x.Overlap > 0)
                        .OrderByDescending(x => x.Overlap)
                        .Select(x => x.Sprint)
                        .FirstOrDefault();
                }
            }

            if (match is not null)
            {
                claimed.Add(match.Id);
                results.Add(new SprintJiraMatch(local, match, SprintMatchKind.ByName));
            }
            else
            {
                unmatched.Add(local);
            }
        }

        foreach (var local in unmatched)
        {
            var localLength = Math.Max(1.0, (local.EndDate.Date - local.StartDate.Date).TotalDays + 1);
            var best = jiraSprints
                .Where(c => !claimed.Contains(c.Id))
                .Select(c => (Sprint: c, Overlap: OverlapDays(local, c)))
                .Where(x => x.Overlap >= localLength / 2.0)
                .OrderByDescending(x => x.Overlap)
                .Select(x => x.Sprint)
                .FirstOrDefault();

            if (best is not null)
            {
                claimed.Add(best.Id);
                results.Add(new SprintJiraMatch(local, best, SprintMatchKind.ByDates));
            }
            else
            {
                results.Add(new SprintJiraMatch(local, null, SprintMatchKind.None));
            }
        }

        return results;
    }

    private static double OverlapDays(Sprint local, JiraAgileSprint jira)
    {
        var jiraStart = jira.StartDate;
        var jiraEnd = jira.CompleteDate ?? jira.EndDate;
        if (jiraStart is null || jiraEnd is null)
        {
            return 0;
        }

        // Local dates are date-only with an inclusive end; extend to the end of that day.
        var localStart = local.StartDate.Date;
        var localEnd = local.EndDate.Date.AddDays(1);

        var start = localStart > jiraStart.Value ? localStart : jiraStart.Value;
        var end = localEnd < jiraEnd.Value ? localEnd : jiraEnd.Value;
        return (end - start).TotalDays;
    }

    private static string Normalize(string name)
    {
        return name.Trim().ToUpperInvariant();
    }
}
