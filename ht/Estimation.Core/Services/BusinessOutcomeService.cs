using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public record BusinessOutcomeListItem(
    int Id,
    string? JiraId,
    string? Name,
    string? EpicName,
    int FeatureCount);

public record BoListPagedResult(List<BusinessOutcomeListItem> Items, int TotalCount);

public interface IBusinessOutcomeService
{
    Task<List<BusinessOutcome>> GetAllAsync();
    Task<List<BusinessOutcome>> GetAllWithHierarchyAsync();
    Task<BoListPagedResult> GetPagedListAsync(int page, int pageSize, string? search, string? sortField, bool sortAsc, CancellationToken ct = default);
    Task<BusinessOutcome?> GetByIdAsync(int id);
    Task<BusinessOutcome> CreateAsync(BusinessOutcome bo);
    Task<BusinessOutcome> UpdateAsync(BusinessOutcome bo);
    Task<bool> DeleteAsync(int id);
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
            .Include(bo => bo.Features)
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

    public async Task<BoListPagedResult> GetPagedListAsync(int page, int pageSize, string? search, string? sortField, bool sortAsc, CancellationToken ct = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(ct);

        var baseQuery = db.BusinessOutcomes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQuery = baseQuery.Where(bo =>
                (bo.Summary != null && bo.Summary.Contains(term)) ||
                (bo.JiraId != null && bo.JiraId.Contains(term)));
        }

        var totalCount = await baseQuery.CountAsync(ct);

        var sorted = sortField?.ToLowerInvariant() switch
        {
            "jiraid" => sortAsc ? baseQuery.OrderBy(bo => bo.JiraId) : baseQuery.OrderByDescending(bo => bo.JiraId),
            "epic" => sortAsc ? baseQuery.OrderBy(bo => bo.PortfolioEpic!.Summary) : baseQuery.OrderByDescending(bo => bo.PortfolioEpic!.Summary),
            "features" => sortAsc ? baseQuery.OrderBy(bo => bo.Features.Count) : baseQuery.OrderByDescending(bo => bo.Features.Count),
            _ => sortAsc ? baseQuery.OrderBy(bo => bo.Summary) : baseQuery.OrderByDescending(bo => bo.Summary),
        };

        var items = await sorted
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(bo => new BusinessOutcomeListItem(
                bo.Id,
                bo.JiraId,
                bo.Summary,
                bo.PortfolioEpic != null ? bo.PortfolioEpic.Summary : null,
                bo.Features.Count))
            .ToListAsync(ct);

        return new BoListPagedResult(items, totalCount);
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
        db.BusinessOutcomes.Add(bo); await db.SaveChangesAsync(); return bo;
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
        if (bo is null) return false;
        db.BusinessOutcomes.Remove(bo); await db.SaveChangesAsync(); return true;
    }
}
