using Estimation.Core.Capacity.Services;
using Estimation.Core.Features.DependencyMap.Services;
using Estimation.Core.Features.Hygiene.Services;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.DepartmentPlanning.Services;

public interface IDepartmentPlanningService
{
    Task<DepartmentPlanningData?> GetAsync(DepartmentPlanningScope scope, CancellationToken cancellationToken = default);

    Task<PlanningItemDetails?> GetDetailsAsync(PlanningLevel level, int id);
}

public class DepartmentPlanningService : IDepartmentPlanningService
{
    private const int BoardParallelism = 4;

    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly ITeamCapacityService _teamCapacity;
    private readonly IDependencyMapService _dependencyMap;
    private readonly IFeatureHygieneService _hygiene;

    public DepartmentPlanningService(
        IDbContextFactory<EstimationDbContext> ctx,
        ITeamCapacityService teamCapacity,
        IDependencyMapService dependencyMap,
        IFeatureHygieneService hygiene)
    {
        _ctx = ctx;
        _teamCapacity = teamCapacity;
        _dependencyMap = dependencyMap;
        _hygiene = hygiene;
    }

    private sealed record ArtRow(int Id, string Name, int? DepartmentId, string? DepartmentName, ArtPrioritization? Prioritization, List<int> TeamIds);

    private sealed record FeatureRow(
        int Id,
        string? JiraId,
        string Title,
        string? Status,
        string? RagStatus,
        string? RagExplain,
        int? Ranking,
        int? PiId,
        string? PiName,
        int? BusinessOutcomeId,
        int? PortfolioEpicId,
        DateTime? TargetStart,
        DateTime? TargetEnd,
        string? FundingStatus);

    private sealed record Hierarchy(List<PlanningObjective> Objectives, List<PlanningEpic> Epics, List<PlanningOutcome> Outcomes);

    public async Task<DepartmentPlanningData?> GetAsync(DepartmentPlanningScope scope, CancellationToken cancellationToken = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);

        var pi = await db.Pis
            .AsNoTracking()
            .Where(p => p.Id == scope.PiId)
            .Select(p => new { p.Id, p.Name, p.IsLocked, p.StartDate, p.EndDate })
            .FirstOrDefaultAsync(cancellationToken);
        if (pi is null)
        {
            return null;
        }

        var artQuery = db.CapitalProjects.AsNoTracking();
        if (scope.ArtIds.Count > 0)
        {
            var ids = scope.ArtIds.ToList();
            artQuery = artQuery.Where(a => ids.Contains(a.Id));
        }
        else if (scope.DepartmentId is { } departmentId)
        {
            artQuery = artQuery.Where(a => a.DepartmentId == departmentId);
        }

        var arts = await artQuery
            .OrderBy(a => a.Name)
            .Select(a => new ArtRow(
                a.Id,
                a.Name,
                a.DepartmentId,
                a.Department == null ? null : a.Department.Name,
                a.Prioritization,
                a.CapitalProjectTeams.Select(t => t.TeamId).ToList()))
            .ToListAsync(cancellationToken);
        var artIds = arts.Select(a => a.Id).ToList();
        var teamIds = arts.SelectMany(a => a.TeamIds).Distinct().OrderBy(id => id).ToList();

