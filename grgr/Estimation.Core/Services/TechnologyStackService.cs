using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Estimation.Core.Services;

public interface ITechnologyStackService
{
    Task<List<TechnologyStack>> GetAllAsync();
    Task<List<TechnologyStack>> GetAllWithSkillsAsync();
    Task<TechnologyStack?> GetByIdAsync(int id);
    Task<TechnologyStack> CreateAsync(TechnologyStack stack);
    Task<TechnologyStack> UpdateAsync(TechnologyStack stack);
    Task<bool> DeleteAsync(int id);
    Task AddSkillAsync(int technologyStackId, int skillId);
    Task RemoveSkillAsync(int technologyStackId, int skillId);
}

public class TechnologyStackService : ITechnologyStackService, IDisposable
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IMemoryCache _cache;
    private const string CacheKey = "TechnologyStacks_All";

    public TechnologyStackService(IDbContextFactory<EstimationDbContext> ctx, IMemoryCache cache)
    {
        _ctx = ctx;
        _cache = cache;
    }

    public async Task<List<TechnologyStack>> GetAllAsync()
    {
        if (_cache.TryGetValue(CacheKey, out List<TechnologyStack>? cached) && cached is not null)
            return cached;

        await using var db = await _ctx.CreateDbContextAsync();
        var stacks = await db.TechnologyStacks
            .Include(ts => ts.TechnologyStackSkills).ThenInclude(tss => tss.Skill)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(ts => ts.Name)
            .ToListAsync();

        _cache.Set(CacheKey, stacks, TimeSpan.FromMinutes(5));
        return stacks;
    }

    public async Task<List<TechnologyStack>> GetAllWithSkillsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.TechnologyStacks
            .Include(ts => ts.TechnologyStackSkills).ThenInclude(tss => tss.Skill)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(ts => ts.Name)
            .ToListAsync();
    }

    private void InvalidateCache() => _cache.Remove(CacheKey);

    public async Task<TechnologyStack?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.TechnologyStacks
            .Include(ts => ts.TechnologyStackSkills).ThenInclude(tss => tss.Skill)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(ts => ts.Id == id);
    }

    public async Task<TechnologyStack> CreateAsync(TechnologyStack stack)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.TechnologyStacks.Add(stack);
        await db.SaveChangesAsync();
        InvalidateCache();
        return stack;
    }

    public async Task<TechnologyStack> UpdateAsync(TechnologyStack stack)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.TechnologyStacks.FirstOrDefaultAsync(ts => ts.Id == stack.Id);
        if (existing is null)
        {
            Log.Warning("TechnologyStack {Id} not found", stack.Id);
            throw new KeyNotFoundException($"TechnologyStack {stack.Id} not found.");
        }

        existing.Name = stack.Name;
        existing.Description = stack.Description;
        await db.SaveChangesAsync();
        InvalidateCache();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var stack = await db.TechnologyStacks.FindAsync(id);
        if (stack is null) return false;

        db.TechnologyStacks.Remove(stack);
        await db.SaveChangesAsync();
        InvalidateCache();
        return true;
    }

    public async Task AddSkillAsync(int technologyStackId, int skillId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        if (!await db.TechnologyStackSkills.AnyAsync(tss => tss.TechnologyStackId == technologyStackId && tss.SkillId == skillId))
        {
            db.TechnologyStackSkills.Add(new TechnologyStackSkill { TechnologyStackId = technologyStackId, SkillId = skillId });
            await db.SaveChangesAsync();
            InvalidateCache();
        }
    }

    public async Task RemoveSkillAsync(int technologyStackId, int skillId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.TechnologyStackSkills.FirstOrDefaultAsync(
            tss => tss.TechnologyStackId == technologyStackId && tss.SkillId == skillId);
        if (e is not null) { db.TechnologyStackSkills.Remove(e); await db.SaveChangesAsync(); InvalidateCache(); }
    }

    public void Dispose()
    {
        _cache.Remove(CacheKey);
    }
}
