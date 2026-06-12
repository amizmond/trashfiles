using Estimation.Core.JiraLogic;
using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public record JiraEpicSyncItem(string JiraKey, string? Summary, string? Description, string? AcceptanceCriteria, string? NavigatorId, string? IssueType, string? Labels, string? ParentLink, string? Status, DateTime? JiraUpdated, DateTime? TargetStart, DateTime? TargetEnd, int? StoryPoints)
    : IJiraScalarSyncItem
{
    /// <summary>
    /// Optional opt-in mask for partial updates. When null, every property is written
    /// (current behavior). When set, only properties whose <see cref="Estimation.Core.JiraLogic.JiraSyncProperties"/>
    /// key is present in the set are written to existing records.
    /// </summary>
    public HashSet<string>? PropertyMask { get; init; }
}

public interface IPortfolioEpicService
{
    Task<List<PortfolioEpic>> GetAllAsync();
    Task<List<PortfolioEpic>> GetAllLightAsync();
    Task<List<PortfolioEpic>> GetAllWithHierarchyAsync();
    Task<HashSet<string>> GetExistingJiraIdsAsync();
    Task<PortfolioEpic?> GetByIdAsync(int id);
    Task<PortfolioEpic> CreateAsync(PortfolioEpic epic);
    Task<PortfolioEpic> UpdateAsync(PortfolioEpic epic);
    Task<bool> DeleteAsync(int id);
    Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraEpicSyncItem> items);
}

public class PortfolioEpicService : IPortfolioEpicService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public PortfolioEpicService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<PortfolioEpic>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.PortfolioEpics
            .Include(pe => pe.BusinessOutcomes)
            .AsNoTracking().OrderBy(pe => pe.Summary).ToListAsync();
    }

    public async Task<List<PortfolioEpic>> GetAllLightAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.PortfolioEpics
            .AsNoTracking().OrderBy(pe => pe.Id).ToListAsync();
    }
    public async Task<List<PortfolioEpic>> GetAllWithHierarchyAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.PortfolioEpics
            .Include(pe => pe.StrategicObjectivePortfolioEpics)
                .ThenInclude(ppe => ppe.StrategicObjective)
                    .ThenInclude(pp => pp.CapitalProjectStrategicObjectives)
                        .ThenInclude(cpp => cpp.CapitalProject)
            .Include(pe => pe.BusinessOutcomes)
                .ThenInclude(bo => bo.Features)
                    .ThenInclude(f => f.Pi)
            .AsSplitQuery()
            .AsNoTracking().OrderBy(pe => pe.Summary).ToListAsync();
    }

    public async Task<HashSet<string>> GetExistingJiraIdsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var ids = await db.PortfolioEpics.AsNoTracking()
            .Where(pe => pe.JiraId != null && pe.JiraId != "")
            .Select(pe => pe.JiraId!)
            .ToListAsync();
        return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PortfolioEpic?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.PortfolioEpics
            .Include(pe => pe.BusinessOutcomes)
            .AsNoTracking().FirstOrDefaultAsync(pe => pe.Id == id);
    }

    public async Task<PortfolioEpic> CreateAsync(PortfolioEpic epic)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.PortfolioEpics.Add(epic);
        await db.SaveChangesAsync();
        return epic;
    }

    public async Task<PortfolioEpic> UpdateAsync(PortfolioEpic epic)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.PortfolioEpics
            .Include(pe => pe.BusinessOutcomes)
            .FirstOrDefaultAsync(pe => pe.Id == epic.Id);

        if (existing is null)
        {
            Log.Warning("PortfolioEpic {EpicId} not found", epic.Id);
            throw new KeyNotFoundException($"PortfolioEpic {epic.Id} not found.");
        }

        existing.JiraId = epic.JiraId;
        existing.Summary = epic.Summary;
        existing.Description = epic.Description;
        existing.Comments = epic.Comments;
        existing.Ranking = epic.Ranking;

        await db.SaveChangesAsync();
        return existing;
    }

   
    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var pe = await db.PortfolioEpics.FindAsync(id);
        if (pe is null)
        {
            return false;
        }

        db.StrategicObjectivePortfolioEpics.RemoveRange(
            db.StrategicObjectivePortfolioEpics.Where(ppe => ppe.PortfolioEpicId == id));

        db.PortfolioEpics.Remove(pe);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraEpicSyncItem> items)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var jiraKeys = items.Select(i => i.JiraKey).ToList();
        var existing = await db.PortfolioEpics
            .Where(pe => pe.JiraId != null && jiraKeys.Contains(pe.JiraId))
            .ToListAsync();
        var existingByJiraId = existing.ToDictionary(pe => pe.JiraId!);

        var parentLinks = items.Where(i => !string.IsNullOrEmpty(i.ParentLink)).Select(i => i.ParentLink!).Distinct().ToList();
        var soByJiraId = parentLinks.Count > 0
            ? await db.StrategicObjectives
                .Where(so => so.JiraId != null && parentLinks.Contains(so.JiraId))
                .ToDictionaryAsync(so => so.JiraId!)
            : new Dictionary<string, StrategicObjective>();

        var existingLinks = await db.StrategicObjectivePortfolioEpics.ToListAsync();
        var existingLinkSet = new HashSet<(int SoId, int PeId)>(
            existingLinks.Select(l => (l.StrategicObjectiveId, l.PortfolioEpicId)));

        int created = 0, updated = 0, linked = 0;

        // First pass: upsert PortfolioEpic entities so each gets an Id.
        var perItem = new List<(JiraEpicSyncItem Item, PortfolioEpic Entity)>(items.Count);
        foreach (var item in items)
        {
            PortfolioEpic pe;
            if (existingByJiraId.TryGetValue(item.JiraKey, out var existingPe))
            {
                JiraScalarApply.ApplyToExisting(existingPe, item);
                pe = existingPe;
                updated++;
            }
            else
            {
                pe = new PortfolioEpic();
                JiraScalarApply.ApplyToNew(pe, item, projectKey);
                db.PortfolioEpics.Add(pe);
                created++;
            }
            perItem.Add((item, pe));
        }

        await db.SaveChangesAsync();

        // Second pass: relink parents now that all PEs have stable Ids.
        foreach (var (item, pe) in perItem)
        {
            if (string.IsNullOrEmpty(item.ParentLink)
                || !soByJiraId.TryGetValue(item.ParentLink, out var parentSo))
            {
                continue;
            }
            var key = (parentSo.Id, pe.Id);
            if (existingLinkSet.Add(key))
            {
                db.StrategicObjectivePortfolioEpics.Add(new StrategicObjectivePortfolioEpic
                {
                    StrategicObjectiveId = parentSo.Id,
                    PortfolioEpicId = pe.Id,
                });
                linked++;
            }
        }

        if (linked > 0)
        {
            await db.SaveChangesAsync();
        }

        Log.Information("Jira PortfolioEpic sync: created={Created}, updated={Updated}, linked={Linked}",
            created, updated, linked);

        return new JiraSyncResult(created, updated, linked);
    }
}
