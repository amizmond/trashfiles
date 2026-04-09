using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Services;

public interface IPiCapacityService
{
    Task<PiCapacityResult> CalculateCapacityAsync(List<Feature> features);
}

public class PiCapacityService : IPiCapacityService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public PiCapacityService(IDbContextFactory<EstimationDbContext> ctx)
        => _ctx = ctx;

    public async Task<PiCapacityResult> CalculateCapacityAsync(List<Feature> features)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        // Load all teams with their TechnologyStacks and members' skills
        var teams = await db.Teams
            .Include(t => t.TeamTechnologyStacks).ThenInclude(tts => tts.TechnologyStack)
                .ThenInclude(ts => ts.TechnologyStackSkills).ThenInclude(tss => tss.Skill)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.Skill)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.SkillLevel)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        // Load features with FeatureTechnologyStacks and FeatureTeams
        var featureIds = features.Select(f => f.Id).ToList();
        var featuresWithDetails = await db.Features
            .Where(f => featureIds.Contains(f.Id))
            .Include(f => f.FeatureTechnologyStacks).ThenInclude(fts => fts.TechnologyStack)
                .ThenInclude(ts => ts.TechnologyStackSkills)
            .Include(f => f.FeatureTeams)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        var featureDict = featuresWithDetails.ToDictionary(f => f.Id);

        // Build team capacity per TechnologyStack
        // For each team, for each TechnologyStack the team has, capacity = sum of member skill levels
        // for the skills that belong to that TechnologyStack
        var teamTsCapacity = BuildTeamTechnologyStackCapacity(teams);

        // Process features in ranking order (lower ranking = higher priority)
        var orderedFeatures = features
            .OrderBy(f => f.Ranking ?? int.MaxValue)
            .ToList();

        var result = new PiCapacityResult();

        foreach (var feature in orderedFeatures)
        {
            if (!featureDict.TryGetValue(feature.Id, out var detailedFeature))
                continue;

            var featureResult = CalculateFeatureCapacity(detailedFeature, teams, teamTsCapacity);
            result.FeatureResults.Add(featureResult);
        }

        // Build summary
        result.Summary = new PiCapacitySummary
        {
            TotalFeatures = result.FeatureResults.Count,
            FeaturesFullyReserved = result.FeatureResults.Count(r => r.IsFullyReserved),
            FeaturesPartiallyReserved = result.FeatureResults.Count(r =>
                !r.IsFullyReserved && r.TotalReserved > 0),
            FeaturesNotReserved = result.FeatureResults.Count(r => r.TotalReserved == 0 && r.TotalRequired > 0),
            TotalRequiredCapacity = result.FeatureResults.Sum(r => r.TotalRequired),
            TotalReservedCapacity = result.FeatureResults.Sum(r => r.TotalReserved),
            TotalUnreservedCapacity = result.FeatureResults.Sum(r => r.TotalRequired - r.TotalReserved),
        };

        return result;
    }

    /// <summary>
    /// Builds a mutable dictionary: TeamId -> (TechnologyStackId -> available capacity).
    /// Capacity for a TechnologyStack = sum of SkillLevel.Value (or 1 if null) for each active team member
    /// who has any skill that belongs to that TechnologyStack.
    /// </summary>
    private static Dictionary<int, Dictionary<int, int>> BuildTeamTechnologyStackCapacity(List<Team> teams)
    {
        var result = new Dictionary<int, Dictionary<int, int>>();

        foreach (var team in teams)
        {
            var tsCapacity = new Dictionary<int, int>();

            foreach (var tts in team.TeamTechnologyStacks)
            {
                var tsSkillIds = tts.TechnologyStack.TechnologyStackSkills
                    .Select(tss => tss.SkillId).ToHashSet();

                var capacity = 0;
                foreach (var member in team.TeamMembers)
                {
                    if (!member.HumanResource.IsActive) continue;

                    foreach (var hrs in member.HumanResource.HumanResourceSkills)
                    {
                        if (tsSkillIds.Contains(hrs.SkillId))
                        {
                            capacity += hrs.SkillLevel?.Value ?? 1;
                        }
                    }
                }

                tsCapacity[tts.TechnologyStackId] = capacity;
            }

            result[team.Id] = tsCapacity;
        }

        return result;
    }

    private static FeatureCapacityResult CalculateFeatureCapacity(
        Feature feature,
        List<Team> teams,
        Dictionary<int, Dictionary<int, int>> teamTsCapacity)
    {
        var featureResult = new FeatureCapacityResult
        {
            FeatureId = feature.Id,
            FeatureName = feature.Name,
            JiraId = feature.JiraId,
            Ranking = feature.Ranking,
        };

        if (feature.FeatureTechnologyStacks.Count == 0)
        {
            featureResult.IsFullyReserved = true;
            return featureResult;
        }

        foreach (var fts in feature.FeatureTechnologyStacks)
        {
            var allocation = AllocateTechnologyStackCapacity(fts, feature, teams, teamTsCapacity);
            featureResult.TechnologyStackAllocations.Add(allocation);
        }

        featureResult.TotalRequired = featureResult.TechnologyStackAllocations.Sum(a => a.Required);
        featureResult.TotalReserved = featureResult.TechnologyStackAllocations.Sum(a => a.Reserved);
        featureResult.IsFullyReserved = featureResult.TechnologyStackAllocations.All(a => a.IsReserved);

        return featureResult;
    }

    private static TechnologyStackCapacityAllocation AllocateTechnologyStackCapacity(
        FeatureTechnologyStack featureTechStack,
        Feature feature,
        List<Team> teams,
        Dictionary<int, Dictionary<int, int>> teamTsCapacity)
    {
        var allocation = new TechnologyStackCapacityAllocation
        {
            TechnologyStackId = featureTechStack.TechnologyStackId,
            TechnologyStackName = featureTechStack.TechnologyStack.Name,
            Required = featureTechStack.EstimatedEffort ?? 0,
        };

        var remaining = allocation.Required;
        if (remaining <= 0)
        {
            allocation.Reserved = 0;
            return allocation;
        }

        // Priority 1: Teams assigned to the Feature that have this TechnologyStack
        var assignedTeamIds = feature.FeatureTeams.Select(ft => ft.TeamId).ToHashSet();
        remaining = TryAllocateFromTeams(
            assignedTeamIds, featureTechStack.TechnologyStackId,
            remaining, teams, teamTsCapacity, allocation, "AssignedTeam");

        // Priority 2: Any other team that has this TechnologyStack
        if (remaining > 0)
        {
            var otherTeamIds = teams
                .Where(t => !assignedTeamIds.Contains(t.Id) &&
                             t.TeamTechnologyStacks.Any(tts => tts.TechnologyStackId == featureTechStack.TechnologyStackId))
                .Select(t => t.Id)
                .ToHashSet();

            remaining = TryAllocateFromTeams(
                otherTeamIds, featureTechStack.TechnologyStackId,
                remaining, teams, teamTsCapacity, allocation, "MatchingTeam");
        }

        allocation.Reserved = allocation.Required - remaining;
        return allocation;
    }

    private static int TryAllocateFromTeams(
        HashSet<int> teamIds,
        int technologyStackId,
        int remaining,
        List<Team> teams,
        Dictionary<int, Dictionary<int, int>> teamTsCapacity,
        TechnologyStackCapacityAllocation allocation,
        string matchMethod)
    {
        foreach (var teamId in teamIds)
        {
            if (remaining <= 0) break;

            if (!teamTsCapacity.TryGetValue(teamId, out var tsCapacities)) continue;
            if (!tsCapacities.TryGetValue(technologyStackId, out var available) || available <= 0) continue;

            var toAllocate = Math.Min(remaining, available);
            tsCapacities[technologyStackId] -= toAllocate;
            remaining -= toAllocate;

            var team = teams.FirstOrDefault(t => t.Id == teamId);
            var existing = allocation.TeamAllocations.FirstOrDefault(ta => ta.TeamId == teamId);
            if (existing != null)
            {
                existing.AllocatedCapacity += toAllocate;
                existing.RemainingCapacity = tsCapacities[technologyStackId];
            }
            else
            {
                allocation.TeamAllocations.Add(new TeamAllocation
                {
                    TeamId = teamId,
                    TeamName = team?.Name ?? $"Team {teamId}",
                    AllocatedCapacity = toAllocate,
                    RemainingCapacity = tsCapacities[technologyStackId],
                });
            }

            allocation.MatchMethod ??= matchMethod;
        }

        return remaining;
    }
}
