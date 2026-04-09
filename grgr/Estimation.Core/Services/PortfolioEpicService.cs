using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public interface IPortfolioEpicService
{
    Task<List<PortfolioEpic>> GetAllAsync();
    Task<List<PortfolioEpic>> GetAllWithHierarchyAsync();
    Task<PortfolioEpic?> GetByIdAsync(int id);
    Task<PortfolioEpic> CreateAsync(PortfolioEpic epic);
    Task<PortfolioEpic> UpdateAsync(PortfolioEpic epic);
    Task<bool> DeleteAsync(int id);
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
            .AsNoTracking().OrderBy(pe => pe.Name).ToListAsync();
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
            .AsNoTracking().OrderBy(pe => pe.Name).ToListAsync();
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
        existing.Name = epic.Name;
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
}
