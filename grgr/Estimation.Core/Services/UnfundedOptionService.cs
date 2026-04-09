using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public interface IUnfundedOptionService
{
    Task<List<UnfundedOption>> GetAllAsync();
    Task<UnfundedOption?> GetByIdAsync(int id);
    Task<UnfundedOption> CreateAsync(UnfundedOption option);
    Task<UnfundedOption> UpdateAsync(UnfundedOption option);
    Task<bool> DeleteAsync(int id);
}

public class UnfundedOptionService : IUnfundedOptionService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public UnfundedOptionService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<UnfundedOption>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.UnfundedOptions
            .AsNoTracking()
            .OrderBy(u => u.Order)
            .ThenBy(u => u.Name)
            .ToListAsync();
    }

    public async Task<UnfundedOption?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.UnfundedOptions
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<UnfundedOption> CreateAsync(UnfundedOption option)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.UnfundedOptions.Add(option);
        await db.SaveChangesAsync();
        return option;
    }

    public async Task<UnfundedOption> UpdateAsync(UnfundedOption option)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.UnfundedOptions.FindAsync(option.Id);

        if (existing is null)
        {
            Log.Warning("UnfundedOption {OptionId} not found", option.Id);
            throw new KeyNotFoundException($"UnfundedOption {option.Id} not found.");
        }

        existing.Name = option.Name;
        existing.Description = option.Description;
        existing.Order = option.Order;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var inUse = await db.Features.AnyAsync(f => f.UnfundedOptionId == id);
        if (inUse)
            throw new InvalidOperationException(
                "Cannot delete this option — it is assigned to one or more features.");

        var option = await db.UnfundedOptions.FindAsync(id);
        if (option is null) return false;

        db.UnfundedOptions.Remove(option);
        await db.SaveChangesAsync();
        return true;
    }
}
