using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Estimation.Core.Services;

public interface ISkillService
{
    Task<List<Skill>> GetAllAsync();

    Task<Skill?> GetByIdAsync(int id);

    Task<Skill> CreateAsync(Skill skill);

    Task<Skill> UpdateAsync(Skill skill);

    Task<bool> DeleteAsync(int id);
}

public class SkillService : ISkillService, IDisposable
{
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private const string AllSkillsCacheKey = "Skills_All";

    public SkillService(IDbContextFactory<EstimationDbContext> contextFactory, IMemoryCache cache)
    {
        _contextFactory = contextFactory;
        _cache = cache;
    }

    public async Task<List<Skill>> GetAllAsync()
    {
        if (_cache.TryGetValue(AllSkillsCacheKey, out List<Skill>? cached) && cached is not null)
            return cached;

        await using var context = await _contextFactory.CreateDbContextAsync();

        var skills = await context.Skills
            .Include(s => s.Levels)
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync();

        _cache.Set(AllSkillsCacheKey, skills, TimeSpan.FromMinutes(5));
        return skills;
    }

    private void InvalidateCache() => _cache.Remove(AllSkillsCacheKey);

    public async Task<Skill?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Skills
            .Include(s => s.Levels)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Skill> CreateAsync(Skill skill)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        skill.Created = DateTime.Now;
        skill.Updated = null;

        context.Skills.Add(skill);
        await context.SaveChangesAsync();
        InvalidateCache();
        return skill;
    }

    public async Task<Skill> UpdateAsync(Skill skill)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var existing = await context.Skills
            .Include(s => s.Levels)
            .FirstOrDefaultAsync(s => s.Id == skill.Id);

        if (existing is null)
        {
            Log.Warning("Skill {SkillId} not found", skill.Id);
            throw new KeyNotFoundException($"Skill with id {skill.Id} not found.");
        }

        existing.Name = skill.Name;
        existing.Description = skill.Description;
        existing.Updated = DateTime.Now;

        var incomingIds = skill.Levels
            .Where(l => l.Id != 0)
            .Select(l => l.Id)
            .ToHashSet();

        existing.Levels
            .Where(l => !incomingIds.Contains(l.Id))
            .ToList()
            .ForEach(l => existing.Levels.Remove(l));

        foreach (var incomingLevel in skill.Levels)
        {
            if (incomingLevel.Id == 0)
            {
                existing.Levels.Add(incomingLevel);
            }
            else
            {
                var existingLevel = existing.Levels.FirstOrDefault(l => l.Id == incomingLevel.Id);
                if (existingLevel is not null)
                {
                    existingLevel.Name = incomingLevel.Name;
                    existingLevel.Description = incomingLevel.Description;
                    existingLevel.Value = incomingLevel.Value;
                }
            }
        }

        await context.SaveChangesAsync();
        InvalidateCache();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var skill = await context.Skills.FindAsync(id);
        if (skill is null) return false;

        context.HumanResourceSkills.RemoveRange(
            context.HumanResourceSkills.Where(hrs => hrs.SkillId == id));
        context.TechnologyStackSkills.RemoveRange(
            context.TechnologyStackSkills.Where(tss => tss.SkillId == id));

        context.Skills.Remove(skill);
        await context.SaveChangesAsync();
        InvalidateCache();
        return true;
    }

    public void Dispose()
    {
        _cache.Remove(AllSkillsCacheKey);
    }
}
