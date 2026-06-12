using Estimation.Core.JiraLogic;
using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public record JiraSyncItem(string JiraKey, string? Summary, string? Description, string? AcceptanceCriteria, string? NavigatorId, string? IssueType, string? Labels, string? Status, DateTime? JiraUpdated, DateTime? TargetStart, DateTime? TargetEnd, int? StoryPoints)
    : IJiraScalarSyncItem
{
    /// <summary>
    /// Optional opt-in mask for partial updates. When null, every property is written
    /// (current behavior). When set, only properties whose <see cref="Estimation.Core.JiraLogic.JiraSyncProperties"/>
    /// key is present in the set are written to existing records.
    /// </summary>
    public HashSet<string>? PropertyMask { get; init; }
}

public record JiraSyncResult(int Created, int Updated, int Linked);

public interface IStrategicObjectiveService
{
    Task<List<StrategicObjective>> GetAllAsync();
    Task<List<StrategicObjective>> GetAllLightAsync();
    Task<List<StrategicObjective>> GetAllWithHierarchyAsync();
    Task<HashSet<string>> GetExistingJiraIdsAsync();
    Task<StrategicObjective?> GetByIdAsync(int id);
    Task<StrategicObjective> CreateAsync(StrategicObjective program);
    Task<StrategicObjective> UpdateAsync(StrategicObjective program);
    Task<bool> DeleteAsync(int id);
    Task AddEpicAsync(int programId, int epicId);
    Task RemoveEpicAsync(int programId, int epicId);
    Task<JiraSyncResult> SyncFromJiraAsync(int capitalProjectId, string projectKey, List<JiraSyncItem> items);
}

public class StrategicObjectiveService : IStrategicObjectiveService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public StrategicObjectiveService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<StrategicObjective>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.StrategicObjectives
            .Include(pp => pp.StrategicObjectivePortfolioEpics).ThenInclude(ppe => ppe.PortfolioEpic)
            .AsNoTracking().OrderBy(pp => pp.Summary).ToListAsync();
    }
    public async Task<List<StrategicObjective>> GetAllLightAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.StrategicObjectives
            .AsNoTracking().OrderBy(pp => pp.Id).ToListAsync();
    }

    public async Task<List<StrategicObjective>> GetAllWithHierarchyAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.StrategicObjectives
            .Include(pp => pp.StrategicObjectivePortfolioEpics)
                .ThenInclude(ppe => ppe.PortfolioEpic)
                    .ThenInclude(pe => pe.BusinessOutcomes)
                        .ThenInclude(bo => bo.Features)
                            .ThenInclude(f => f.Pi)
            .Include(pp => pp.CapitalProjectStrategicObjectives)
                .ThenInclude(cpp => cpp.CapitalProject)
            .AsSplitQuery()
            .AsNoTracking().OrderBy(pp => pp.Summary).ToListAsync();
    }

    public async Task<HashSet<string>> GetExistingJiraIdsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var ids = await db.StrategicObjectives.AsNoTracking()
            .Where(so => so.JiraId != null && so.JiraId != "")
            .Select(so => so.JiraId!)
            .ToListAsync();
        return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<StrategicObjective?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.StrategicObjectives
            .Include(pp => pp.StrategicObjectivePortfolioEpics).ThenInclude(ppe => ppe.PortfolioEpic)
            .AsNoTracking().FirstOrDefaultAsync(pp => pp.Id == id);
    }

    public async Task<StrategicObjective> CreateAsync(StrategicObjective program)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.StrategicObjectives.Add(program);
        await db.SaveChangesAsync();
        return program;
    }

    public async Task<StrategicObjective> UpdateAsync(StrategicObjective program)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.StrategicObjectives
            .Include(pp => pp.StrategicObjectivePortfolioEpics)
                .ThenInclude(ppe => ppe.PortfolioEpic)
            .FirstOrDefaultAsync(pp => pp.Id == program.Id);

        if (existing is null)
        {
            Log.Warning("StrategicObjective {ProgramId} not found", program.Id);
            throw new KeyNotFoundException($"StrategicObjective {program.Id} not found.");
        }

        existing.Summary = program.Summary;
        existing.JiraId = program.JiraId;
        existing.Description = program.Description;
        existing.Comments = program.Comments;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var pp = await db.StrategicObjectives.FindAsync(id);
        if (pp is null)
        {
            return false;
        }

        db.CapitalProjectStrategicObjectives.RemoveRange(
            db.CapitalProjectStrategicObjectives.Where(cpp => cpp.StrategicObjectiveId == id));

        db.StrategicObjectives.Remove(pp);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task AddEpicAsync(int programId, int epicId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        if (!await db.StrategicObjectivePortfolioEpics.AnyAsync(
                x => x.StrategicObjectiveId == programId && x.PortfolioEpicId == epicId))
        {
            db.StrategicObjectivePortfolioEpics.Add(
                new StrategicObjectivePortfolioEpic { StrategicObjectiveId = programId, PortfolioEpicId = epicId });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveEpicAsync(int programId, int epicId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.StrategicObjectivePortfolioEpics.FirstOrDefaultAsync(
            x => x.StrategicObjectiveId == programId && x.PortfolioEpicId == epicId);
        if (e is not null)
        { db.StrategicObjectivePortfolioEpics.Remove(e); await db.SaveChangesAsync(); }
    }

    public async Task<JiraSyncResult> SyncFromJiraAsync(int capitalProjectId, string projectKey, List<JiraSyncItem> items)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var jiraKeys = items.Select(i => i.JiraKey).ToList();
        var existing = await db.StrategicObjectives
            .Where(so => so.JiraId != null && jiraKeys.Contains(so.JiraId))
            .ToListAsync();
        var existingByJiraId = existing.ToDictionary(so => so.JiraId!);

        var existingLinks = (await db.CapitalProjectStrategicObjectives
            .Where(l => l.CapitalProjectId == capitalProjectId)
            .Select(l => l.StrategicObjectiveId)
            .ToListAsync()).ToHashSet();

        int created = 0, updated = 0, linked = 0;

        // First pass: upsert StrategicObjective entities so each gets an Id.
        var perItem = new List<StrategicObjective>(items.Count);
        foreach (var item in items)
        {
            if (existingByJiraId.TryGetValue(item.JiraKey, out var so))
            {
                JiraScalarApply.ApplyToExisting(so, item);
                updated++;
            }
            else
            {
                so = new StrategicObjective();
                JiraScalarApply.ApplyToNew(so, item, projectKey);
                db.StrategicObjectives.Add(so);
                created++;
            }
            perItem.Add(so);
        }

        await db.SaveChangesAsync();

        // Second pass: link to CapitalProject now that all SOs have stable Ids.
        foreach (var so in perItem)
        {
            if (existingLinks.Add(so.Id))
            {
                db.CapitalProjectStrategicObjectives.Add(new CapitalProjectStrategicObjective
                {
                    CapitalProjectId = capitalProjectId,
                    StrategicObjectiveId = so.Id,
                });
                linked++;
            }
        }

        if (linked > 0)
        {
            await db.SaveChangesAsync();
        }

        Log.Information("Jira sync for CapitalProject {Id}: created={Created}, updated={Updated}, linked={Linked}",
            capitalProjectId, created, updated, linked);

        return new JiraSyncResult(created, updated, linked);
    }
}
