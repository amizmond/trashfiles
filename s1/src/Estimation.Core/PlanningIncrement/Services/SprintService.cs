using Estimation.Core.PlanningIncrement.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.PlanningIncrement.Services;

public interface ISprintService
{
    Task<List<Sprint>> GetForTeamAsync(int teamId);
    Task<List<Sprint>> GetForTeamInRangeAsync(int teamId, DateTime from, DateTime to);
    Task<Sprint?> GetByIdAsync(int id);
    Task<Sprint> CreateAsync(Sprint entity);
    Task<Sprint> UpdateAsync(Sprint entity);
    Task<bool> DeleteAsync(int id);
}

public class SprintService : ISprintService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public SprintService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<Sprint>> GetForTeamAsync(int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Sprints
            .Include(s => s.Pi)
            .Where(s => s.TeamId == teamId)
            .AsNoTracking()
            .OrderByDescending(s => s.StartDate)
            .ToListAsync();
    }

    public async Task<List<Sprint>> GetForTeamInRangeAsync(int teamId, DateTime from, DateTime to)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Sprints
            .Include(s => s.Pi)
            .Where(s => s.TeamId == teamId && s.StartDate <= to && s.EndDate >= from)
            .AsNoTracking()
            .OrderBy(s => s.StartDate)
            .ToListAsync();
    }

    public async Task<Sprint?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Sprints
            .Include(s => s.Pi)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Sprint> CreateAsync(Sprint entity)
    {
        Validate(entity);
        await using var db = await _ctx.CreateDbContextAsync();
        await EnsureNoOverlapAsync(db, entity);
        db.Sprints.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<Sprint> UpdateAsync(Sprint entity)
    {
        Validate(entity);
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.Sprints.FirstOrDefaultAsync(s => s.Id == entity.Id);
        if (existing is null)
        {
            Log.Warning("Sprint {Id} not found", entity.Id);
            throw new KeyNotFoundException($"Sprint {entity.Id} not found.");
        }
        existing.Name = entity.Name;
        existing.ColorHex = entity.ColorHex;
        existing.Comment = entity.Comment;
        existing.IsIpSprint = entity.IsIpSprint;

        if (existing.SourceArtSprintId is null)
        {
            await EnsureNoOverlapAsync(db, entity);
            existing.PiId = entity.PiId;
            existing.StartDate = entity.StartDate.Date;
            existing.EndDate = entity.EndDate.Date;
        }

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var entity = await db.Sprints.FindAsync(id);
        if (entity is null)
        {
            return false;
        }
        db.Sprints.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    private static void Validate(Sprint entity)
    {
        entity.StartDate = entity.StartDate.Date;
        entity.EndDate = entity.EndDate.Date;
        if (entity.EndDate < entity.StartDate)
        {
            throw new InvalidOperationException("End date must be on or after start date.");
        }
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            throw new InvalidOperationException("Sprint name is required.");
        }
    }

    private static async Task EnsureNoOverlapAsync(EstimationDbContext db, Sprint entity)
    {
        var overlap = await db.Sprints
            .Where(s => s.Id != entity.Id
                && s.TeamId == entity.TeamId
                && s.StartDate <= entity.EndDate
                && s.EndDate >= entity.StartDate)
            .AnyAsync();
        if (overlap)
        {
            throw new InvalidOperationException("This team already has a sprint overlapping the selected dates.");
        }
    }
}
