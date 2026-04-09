using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public record FeatureListItem(
    int Id,
    string? JiraId,
    string? Name,
    int? Ranking,
    string? PiName,
    string? BusinessOutcomeName,
    string? UnfundedOptionName,
    List<string> TeamNames);

public record FeatureListPagedResult(List<FeatureListItem> Items, int TotalCount);

public record JiraFeatureSyncItem(string JiraKey, string? Summary, string? Description, string? IssueType, string? Labels, string? FeatureName, string? ParentLink);

public interface IFeatureService
{
    Task<List<Feature>> GetAllAsync();
    Task<List<Feature>> GetAllWithHierarchyAsync();
    Task<(List<Feature> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, string? search = null);
    Task<FeatureListPagedResult> GetPagedListAsync(int page, int pageSize, string? search, string? sortField, bool sortAsc, CancellationToken ct = default);
    Task<Feature?> GetByIdAsync(int id);
    Task<Feature> CreateAsync(Feature feature);
    Task<Feature> UpdateAsync(Feature feature);
    Task<bool> DeleteAsync(int id);
    Task AddTeamAsync(int featureId, int teamId);
    Task RemoveTeamAsync(int featureId, int teamId);
    Task<FeatureTechnologyStack> AddFeatureTechnologyStackAsync(int featureId, int technologyStackId, int? estimatedEffort);
    Task SetFeatureTechnologyStackEffortAsync(int featureTechnologyStackId, int? estimatedEffort);
    Task RemoveFeatureTechnologyStackAsync(int featureTechnologyStackId);
    Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraFeatureSyncItem> items);
}

