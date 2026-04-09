using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public interface IStrategicObjectiveService
{
    Task<List<StrategicObjective>> GetAllAsync();
    Task<List<StrategicObjective>> GetAllWithHierarchyAsync();
    Task<StrategicObjective?> GetByIdAsync(int id);
    Task<StrategicObjective> CreateAsync(StrategicObjective program);
    Task<StrategicObjective> UpdateAsync(StrategicObjective program);
    Task<bool> DeleteAsync(int id);
    Task AddEpicAsync(int programId, int epicId);
    Task RemoveEpicAsync(int programId, int epicId);
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
            .AsNoTracking().OrderBy(pp => pp.Name).ToListAsync();
    }

    public async Task<List<StrategicObjective>> GetAllWithHierarchyAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.StrategicObjectives
            .Include(pp => pp.StrategicObjectivePortfolioEpics)
                .ThenInclude(ppe => ppe.PortfolioEpic)
            .Include(pp => pp.CapitalProjectStrategicObjectives)
                .ThenInclude(cpp => cpp.CapitalProject)
            .AsSplitQuery()
            .AsNoTracking().OrderBy(pp => pp.Name).ToListAsync();
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
        db.StrategicObjectives.Add(program); await db.SaveChangesAsync(); return program;
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

        existing.Name = program.Name;
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
        if (pp is null) return false;

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
        if (e is not null) { db.StrategicObjectivePortfolioEpics.Remove(e); await db.SaveChangesAsync(); }
    }
}
