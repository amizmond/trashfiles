using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Services;

public interface IPiPrioritizationService
{
    Task<List<Feature>> GetPrioritizedFeaturesAsync(int piId);
    Task UpdatePrioritiesAsync(List<(int FeatureId, int Priority)> priorities);
    Task MoveFeatureToPiAsync(int featureId, int targetPiId);
    Task MoveFeaturesToPiAsync(List<(int FeatureId, int TargetPiId)> moves);
    Task MoveFeatureAndUpdatePrioritiesAsync(int featureId, int targetPiId,
        List<(int FeatureId, int Priority)> sourcePriorities,
        List<(int FeatureId, int Priority)> targetPriorities);
    Task UpdateFeatureCommentsAsync(List<(int FeatureId, string? Comment)> comments);
}

public class PiPrioritizationService : IPiPrioritizationService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public PiPrioritizationService(IDbContextFactory<EstimationDbContext> ctx)
        => _ctx = ctx;

    public async Task<List<Feature>> GetPrioritizedFeaturesAsync(int piId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var features = await db.Features
            .Where(f => f.PiId == piId)
            .Include(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
                    .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                        .ThenInclude(ppe => ppe.StrategicObjective)
                            .ThenInclude(pp => pp.CapitalProjectStrategicObjectives)
                                .ThenInclude(cpp => cpp.CapitalProject)
            .Include(f => f.FeatureTeams)
                .ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTechnologyStacks)
                .ThenInclude(fts => fts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        var unprioritized = features.Where(f => !f.Ranking.HasValue).ToList();

        if (unprioritized.Count > 0)
        {
            var maxPriority = features
                .Where(f => f.Ranking.HasValue)
                .Select(f => f.Ranking!.Value)
                .DefaultIfEmpty(0)
                .Max();

            await using var writeDb = await _ctx.CreateDbContextAsync();
            var unprioritizedIds = unprioritized.Select(f => f.Id).ToList();
            var tracked = await writeDb.Features
                .Where(f => unprioritizedIds.Contains(f.Id))
                .ToListAsync();
            var trackedDict = tracked.ToDictionary(f => f.Id);

            foreach (var feature in unprioritized)
            {
                maxPriority++;
                feature.Ranking = maxPriority;

                if (trackedDict.TryGetValue(feature.Id, out var t))
                    t.Ranking = maxPriority;
            }

            await writeDb.SaveChangesAsync();
        }

        return features
            .OrderBy(f => f.Ranking ?? int.MaxValue)
            .ToList();
    }

    public async Task UpdatePrioritiesAsync(List<(int FeatureId, int Priority)> priorities)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var idToPriority = priorities.ToDictionary(p => p.FeatureId, p => p.Priority);
        var featureIds = idToPriority.Keys.ToList();

        var features = await db.Features
            .Where(f => featureIds.Contains(f.Id))
            .ToListAsync();

        foreach (var feature in features)
        {
            if (idToPriority.TryGetValue(feature.Id, out var priority))
                feature.Ranking = priority;
        }

        await db.SaveChangesAsync();
    }

    public async Task MoveFeatureToPiAsync(int featureId, int targetPiId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var feature = await db.Features.FindAsync(featureId);
        if (feature is null) return;

        feature.PiId = targetPiId;
        feature.Ranking = null;

        await db.SaveChangesAsync();
    }

    public async Task MoveFeaturesToPiAsync(List<(int FeatureId, int TargetPiId)> moves)
    {
        if (moves.Count == 0) return;

        await using var db = await _ctx.CreateDbContextAsync();

        var moveDict = moves.ToDictionary(m => m.FeatureId, m => m.TargetPiId);
        var featureIds = moveDict.Keys.ToList();

        var features = await db.Features
            .Where(f => featureIds.Contains(f.Id))
            .ToListAsync();

        foreach (var feature in features)
        {
            if (moveDict.TryGetValue(feature.Id, out var targetPiId))
                feature.PiId = targetPiId;
        }

        await db.SaveChangesAsync();
    }

    public async Task MoveFeatureAndUpdatePrioritiesAsync(int featureId, int targetPiId,
        List<(int FeatureId, int Priority)> sourcePriorities,
        List<(int FeatureId, int Priority)> targetPriorities)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var feature = await db.Features.FindAsync(featureId);
        if (feature is not null)
        {
            feature.PiId = targetPiId;
        }

        var allPriorities = sourcePriorities.Concat(targetPriorities).ToList();
        foreach (var (fId, priority) in allPriorities)
        {
            var f = await db.Features.FindAsync(fId);
            if (f is not null)
            {
                f.Ranking = priority;
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task UpdateFeatureCommentsAsync(List<(int FeatureId, string? Comment)> comments)
    {
        if (comments.Count == 0) return;

        await using var db = await _ctx.CreateDbContextAsync();

        var commentDict = comments.ToDictionary(c => c.FeatureId, c => c.Comment);
        var featureIds = commentDict.Keys.ToList();

        var features = await db.Features
            .Where(f => featureIds.Contains(f.Id))
            .ToListAsync();

        foreach (var feature in features)
        {
            if (commentDict.TryGetValue(feature.Id, out var comment))
                feature.Comments = comment;
        }

        await db.SaveChangesAsync();
    }
}