public class FeatureService : IFeatureService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public FeatureService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<Feature>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features
            .Include(f => f.Pi)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.UnfundedOption)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(f => f.Ranking).ThenBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<List<Feature>> GetAllWithHierarchyAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features
            .Include(f => f.Pi)
            .Include(f => f.BusinessOutcome)
                .ThenInclude(bo => bo!.PortfolioEpic)
                    .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                        .ThenInclude(ppe => ppe.StrategicObjective)
                            .ThenInclude(pp => pp.CapitalProjectStrategicObjectives)
                                .ThenInclude(cpp => cpp.CapitalProject)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(f => f.Ranking).ThenBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<(List<Feature> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, string? search = null)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        IQueryable<Feature> query = db.Features;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(f =>
                (f.Name != null && f.Name.Contains(term)) ||
                (f.JiraId != null && f.JiraId.Contains(term)) ||
                (f.Pi != null && f.Pi.Name.Contains(term)) ||
                (f.BusinessOutcome != null && f.BusinessOutcome.Summary != null && f.BusinessOutcome.Summary.Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .Include(f => f.Pi)
            .Include(f => f.UnfundedOption)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.FeatureTeams)
                .ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTechnologyStacks)
                .ThenInclude(fts => fts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(f => f.Ranking)
                .ThenBy(f => f.Name)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<FeatureListPagedResult> GetPagedListAsync(
        int page, int pageSize, string? search, string? sortField, bool sortAsc, CancellationToken ct = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(ct);

        var baseQuery = db.Features.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQuery = baseQuery.Where(f =>
                (f.Name != null && f.Name.Contains(term)) ||
                (f.JiraId != null && f.JiraId.Contains(term)) ||
                (f.Pi != null && f.Pi.Name.Contains(term)) ||
                (f.BusinessOutcome != null && f.BusinessOutcome.Summary != null && f.BusinessOutcome.Summary.Contains(term)));
        }

        var totalCount = await baseQuery.CountAsync(ct);

        var sorted = sortField?.ToLowerInvariant() switch
        {
            "jiraid" => sortAsc ? baseQuery.OrderBy(f => f.JiraId) : baseQuery.OrderByDescending(f => f.JiraId),
            "name" => sortAsc ? baseQuery.OrderBy(f => f.Name) : baseQuery.OrderByDescending(f => f.Name),
            "pi" => sortAsc ? baseQuery.OrderBy(f => f.Pi!.Name) : baseQuery.OrderByDescending(f => f.Pi!.Name),
            "businessoutcome" => sortAsc ? baseQuery.OrderBy(f => f.BusinessOutcome!.Summary) : baseQuery.OrderByDescending(f => f.BusinessOutcome!.Summary),
            "unfunded" => sortAsc ? baseQuery.OrderBy(f => f.UnfundedOption!.Name) : baseQuery.OrderByDescending(f => f.UnfundedOption!.Name),
            _ => sortAsc
                ? baseQuery.OrderBy(f => f.Ranking).ThenBy(f => f.Name)
                : baseQuery.OrderByDescending(f => f.Ranking).ThenByDescending(f => f.Name),
        };

        var projected = await sorted
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(f => new
            {
                f.Id,
                f.JiraId,
                f.Name,
                f.Ranking,
                PiName = f.Pi != null ? f.Pi.Name : null,
                BusinessOutcomeName = f.BusinessOutcome != null ? f.BusinessOutcome.Summary : null,
                UnfundedOptionName = f.UnfundedOption != null ? f.UnfundedOption.Name : null,
                TeamNames = f.FeatureTeams.Select(ft => ft.Team.Name).ToList()
            })
            .ToListAsync(ct);

        var items = projected.Select(f => new FeatureListItem(
            f.Id,
            f.JiraId,
            f.Name,
            f.Ranking,
            f.PiName,
            f.BusinessOutcomeName,
            f.UnfundedOptionName,
            f.TeamNames))
            .ToList();

        return new FeatureListPagedResult(items, totalCount);
    }

    public async Task<Feature?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features
            .Include(f => f.Pi)
            .Include(f => f.BusinessOutcome)
            .Include(f => f.FeatureTeams).ThenInclude(ft => ft.Team)
            .Include(f => f.FeatureTechnologyStacks).ThenInclude(fts => fts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking().FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<Feature> CreateAsync(Feature feature)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.Features.Add(feature); await db.SaveChangesAsync(); return feature;
    }

    public async Task<Feature> UpdateAsync(Feature feature)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.Features
            .FirstOrDefaultAsync(f => f.Id == feature.Id);

        if (existing is null)
        {
            Log.Warning("Feature {FeatureId} not found", feature.Id);
            throw new KeyNotFoundException($"Feature {feature.Id} not found.");
        }

        existing.JiraId = feature.JiraId;
        existing.ProjectKey = feature.ProjectKey;
        existing.IssueType = feature.IssueType;
        existing.Summary = feature.Summary;
        existing.Name = feature.Name;
        existing.Description = feature.Description;
        existing.Labels = feature.Labels;
        existing.Comments = feature.Comments;
        existing.Ranking = feature.Ranking;
        existing.UnfundedOptionId = feature.UnfundedOptionId;
        existing.BusinessOutcomeId = feature.BusinessOutcomeId;
        existing.PiId = feature.PiId;
        existing.DateExpected = feature.DateExpected;
        existing.IsLinkedToTheJira = feature.IsLinkedToTheJira;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var f = await db.Features.FindAsync(id);
        if (f is null) return false;
        db.Features.Remove(f); await db.SaveChangesAsync(); return true;
    }

    public async Task AddTeamAsync(int featureId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        if (!await db.FeatureTeams.AnyAsync(ft => ft.FeatureId == featureId && ft.TeamId == teamId))
        {
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = featureId, TeamId = teamId });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveTeamAsync(int featureId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.FeatureTeams.FirstOrDefaultAsync(
            ft => ft.FeatureId == featureId && ft.TeamId == teamId);
        if (e is not null) { db.FeatureTeams.Remove(e); await db.SaveChangesAsync(); }
    }

    public async Task<FeatureTechnologyStack> AddFeatureTechnologyStackAsync(int featureId, int technologyStackId, int? estimatedEffort)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var fts = new FeatureTechnologyStack
        {
            FeatureId = featureId,
            TechnologyStackId = technologyStackId,
            EstimatedEffort = estimatedEffort
        };
        db.FeatureTechnologyStacks.Add(fts);
        await db.SaveChangesAsync();
        return fts;
    }

    public async Task SetFeatureTechnologyStackEffortAsync(int featureTechnologyStackId, int? estimatedEffort)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.FeatureTechnologyStacks.FindAsync(featureTechnologyStackId);
        if (e is not null) { e.EstimatedEffort = estimatedEffort; await db.SaveChangesAsync(); }
    }

    public async Task RemoveFeatureTechnologyStackAsync(int featureTechnologyStackId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.FeatureTechnologyStacks.FindAsync(featureTechnologyStackId);
        if (e is not null) { db.FeatureTechnologyStacks.Remove(e); await db.SaveChangesAsync(); }
    }

    public async Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraFeatureSyncItem> items)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var jiraKeys = items.Select(i => i.JiraKey).ToList();
        var existing = await db.Features
            .Where(f => f.JiraId != null && jiraKeys.Contains(f.JiraId))
            .ToListAsync();
        var existingByJiraId = existing.ToDictionary(f => f.JiraId!);

        // Load BusinessOutcomes by JiraId for parent linking
        var parentLinks = items.Where(i => !string.IsNullOrEmpty(i.ParentLink)).Select(i => i.ParentLink!).Distinct().ToList();
        var boByJiraId = parentLinks.Count > 0
            ? await db.BusinessOutcomes
                .Where(bo => bo.JiraId != null && parentLinks.Contains(bo.JiraId))
                .ToDictionaryAsync(bo => bo.JiraId!)
            : new Dictionary<string, BusinessOutcome>();

        int created = 0, updated = 0, linked = 0;

        foreach (var item in items)
        {
            if (existingByJiraId.TryGetValue(item.JiraKey, out var existingF))
            {
                existingF.Summary = item.Summary ?? existingF.Summary;
                existingF.Description = item.Description;
                existingF.Labels = item.Labels;
                if (!string.IsNullOrWhiteSpace(item.FeatureName))
                    existingF.Name = item.FeatureName;
                updated++;

                if (!string.IsNullOrEmpty(item.ParentLink) && boByJiraId.TryGetValue(item.ParentLink, out var parentBo)
                    && existingF.BusinessOutcomeId != parentBo.Id)
                {
                    existingF.BusinessOutcomeId = parentBo.Id;
                    linked++;
                }
            }
            else
            {
                var f = new Feature
                {
                    JiraId = item.JiraKey,
                    ProjectKey = projectKey,
                    IssueType = item.IssueType,
                    Summary = item.Summary ?? item.JiraKey,
                    Description = item.Description,
                    Labels = item.Labels,
                    Name = item.FeatureName,
                    IsLinkedToTheJira = true,
                };

                if (!string.IsNullOrEmpty(item.ParentLink) && boByJiraId.TryGetValue(item.ParentLink, out var parentBo))
                {
                    f.BusinessOutcomeId = parentBo.Id;
                    linked++;
                }

                db.Features.Add(f);
                created++;
            }
        }

        await db.SaveChangesAsync();

        Log.Information("Jira Feature sync: created={Created}, updated={Updated}, linked={Linked}",
            created, updated, linked);

        return new JiraSyncResult(created, updated, linked);
    }
}
