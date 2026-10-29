namespace Estimation.Core.Capacity.Services;

public static class TeamStoryPointShares
{
    public static Dictionary<int, decimal> Of(int? featureStoryPoints, IReadOnlyList<TeamCapacityFeatureTeamRef> teams)
    {
        var shares = new Dictionary<int, decimal>();
        var open = new List<int>();
        var estimated = 0;
        var ordered = teams
            .DistinctBy(t => t.TeamId)
            .OrderByDescending(t => t.IsPrimary)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.TeamId);
        foreach (var team in ordered)
        {
            if (team.StoryPoints is { } own)
            {
                shares[team.TeamId] = own;
                estimated += own;
            }
            else
            {
                open.Add(team.TeamId);
            }
        }

        if (open.Count == 0)
        {
            return shares;
        }

        var rest = Math.Max(0, (featureStoryPoints ?? 0) - estimated);
        var hundredths = StoryPointSplitter.Split(rest * 100, open.Select(id => (id, 1m)).ToList());
        foreach (var id in open)
        {
            shares[id] = hundredths[id] / 100m;
        }

        return shares;
    }

    public static decimal ShareOf(int teamId, int? teamStoryPoints, int? featureStoryPoints, IReadOnlyList<TeamCapacityFeatureTeamRef>? teams) =>
        teams is not null && Of(featureStoryPoints, teams).TryGetValue(teamId, out var share)
            ? share
            : teamStoryPoints ?? featureStoryPoints ?? 0;
}
