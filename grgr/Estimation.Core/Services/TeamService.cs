using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Estimation.Core.Services;

public interface ITeamService
{
    Task<List<Team>> GetAllAsync();
    Task<List<Team>> GetAllNamesAsync();
    Task<Team?> GetByIdAsync(int id);
    Task<Team> CreateAsync(Team team);
    Task<Team> UpdateAsync(Team team);
    Task<bool> DeleteAsync(int id);
    Task AddMemberAsync(int teamId, int humanResourceId);
    Task RemoveMemberAsync(int teamId, int humanResourceId);
    Task AddTechnologyStackAsync(int teamId, int technologyStackId);
    Task RemoveTechnologyStackAsync(int teamId, int technologyStackId);
}

public class TeamService : ITeamService, IDisposable
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IMemoryCache _cache;
    private const string TeamNamesCacheKey = "Teams_Names";

    public TeamService(IDbContextFactory<EstimationDbContext> ctx, IMemoryCache cache)
    {
        _ctx = ctx;
        _cache = cache;
    }

    public async Task<List<Team>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Teams
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.Skill)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.SkillLevel)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.TeamRole)
            .Include(t => t.TeamTechnologyStacks).ThenInclude(tts => tts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<List<Team>> GetAllNamesAsync()
    {
        if (_cache.TryGetValue(TeamNamesCacheKey, out List<Team>? cached) && cached is not null)
            return cached;

        await using var db = await _ctx.CreateDbContextAsync();
        var teams = await db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync();

        _cache.Set(TeamNamesCacheKey, teams, TimeSpan.FromMinutes(5));
        return teams;
    }

    private void InvalidateCache() => _cache.Remove(TeamNamesCacheKey);

    public async Task<Team?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Teams
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.Skill)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.HumanResourceSkills).ThenInclude(hrs => hrs.SkillLevel)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.TeamRole)
            .Include(t => t.TeamTechnologyStacks).ThenInclude(tts => tts.TechnologyStack)
            .AsSplitQuery()
            .AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Team> CreateAsync(Team team)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        InvalidateCache();
        return team;
    }

    public async Task<Team> UpdateAsync(Team team)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var existing = await db.Teams
            .FirstOrDefaultAsync(t => t.Id == team.Id);

        if (existing is null)
        {
            Log.Warning("Team {TeamId} not found", team.Id);
            throw new KeyNotFoundException($"Team {team.Id} not found.");
        }

        existing.Name = team.Name;
        existing.FullName = team.FullName;
        existing.OptionalTeamTag = team.OptionalTeamTag;
        existing.Description = team.Description;

        await db.SaveChangesAsync();
        InvalidateCache();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var team = await db.Teams.FindAsync(id);
        if (team is null) return false;

        db.CapitalProjectTeams.RemoveRange(
            db.CapitalProjectTeams.Where(cpt => cpt.TeamId == id));
        db.FeatureTeams.RemoveRange(
            db.FeatureTeams.Where(ft => ft.TeamId == id));

        db.Teams.Remove(team);
        await db.SaveChangesAsync();
        InvalidateCache();
        return true;
    }

    public async Task AddMemberAsync(int teamId, int humanResourceId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var exists = await db.TeamMembers.AnyAsync(
            tm => tm.TeamId == teamId && tm.HumanResourceId == humanResourceId);
        if (!exists)
        {
            db.TeamMembers.Add(new TeamMember { TeamId = teamId, HumanResourceId = humanResourceId });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveMemberAsync(int teamId, int humanResourceId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var entry = await db.TeamMembers.FirstOrDefaultAsync(
            tm => tm.TeamId == teamId && tm.HumanResourceId == humanResourceId);
        if (entry is not null) { db.TeamMembers.Remove(entry); await db.SaveChangesAsync(); }
    }

    public async Task AddTechnologyStackAsync(int teamId, int technologyStackId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        if (!await db.TeamTechnologyStacks.AnyAsync(tts => tts.TeamId == teamId && tts.TechnologyStackId == technologyStackId))
        {
            db.TeamTechnologyStacks.Add(new TeamTechnologyStack { TeamId = teamId, TechnologyStackId = technologyStackId });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveTechnologyStackAsync(int teamId, int technologyStackId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.TeamTechnologyStacks.FirstOrDefaultAsync(
            tts => tts.TeamId == teamId && tts.TechnologyStackId == technologyStackId);
        if (e is not null) { db.TeamTechnologyStacks.Remove(e); await db.SaveChangesAsync(); }
    }

    public void Dispose()
    {
        _cache.Remove(TeamNamesCacheKey);
    }
}