        var teamNames = teamIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Teams
                .AsNoTracking()
                .Where(t => teamIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var boards = await LoadBoardsAsync(teamIds, scope.PiId, scope.IncludeIpSprints, cancellationToken);

        var teams = teamIds
            .Where(boards.ContainsKey)
            .Select(id =>
            {
                var board = boards[id];
                return new PlanningTeam(
                    id,
                    teamNames.GetValueOrDefault(id) ?? $"Team {id}",
                    arts.Where(a => a.TeamIds.Contains(id)).Select(a => a.Id).ToList(),
                    board.StoryPointsPerDay,
                    board.MemberMeta.Sum(m => m.EffectiveDays),
                    (decimal)board.TotalCapacity,
                    board.Members.Count,
                    board.WindowMode);
            })
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var links = new Dictionary<int, List<PlanningFeatureTeam>>();
        foreach (var (teamId, board) in boards)
        {
            var allocations = TeamBoardAllocator.Allocate(
                board.Features.Where(f => f.IsIncludedInCapacity).Select(f => f.FeatureId),
                board.Demands,
                board.MemberMeta);
            var position = 0;
            foreach (var row in board.Features)
            {
                position++;
                if (!links.TryGetValue(row.FeatureId, out var list))
                {
                    links[row.FeatureId] = list = [];
                }

                list.Add(new PlanningFeatureTeam(
                    teamId,
                    row.Effort,
                    row.IsIncluded,
                    row.BelowArtLine,
                    position,
                    row.ArtBoardPosition,
                    allocations.TryGetValue(row.FeatureId, out var allocation) ? allocation.Reserved : 0m));
            }
        }

        var featureIds = links.Keys.ToList();
        var featureRows = await LoadFeaturesAsync(db, featureIds, cancellationToken);
        var hierarchy = await LoadHierarchyAsync(
            db,
            artIds,
            featureRows.Select(f => f.BusinessOutcomeId).OfType<int>().Distinct().ToList(),
            featureRows.Select(f => f.PortfolioEpicId).OfType<int>().Distinct().ToList(),
            cancellationToken);

        var levelByArt = arts
            .Where(a => a.Prioritization is not null)
            .ToDictionary(a => a.Id, a => a.Prioritization!.Value);
        var facts = await ArtOrderLoader.LoadAsync(db, levelByArt, [scope.PiId]);

        var featureIdSet = featureIds.ToHashSet();
        var (dependencies, dependenciesLoaded) = await LoadDependenciesAsync(artIds, scope.PiId, featureIdSet, cancellationToken);
        var (hygiene, hygieneLoaded) = await LoadHygieneAsync(featureIds);

        var abbreviations = ArtAbbreviations.For(arts.Select(a => (a.Id, a.Name)));
        var planningArts = arts
            .Select(a =>
            {
                var resolution = facts.TryGetValue(a.Id, out var artFacts) ? artFacts.Resolve(scope.PiId) : ArtOrderResolution.Empty;
                return new PlanningArt(
                    a.Id,
                    a.Name,
                    abbreviations[a.Id],
                    a.DepartmentId,
                    a.DepartmentName,
                    a.Prioritization,
                    resolution.Mode,
                    a.TeamIds,
                    resolution.Entries);
            })
            .ToList();

        var features = featureRows
            .Select(f =>
            {
                var dependency = dependencies.GetValueOrDefault(f.Id);
                return new PlanningFeature(
                    f.Id,
                    f.JiraId,
                    f.Title,
                    f.Status,
                    f.RagStatus,
                    f.RagExplain,
                    f.Ranking,
                    f.PiId,
                    f.PiName,
                    f.BusinessOutcomeId,
                    f.PortfolioEpicId,
                    f.TargetStart,
                    f.TargetEnd,
                    f.FundingStatus,
                    links.GetValueOrDefault(f.Id) ?? [])
                {
                    DependencyCount = dependency.Count,
                    DependenciesAtRisk = dependency.AtRisk,
                    HygieneFailures = hygiene.GetValueOrDefault(f.Id),
                };
            })
            .OrderBy(f => f.JiraId, Comparer<string?>.Create(ArtOrderResolver.CompareJiraKeys))
            .ThenBy(f => f.Id)
            .ToList();

        return new DepartmentPlanningData(
            scope,
            pi.Id,
            pi.Name,
            pi.IsLocked == true,
            pi.StartDate,
            pi.EndDate,
            planningArts,
            teams,
            features,
            hierarchy.Objectives,
            hierarchy.Epics,
            hierarchy.Outcomes)
        {
            DependenciesLoaded = dependenciesLoaded,
            HygieneLoaded = hygieneLoaded,
        };
    }

