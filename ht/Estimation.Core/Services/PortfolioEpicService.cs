using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public record JiraEpicSyncItem(string JiraKey, string? Name, string? Description, string? IssueType, string? Labels, string? ParentLink);

public interface IPortfolioEpicService
{
    Task<List<PortfolioEpic>> GetAllAsync();
    Task<List<PortfolioEpic>> GetAllWithHierarchyAsync();
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

    public async Task<List<PortfolioEpic>> GetAllWithHierarchyAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.PortfolioEpics
            .Include(pe => pe.BusinessOutcomes)
            .Include(pe => pe.StrategicObjectivePortfolioEpics)
                .ThenInclude(ppe => ppe.StrategicObjective)
                    .ThenInclude(pp => pp.CapitalProjectStrategicObjectives)
                        .ThenInclude(cpp => cpp.CapitalProject)
            .AsSplitQuery()
            .AsNoTracking().OrderBy(pe => pe.Summary).ToListAsync();
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
        db.PortfolioEpics.Add(epic); await db.SaveChangesAsync(); return epic;
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

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var pe = await db.PortfolioEpics.FindAsync(id);
        if (pe is null) return false;

        db.StrategicObjectivePortfolioEpics.RemoveRange(
            db.StrategicObjectivePortfolioEpics.Where(ppe => ppe.PortfolioEpicId == id));

        db.PortfolioEpics.Remove(pe);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraEpicSyncItem> items)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        // Load existing PortfolioEpics by JiraId
        var jiraKeys = items.Select(i => i.JiraKey).ToList();
        var existing = await db.PortfolioEpics
            .Where(pe => pe.JiraId != null && jiraKeys.Contains(pe.JiraId))
            .ToListAsync();
        var existingByJiraId = existing.ToDictionary(pe => pe.JiraId!);

        // Load StrategicObjectives by JiraId for parent linking
        var parentLinks = items.Where(i => !string.IsNullOrEmpty(i.ParentLink)).Select(i => i.ParentLink!).Distinct().ToList();
        var soByJiraId = parentLinks.Count > 0
            ? await db.StrategicObjectives
                .Where(so => so.JiraId != null && parentLinks.Contains(so.JiraId))
                .ToDictionaryAsync(so => so.JiraId!)
            : new Dictionary<string, StrategicObjective>();

        // Load existing links
        var existingLinks = (await db.StrategicObjectivePortfolioEpics.ToListAsync())
            .Where(l => true)
            .ToList();

        int created = 0, updated = 0, linked = 0;

        foreach (var item in items)
        {
            PortfolioEpic pe;
            if (existingByJiraId.TryGetValue(item.JiraKey, out var existingPe))
            {
                existingPe.Summary = item.Name ?? existingPe.Summary;
                existingPe.Description = item.Description;
                existingPe.Labels = item.Labels;
                pe = existingPe;
                updated++;
            }
            else
            {
                pe = new PortfolioEpic
                {
                    JiraId = item.JiraKey,
                    ProjectKey = projectKey,
                    IssueType = item.IssueType,
                    Summary = item.Name ?? item.JiraKey,
                    Description = item.Description,
                    Labels = item.Labels,
                };
                db.PortfolioEpics.Add(pe);
                created++;
            }

            await db.SaveChangesAsync();

            // Link to parent StrategicObjective via ParentLink
            if (!string.IsNullOrEmpty(item.ParentLink) && soByJiraId.TryGetValue(item.ParentLink, out var parentSo))
            {
                var alreadyLinked = existingLinks.Any(l => l.StrategicObjectiveId == parentSo.Id && l.PortfolioEpicId == pe.Id);
                if (!alreadyLinked)
                {
                    var link = new StrategicObjectivePortfolioEpic
                    {
                        StrategicObjectiveId = parentSo.Id,
                        PortfolioEpicId = pe.Id,
                    };
                    db.StrategicObjectivePortfolioEpics.Add(link);
                    existingLinks.Add(link);
                    linked++;
                }
            }
        }

        await db.SaveChangesAsync();

        Log.Information("Jira PortfolioEpic sync: created={Created}, updated={Updated}, linked={Linked}",
            created, updated, linked);

        return new JiraSyncResult(created, updated, linked);
    }
}
