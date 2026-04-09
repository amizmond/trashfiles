using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public interface IPiService
{
    Task<List<Pi>> GetAllAsync();
    Task<List<Pi>> GetAllLightAsync();
    Task<Pi?> GetByIdAsync(int id);
    Task<Pi> CreateAsync(Pi pi);
    Task<Pi> UpdateAsync(Pi pi);
    Task<bool> DeleteAsync(int id);
}

public class PiService : IPiService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public PiService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<Pi>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Pis.Include(p => p.Features).AsSplitQuery().AsNoTracking().OrderByDescending(p => p.StartDate).ToListAsync();
    }

    public async Task<List<Pi>> GetAllLightAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Pis.AsNoTracking().OrderByDescending(p => p.StartDate).ToListAsync();
    }

    public async Task<Pi?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Pis
            .Include(p => p.Features)
            .AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Pi> CreateAsync(Pi pi)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.Pis.Add(pi); await db.SaveChangesAsync(); return pi;
    }

    public async Task<Pi> UpdateAsync(Pi pi)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.Pis
            .FirstOrDefaultAsync(p => p.Id == pi.Id);

        if (existing is null)
        {
            Log.Warning("Pi {PiId} not found", pi.Id);
            throw new KeyNotFoundException($"Pi {pi.Id} not found.");
        }

        existing.Name = pi.Name;
        existing.Description = pi.Description;
        existing.Priority = pi.Priority;
        existing.Comments = pi.Comments;
        existing.StartDate = pi.StartDate;
        existing.EndDate = pi.EndDate;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var pi = await db.Pis.FindAsync(id);
        if (pi is null) return false;
        db.Pis.Remove(pi); await db.SaveChangesAsync(); return true;
    }
}