    public async Task<PlanningItemDetails?> GetDetailsAsync(PlanningLevel level, int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        return level switch
        {
            PlanningLevel.StrategicObjective => await db.StrategicObjectives
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new PlanningItemDetails(x.Description, x.AcceptanceCriteria, x.Labels, x.Components, x.FixVersions, x.JiraUpdated))
                .FirstOrDefaultAsync(),
            PlanningLevel.PortfolioEpic => await db.PortfolioEpics
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new PlanningItemDetails(x.Description, x.AcceptanceCriteria, x.Labels, x.Components, x.FixVersions, x.JiraUpdated))
                .FirstOrDefaultAsync(),
            PlanningLevel.BusinessOutcome => await db.BusinessOutcomes
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new PlanningItemDetails(x.Description, x.AcceptanceCriteria, x.Labels, x.Components, x.FixVersions, x.JiraUpdated))
                .FirstOrDefaultAsync(),
            PlanningLevel.BusinessEpic => await db.Features
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new PlanningItemDetails(x.Description, x.AcceptanceCriteria, x.Labels, x.Components, x.FixVersions, x.JiraUpdated))
                .FirstOrDefaultAsync(),
            _ => null,
        };
    }

    private async Task<Dictionary<int, TeamCapacityResult>> LoadBoardsAsync(
        IReadOnlyList<int> teamIds, int piId, bool includeIpSprints, CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
        {
            return new Dictionary<int, TeamCapacityResult>();
        }

        using var throttle = new SemaphoreSlim(BoardParallelism, BoardParallelism);
        var tasks = teamIds.Select(async teamId =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                return (TeamId: teamId, Board: await _teamCapacity.GetAsync(teamId, piId, includeIpSprints));
            }
            finally
            {
                throttle.Release();
            }
        });

        var boards = await Task.WhenAll(tasks);
        return boards.ToDictionary(b => b.TeamId, b => b.Board);
    }

    private static async Task<List<FeatureRow>> LoadFeaturesAsync(EstimationDbContext db, List<int> featureIds, CancellationToken cancellationToken)
    {
        if (featureIds.Count == 0)
        {
            return [];
        }

        return await db.Features
            .AsNoTracking()
            .Where(f => featureIds.Contains(f.Id))
            .Select(f => new FeatureRow(
                f.Id,
                f.JiraId,
                f.Name ?? f.Summary,
                f.Status,
                f.RagStatus,
                f.RagExplain,
                f.Ranking,
                f.PiId,
                f.Pi == null ? null : f.Pi.Name,
                f.BusinessOutcomeId,
                f.BusinessOutcome == null ? null : f.BusinessOutcome.PortfolioEpicId,
                f.TargetStart,
                f.TargetEnd,
                f.UnfundedOption == null ? null : f.UnfundedOption.Name))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Hierarchy> LoadHierarchyAsync(
        EstimationDbContext db,
        List<int> artIds,
        List<int> featureOutcomeIds,
        List<int> featureEpicIds,
        CancellationToken cancellationToken)
    {
        var artObjectiveLinks = artIds.Count == 0
            ? []
            : await db.CapitalProjectStrategicObjectives
                .AsNoTracking()
                .Where(l => artIds.Contains(l.CapitalProjectId))
                .Select(l => new { l.StrategicObjectiveId, l.CapitalProjectId })
                .ToListAsync(cancellationToken);
        var linkedObjectiveIds = artObjectiveLinks.Select(l => l.StrategicObjectiveId).Distinct().ToList();

        var objectiveEpicLinks = linkedObjectiveIds.Count + featureEpicIds.Count == 0
            ? []
            : await db.StrategicObjectivePortfolioEpics
                .AsNoTracking()
                .Where(l => linkedObjectiveIds.Contains(l.StrategicObjectiveId) || featureEpicIds.Contains(l.PortfolioEpicId))
                .Select(l => new { l.StrategicObjectiveId, l.PortfolioEpicId })
                .ToListAsync(cancellationToken);

        var epicIds = objectiveEpicLinks.Select(l => l.PortfolioEpicId).Concat(featureEpicIds).Distinct().ToList();
        var objectiveIds = linkedObjectiveIds.Concat(objectiveEpicLinks.Select(l => l.StrategicObjectiveId)).Distinct().ToList();

        var outcomes = featureOutcomeIds.Count + epicIds.Count == 0
            ? []
            : await db.BusinessOutcomes
                .AsNoTracking()
                .Where(bo => featureOutcomeIds.Contains(bo.Id) || (bo.PortfolioEpicId != null && epicIds.Contains(bo.PortfolioEpicId.Value)))
                .Select(bo => new PlanningOutcome(bo.Id, bo.JiraId, bo.Summary, bo.Status, bo.RagStatus, bo.RagExplain, bo.PortfolioEpicId, bo.TargetStart, bo.TargetEnd))
                .ToListAsync(cancellationToken);

        var objectiveIdsByEpic = objectiveEpicLinks
            .GroupBy(l => l.PortfolioEpicId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(l => l.StrategicObjectiveId).Distinct().OrderBy(id => id).ToList());
        var epics = epicIds.Count == 0
            ? []
            : (await db.PortfolioEpics
                    .AsNoTracking()
                    .Where(pe => epicIds.Contains(pe.Id))
                    .Select(pe => new
                    {
                        pe.Id,
                        pe.JiraId,
                        pe.Summary,
                        pe.Status,
                        pe.RagStatus,
                        Funding = pe.UnfundedOption == null ? null : pe.UnfundedOption.Name,
                        pe.TargetStart,
                        pe.TargetEnd,
                    })
                    .ToListAsync(cancellationToken))
                .Select(pe => new PlanningEpic(
                    pe.Id, pe.JiraId, pe.Summary, pe.Status, pe.RagStatus, pe.Funding, pe.TargetStart, pe.TargetEnd,
                    objectiveIdsByEpic.GetValueOrDefault(pe.Id) ?? []))
                .ToList();

        var artIdsByObjective = artObjectiveLinks
            .GroupBy(l => l.StrategicObjectiveId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(l => l.CapitalProjectId).Distinct().OrderBy(id => id).ToList());
        var objectives = objectiveIds.Count == 0
            ? []
            : (await db.StrategicObjectives
                    .AsNoTracking()
                    .Where(so => objectiveIds.Contains(so.Id))
                    .Select(so => new { so.Id, so.JiraId, so.Summary, so.Status, so.RagStatus })
                    .ToListAsync(cancellationToken))
                .Select(so => new PlanningObjective(so.Id, so.JiraId, so.Summary, so.Status, so.RagStatus, artIdsByObjective.GetValueOrDefault(so.Id) ?? []))
                .ToList();

        return new Hierarchy(objectives, epics, outcomes);
    }

    private async Task<(Dictionary<int, (int Count, int AtRisk)> Counts, bool Loaded)> LoadDependenciesAsync(
        List<int> artIds, int piId, HashSet<int> featureIds, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<int, (int Count, int AtRisk)>();
        if (featureIds.Count == 0)
        {
            return (counts, true);
        }

        var seen = new HashSet<(int FeatureId, string DependencyId)>();
        try
        {
            foreach (var artId in artIds)
            {
                var data = await _dependencyMap.GetAsync(artId, piId, true, cancellationToken);
                if (data is null)
                {
                    continue;
                }

                var index = DependencyClassifier.Build(data);
                foreach (var dependency in index.Dependencies)
                {
                    var atRisk = dependency.Verdict.Risk is DependencyRisk.AtRisk or DependencyRisk.OutOfOrder;
                    foreach (var node in new[] { dependency.Provider, dependency.Consumer })
                    {
                        if (node.Feature is not { } feature || !featureIds.Contains(feature.FeatureId) || !seen.Add((feature.FeatureId, dependency.Id)))
                        {
                            continue;
                        }

                        var current = counts.GetValueOrDefault(feature.FeatureId);
                        counts[feature.FeatureId] = (current.Count + 1, current.AtRisk + (atRisk ? 1 : 0));
                    }
                }
            }

            return (counts, true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Department planning: dependencies could not be loaded for PI {PiId}", piId);
            return (counts, false);
        }
    }

    private async Task<(Dictionary<int, int> Failures, bool Loaded)> LoadHygieneAsync(List<int> featureIds)
    {
        if (featureIds.Count == 0)
        {
            return (new Dictionary<int, int>(), true);
        }

        try
        {
            var failures = await _hygiene.GetFailuresByFeatureAsync(featureIds);
            return (failures.ToDictionary(kv => kv.Key, kv => kv.Value.Count), true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Department planning: hygiene results could not be loaded");
            return (new Dictionary<int, int>(), false);
        }
    }
}
