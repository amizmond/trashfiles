using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Shared.Services;

public interface ISolutionTrainResolver
{
    Task<ArtMatcher> GetArtMatcherAsync();

    TrainScope ResolveForIssue(JiraIssue issue, ArtMatcher matcher);

    TrainScope ResolveForChildren(IEnumerable<JiraIssue> childFeatures, ArtMatcher matcher);

    Task<TrainScope> ResolveForTeamAsync(int teamId);

    Task<TrainScope> ResolveForHumanResourceAsync(int humanResourceId);

    Task<IReadOnlyDictionary<int, IReadOnlyCollection<int>>> GetTeamTrainsAsync(IEnumerable<int> teamIds);
}

public class SolutionTrainResolver : ISolutionTrainResolver
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public SolutionTrainResolver(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<ArtMatcher> GetArtMatcherAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await ArtMatcher.LoadAsync(db);
    }

    public TrainScope ResolveForIssue(JiraIssue issue, ArtMatcher matcher) => ScopeOf(matcher.Match(issue));

    public static TrainScope ScopeOf(ArtMatch match) => match.Kind switch
    {
        ArtMatchKind.NoKey => TrainScope.Unrestricted,
        ArtMatchKind.Matched => TrainScope.ForTrains([match.ArtId!.Value]),
        _ => TrainScope.AdminOrGeneralOnly
    };

    public TrainScope ResolveForChildren(IEnumerable<JiraIssue> childFeatures, ArtMatcher matcher)
    {
        var trainIds = new HashSet<int>();
        var hasChildren = false;
        var hasUnrestrictedChild = false;

        foreach (var child in childFeatures)
        {
            hasChildren = true;
            var match = matcher.Match(child);
            if (match.Kind == ArtMatchKind.NoKey)
            {
                hasUnrestrictedChild = true;
            }
            else if (match.IsMatched)
            {
                trainIds.Add(match.ArtId!.Value);
            }
        }

        if (trainIds.Count > 0)
        {
            return TrainScope.ForTrains(trainIds);
        }

        if (!hasChildren)
        {
            return TrainScope.AdminOrGeneralOnly;
        }

        return hasUnrestrictedChild ? TrainScope.Unrestricted : TrainScope.AdminOrGeneralOnly;
    }

    public async Task<TrainScope> ResolveForTeamAsync(int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var trainIds = await db.CapitalProjectTeams
            .AsNoTracking()
            .Where(ct => ct.TeamId == teamId)
            .Select(ct => ct.CapitalProjectId)
            .ToListAsync();

        return TrainScope.ForTrains(trainIds);
    }

    public async Task<TrainScope> ResolveForHumanResourceAsync(int humanResourceId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var trainIds = await db.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.HumanResourceId == humanResourceId)
            .SelectMany(tm => tm.Team.CapitalProjectTeams.Select(ct => ct.CapitalProjectId))
            .Distinct()
            .ToListAsync();

        return TrainScope.ForTrains(trainIds);
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyCollection<int>>> GetTeamTrainsAsync(IEnumerable<int> teamIds)
    {
        var ids = teamIds.Distinct().ToList();
        await using var db = await _ctx.CreateDbContextAsync();
        var rows = await db.CapitalProjectTeams
            .AsNoTracking()
            .Where(ct => ids.Contains(ct.TeamId))
            .Select(ct => new { ct.TeamId, ct.CapitalProjectId })
            .ToListAsync();

        return rows
            .GroupBy(r => r.TeamId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(r => r.CapitalProjectId).ToList());
    }
}
