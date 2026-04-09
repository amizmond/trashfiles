using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace Estimation.Core.Services;

public record ChartItem(string Label, double Value);

public record PiTimelineItem(string Name, DateTime? StartDate, DateTime? EndDate, int FeatureCount, bool IsCurrent);

public record CapitalProjectCoverageItem(string Name, int ProgramCount, int TeamCount, int FeatureCount);

public record CapitalProjectOption(int Id, string Name);

public record DashboardData(
    List<ChartItem> FeaturesByPi,
    List<ChartItem> FeaturesByUnfundedStatus,
    List<ChartItem> TeamWorkload,
    List<ChartItem> HrByCategory,
    List<ChartItem> HrByVendor,
    List<ChartItem> TeamSizes,
    List<ChartItem> SkillCoverage,
    List<PiTimelineItem> PiTimeline,
    List<ChartItem> ResourcesByLocation,
    List<CapitalProjectCoverageItem> CapitalProjectCoverage,
    List<ChartItem> FeaturesByBusinessOutcome,
    List<ChartItem> FeaturesByEpic);

public interface IDashboardService
{
    Task<DashboardData> GetDashboardDataAsync(int? capitalProjectId = null, CancellationToken ct = default);
    Task<List<CapitalProjectOption>> GetCapitalProjectOptionsAsync(CancellationToken ct = default);
}

