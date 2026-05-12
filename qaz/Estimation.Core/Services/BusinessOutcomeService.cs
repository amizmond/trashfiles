using Estimation.Core.JiraLogic;
using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public interface IBusinessOutcomeService
{
    Task<List<BusinessOutcome>> GetAllAsync();
    Task<List<BusinessOutcome>> GetAllWithHierarchyAsync();
    Task<BusinessOutcome?> GetByIdAsync(int id);
    Task<BusinessOutcome> CreateAsync(BusinessOutcome bo);
    Task<BusinessOutcome> UpdateAsync(BusinessOutcome bo);
    Task<bool> DeleteAsync(int id);
    Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraEpicSyncItem> items);
}

public class BusinessOutcomeService : IBusinessOutcomeService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public BusinessOutcomeService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<BusinessOutcome>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.BusinessOutcomes
            .Include(bo => bo.PortfolioEpic)
            .Include(bo => bo.Features)
            .AsSplitQuery()
            .AsNoTracking().OrderBy(bo => bo.Summary).ToListAsync();
    }

    public async Task<List<BusinessOutcome>> GetAllWithHierarchyAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.BusinessOutcomes
            .Include(bo => bo.PortfolioEpic)
                .ThenInclude(pe => pe!.StrategicObjectivePortfolioEpics)
                    .ThenInclude(ppe => ppe.StrategicObjective)
                        .ThenInclude(pp => pp.CapitalProjectStrategicObjectives)
                            .ThenInclude(cpp => cpp.CapitalProject)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(bo => bo.Ranking).ThenBy(bo => bo.Summary)
            .ToListAsync();
    }

    public async Task<BusinessOutcome?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.BusinessOutcomes
            .Include(bo => bo.PortfolioEpic)
            .Include(bo => bo.Features)
            .AsSplitQuery()
            .AsNoTracking().FirstOrDefaultAsync(bo => bo.Id == id);
    }

    public async Task<BusinessOutcome> CreateAsync(BusinessOutcome bo)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.BusinessOutcomes.Add(bo);
        await db.SaveChangesAsync();
        return bo;
    }

    public async Task<BusinessOutcome> UpdateAsync(BusinessOutcome bo)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.BusinessOutcomes
            .FirstOrDefaultAsync(b => b.Id == bo.Id);

        if (existing is null)
        {
            Log.Warning("BusinessOutcome {BoId} not found", bo.Id);
            throw new KeyNotFoundException($"BusinessOutcome {bo.Id} not found.");
        }

        existing.JiraId = bo.JiraId;
        existing.Summary = bo.Summary;
        existing.Description = bo.Description;
        existing.Comments = bo.Comments;
        existing.Ranking = bo.Ranking;
        existing.ArtName = bo.ArtName;
        existing.PortfolioEpicId = bo.PortfolioEpicId;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var bo = await db.BusinessOutcomes.FindAsync(id);
        if (bo is null)
        {
            return false;
        }
        db.BusinessOutcomes.Remove(bo);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<JiraSyncResult> SyncFromJiraAsync(string projectKey, List<JiraEpicSyncItem> items)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var jiraKeys = items.Select(i => i.JiraKey).ToList();
        var existing = await db.BusinessOutcomes
            .Where(bo => bo.JiraId != null && jiraKeys.Contains(bo.JiraId))
            .ToListAsync();
        var existingByJiraId = existing.ToDictionary(bo => bo.JiraId!);

        // Load PortfolioEpics by JiraId for parent linking
        var parentLinks = items.Where(i => !string.IsNullOrEmpty(i.ParentLink)).Select(i => i.ParentLink!).Distinct().ToList();
        var peByJiraId = parentLinks.Count > 0
            ? await db.PortfolioEpics
                .Where(pe => pe.JiraId != null && parentLinks.Contains(pe.JiraId))
                .ToDictionaryAsync(pe => pe.JiraId!)
            : new Dictionary<string, PortfolioEpic>();

        int created = 0, updated = 0, linked = 0;

        foreach (var item in items)
        {
            if (existingByJiraId.TryGetValue(item.JiraKey, out var existingBo))
            {
                var mask = item.PropertyMask;
                if (ShouldWrite(mask, JiraSyncProperties.Summary))
                {
                    existingBo.Summary = item.Name ?? existingBo.Summary;
                }
                if (ShouldWrite(mask, JiraSyncProperties.Description))
                {
                    existingBo.Description = item.Description;
                }
                if (ShouldWrite(mask, JiraSyncProperties.Labels))
                {
                    existingBo.Labels = item.Labels;
                }
                if (ShouldWrite(mask, JiraSyncProperties.Status))
                {
                    existingBo.Status = item.Status;
                }
                existingBo.JiraUpdated = item.JiraUpdated;
                if (ShouldWrite(mask, JiraSyncProperties.TargetStart))
                {
                    existingBo.TargetStart = item.TargetStart;
                }
                if (ShouldWrite(mask, JiraSyncProperties.TargetEnd))
                {
                    existingBo.TargetEnd = item.TargetEnd;
                }
                if (ShouldWrite(mask, JiraSyncProperties.StoryPoints))
                {
                    existingBo.StoryPoints = item.StoryPoints;
                }
                updated++;

                if (!string.IsNullOrEmpty(item.ParentLink) && peByJiraId.TryGetValue(item.ParentLink, out var parentPe)
                    && existingBo.PortfolioEpicId != parentPe.Id)
                {
                    existingBo.PortfolioEpicId = parentPe.Id;
                    linked++;
                }
            }
            else
            {
                var bo = new BusinessOutcome
                {
                    JiraId = item.JiraKey,
                    ProjectKey = projectKey,
                    IssueType = item.IssueType,
                    Summary = item.Name ?? item.JiraKey,
                    Description = item.Description,
                    Labels = item.Labels,
                    Status = item.Status,
                    JiraUpdated = item.JiraUpdated,
                    TargetStart = item.TargetStart,
                    TargetEnd = item.TargetEnd,
                    StoryPoints = item.StoryPoints,
                };

                if (!string.IsNullOrEmpty(item.ParentLink) && peByJiraId.TryGetValue(item.ParentLink, out var parentPe))
                {
                    bo.PortfolioEpicId = parentPe.Id;
                    linked++;
                }

                db.BusinessOutcomes.Add(bo);
                created++;
            }
        }

        await db.SaveChangesAsync();

        Log.Information("Jira BusinessOutcome sync: created={Created}, updated={Updated}, linked={Linked}",
            created, updated, linked);

        return new JiraSyncResult(created, updated, linked);
    }

    private static bool ShouldWrite(HashSet<string>? mask, string property)
    {
        return mask is null || mask.Contains(property);
    }
}