public class DashboardService : IDashboardService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new();
    private static readonly SemaphoreSlim _connectionThrottle = new(4, 4);

    public DashboardService(IDbContextFactory<EstimationDbContext> ctx, IMemoryCache cache)
    {
        _ctx = ctx;
        _cache = cache;
    }

    public async Task<List<CapitalProjectOption>> GetCapitalProjectOptionsAsync(CancellationToken ct = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(ct);
        return await db.CapitalProjects
            .AsNoTracking()
            .OrderBy(cp => cp.Name)
            .Select(cp => new CapitalProjectOption(cp.Id, cp.Name))
            .ToListAsync(ct);
    }

    public async Task<DashboardData> GetDashboardDataAsync(int? capitalProjectId = null, CancellationToken ct = default)
    {
        // Resolve feature IDs once for all filtered queries
        List<int>? featureIds = null;
        if (capitalProjectId is not null)
            featureIds = await RunThrottledAsync((db, t) => GetFilteredFeatureIdsAsync(db, capitalProjectId.Value, t), ct);

        var filterSuffix = capitalProjectId.HasValue ? $":{capitalProjectId.Value}" : ":all";

        // Filtered queries (cached by capitalProjectId)
        var featuresByPiTask = GetCachedAsync($"dash:featuresByPi{filterSuffix}", (db, t) => GetFeaturesByPiAsync(db, featureIds, t), ct);
        var featuresByUnfundedTask = GetCachedAsync($"dash:featuresByUnfunded{filterSuffix}", (db, t) => GetFeaturesByUnfundedStatusAsync(db, featureIds, t), ct);
        var teamWorkloadTask = GetCachedAsync($"dash:teamWorkload{filterSuffix}", (db, t) => GetTeamWorkloadAsync(db, featureIds, t), ct);
        var piTimelineTask = GetCachedAsync($"dash:piTimeline{filterSuffix}", (db, t) => GetPiTimelineAsync(db, featureIds, t), ct);
        var featuresByBusinessOutcomeTask = GetCachedAsync($"dash:featuresByBO{filterSuffix}", (db, t) => GetFeaturesByBusinessOutcomeAsync(db, featureIds, t), ct);
        var featuresByEpicTask = GetCachedAsync($"dash:featuresByEpic{filterSuffix}", (db, t) => GetFeaturesByEpicAsync(db, featureIds, t), ct);

        // Static queries (cached, independent of capitalProjectId)
        var hrByCategoryTask = GetCachedAsync("dash:hrByCategory", (db, t) => GetHrByCategoryAsync(db, t), ct);
        var hrByVendorTask = GetCachedAsync("dash:hrByVendor", (db, t) => GetHrByVendorAsync(db, t), ct);
        var teamSizesTask = GetCachedAsync("dash:teamSizes", (db, t) => GetTeamSizesAsync(db, t), ct);
        var skillCoverageTask = GetCachedAsync("dash:skillCoverage", (db, t) => GetSkillCoverageAsync(db, t), ct);
        var resourcesByLocationTask = GetCachedAsync("dash:resourcesByLocation", (db, t) => GetResourcesByLocationAsync(db, t), ct);
        var capitalProjectCoverageTask = GetCachedAsync("dash:capitalProjectCoverage", (db, t) => GetCapitalProjectCoverageAsync(db, t), ct);

        await Task.WhenAll(featuresByPiTask, featuresByUnfundedTask, teamWorkloadTask,
            hrByCategoryTask, hrByVendorTask, teamSizesTask, skillCoverageTask, piTimelineTask,
            resourcesByLocationTask, capitalProjectCoverageTask,
            featuresByBusinessOutcomeTask, featuresByEpicTask);

        return new DashboardData(
            featuresByPiTask.Result,
            featuresByUnfundedTask.Result,
            teamWorkloadTask.Result,
            hrByCategoryTask.Result,
            hrByVendorTask.Result,
            teamSizesTask.Result,
            skillCoverageTask.Result,
            piTimelineTask.Result,
            resourcesByLocationTask.Result,
            capitalProjectCoverageTask.Result,
            featuresByBusinessOutcomeTask.Result,
            featuresByEpicTask.Result);
    }

    private async Task<T> RunThrottledAsync<T>(Func<EstimationDbContext, CancellationToken, Task<T>> query, CancellationToken ct)
    {
        await _connectionThrottle.WaitAsync(ct);
        try
        {
            await using var db = await _ctx.CreateDbContextAsync(ct);
            return await query(db, ct);
        }
        finally
        {
            _connectionThrottle.Release();
        }
    }

    private async Task<T> GetCachedAsync<T>(string key, Func<EstimationDbContext, CancellationToken, Task<T>> query, CancellationToken ct)
    {
        if (_cache.TryGetValue(key, out T? cached))
            return cached!;

        var keyLock = _cacheLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out cached))
                return cached!;

            var result = await RunThrottledAsync(query, ct);
            _cache.Set(key, result, CacheDuration);
            return result;
        }
        finally
        {
            keyLock.Release();
        }
    }

    private static async Task<List<int>> GetFilteredFeatureIdsAsync(EstimationDbContext db, int capitalProjectId, CancellationToken ct)
    {
        return await db.Features
            .AsNoTracking()
            .Where(f =>
                f.BusinessOutcome != null &&
                f.BusinessOutcome.PortfolioEpic != null &&
                f.BusinessOutcome.PortfolioEpic.StrategicObjectivePortfolioEpics
                    .Any(ppe => ppe.StrategicObjective.CapitalProjectStrategicObjectives
                        .Any(cpp => cpp.CapitalProjectId == capitalProjectId)))
            .Select(f => f.Id)
            .ToListAsync(ct);
    }

    private static IQueryable<Models.Feature> FilterByIds(EstimationDbContext db, List<int>? featureIds)
    {
        var query = db.Features.AsNoTracking();
        if (featureIds is not null)
            query = query.Where(f => featureIds.Contains(f.Id));
        return query;
    }

    private static async Task<List<ChartItem>> GetFeaturesByPiAsync(EstimationDbContext db, List<int>? featureIds, CancellationToken ct)
    {
        var raw = await FilterByIds(db, featureIds)
            .Where(f => f.PiId != null)
            .GroupBy(f => f.Pi!.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderBy(c => c.Label)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }

    private static async Task<List<ChartItem>> GetFeaturesByUnfundedStatusAsync(EstimationDbContext db, List<int>? featureIds, CancellationToken ct)
    {
        var raw = await FilterByIds(db, featureIds)
            .GroupBy(f => f.UnfundedOptionId == null ? "Funded" : f.UnfundedOption!.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return raw
            .OrderBy(r => r.Label == "Funded" ? 0 : 1)
            .ThenBy(r => r.Label)
            .Select(r => new ChartItem(r.Label, r.Count))
            .ToList();
    }

    private static async Task<List<ChartItem>> GetTeamWorkloadAsync(EstimationDbContext db, List<int>? featureIds, CancellationToken ct)
    {
        IQueryable<Models.FeatureTeam> query = db.FeatureTeams.AsNoTracking();
        if (featureIds is not null)
            query = query.Where(ft => featureIds.Contains(ft.FeatureId));

        var raw = await query
            .GroupBy(ft => ft.Team.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }

    private static async Task<List<ChartItem>> GetHrByCategoryAsync(EstimationDbContext db, CancellationToken ct)
    {
        var raw = await db.HumanResources
            .AsNoTracking()
            .Where(hr => hr.EmployeeCategoryId != null)
            .GroupBy(hr => hr.EmployeeCategory!.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }

    private static async Task<List<ChartItem>> GetHrByVendorAsync(EstimationDbContext db, CancellationToken ct)
    {
        var raw = await db.HumanResources
            .AsNoTracking()
            .Where(hr => hr.EmployeeVendorId != null)
            .GroupBy(hr => hr.EmployeeVendor!.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }

    private static async Task<List<ChartItem>> GetTeamSizesAsync(EstimationDbContext db, CancellationToken ct)
    {
        var raw = await db.TeamMembers
            .AsNoTracking()
            .GroupBy(tm => tm.Team.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }

    private static async Task<List<ChartItem>> GetSkillCoverageAsync(EstimationDbContext db, CancellationToken ct)
    {
        var raw = await db.HumanResourceSkills
            .AsNoTracking()
            .GroupBy(hrs => hrs.Skill.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }

    private static async Task<List<PiTimelineItem>> GetPiTimelineAsync(EstimationDbContext db, List<int>? featureIds, CancellationToken ct)
    {
        var today = DateTime.Today;

        if (featureIds is null)
        {
            var raw = await db.Pis
                .AsNoTracking()
                .Select(p => new { p.Name, p.StartDate, p.EndDate, FeatureCount = p.Features.Count })
                .OrderBy(p => p.StartDate)
                .ToListAsync(ct);
            return raw.Select(r => new PiTimelineItem(
                r.Name, r.StartDate, r.EndDate, r.FeatureCount,
                r.StartDate != null && r.StartDate <= today && (r.EndDate == null || today <= r.EndDate))).ToList();
        }
        else
        {
            var raw = await db.Pis
                .AsNoTracking()
                .Select(p => new
                {
                    p.Name,
                    p.StartDate,
                    p.EndDate,
                    FeatureCount = p.Features.Count(f => featureIds.Contains(f.Id))
                })
                .OrderBy(p => p.StartDate)
                .ToListAsync(ct);
            return raw.Select(r => new PiTimelineItem(
                r.Name, r.StartDate, r.EndDate, r.FeatureCount,
                r.StartDate != null && r.StartDate <= today && (r.EndDate == null || today <= r.EndDate))).ToList();
        }
    }

    private static async Task<List<ChartItem>> GetResourcesByLocationAsync(EstimationDbContext db, CancellationToken ct)
    {
        var raw = await db.HumanResources
            .AsNoTracking()
            .Where(hr => hr.CountryId != null)
            .GroupBy(hr => hr.Country!.Name)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }

    private static async Task<List<CapitalProjectCoverageItem>> GetCapitalProjectCoverageAsync(EstimationDbContext db, CancellationToken ct)
    {
        var raw = await db.CapitalProjects
            .AsNoTracking()
            .Select(cp => new
            {
                cp.Name,
                ProgramCount = cp.CapitalProjectStrategicObjectives.Count,
                TeamCount = cp.CapitalProjectTeams.Count,
                FeatureCount = cp.CapitalProjectStrategicObjectives
                    .SelectMany(cpp => cpp.StrategicObjective.StrategicObjectivePortfolioEpics)
                    .SelectMany(ppe => ppe.PortfolioEpic.BusinessOutcomes)
                    .SelectMany(bo => bo.Features)
                    .Count()
            })
            .OrderBy(cp => cp.Name)
            .ToListAsync(ct);
        return raw.Select(r => new CapitalProjectCoverageItem(r.Name, r.ProgramCount, r.TeamCount, r.FeatureCount)).ToList();
    }

    private static async Task<List<ChartItem>> GetFeaturesByBusinessOutcomeAsync(EstimationDbContext db, List<int>? featureIds, CancellationToken ct)
    {
        var raw = await FilterByIds(db, featureIds)
            .Where(f => f.BusinessOutcomeId != null)
            .GroupBy(f => f.BusinessOutcome!.Summary ?? "(unnamed)")
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }

    private static async Task<List<ChartItem>> GetFeaturesByEpicAsync(EstimationDbContext db, List<int>? featureIds, CancellationToken ct)
    {
        var raw = await FilterByIds(db, featureIds)
            .Where(f => f.BusinessOutcome != null && f.BusinessOutcome.PortfolioEpicId != null)
            .GroupBy(f => f.BusinessOutcome!.PortfolioEpic!.Summary ?? "(unnamed)")
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);
        return raw.Select(r => new ChartItem(r.Label, r.Count)).ToList();
    }
}
