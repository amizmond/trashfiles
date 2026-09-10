using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace Estimation.Core.Resources.Services;

public interface ITeamService
{
    Task<List<Team>> GetAllAsync();
    Task<List<Team>> GetAllNamesAsync();

    Task<IReadOnlyDictionary<int, IReadOnlyCollection<int>>> GetTeamStackIdsAsync();

    Task<Team?> GetByIdAsync(int id);
    Task<Team> CreateAsync(Team team);
    Task<Team> UpdateAsync(Team team);
    Task<bool> DeleteAsync(int id);
    Task AddMemberAsync(int teamId, int humanResourceId);
    Task RemoveMemberAsync(int teamId, int humanResourceId);
    Task SetTeamMemberRoleAsync(int teamId, int humanResourceId, int? teamRoleId);
    Task AddTechnologyStackAsync(int teamId, int technologyStackId);
    Task RemoveTechnologyStackAsync(int teamId, int technologyStackId);
    Task AssignMemberToStackAsync(int teamId, int humanResourceId, int technologyStackId);
    Task UnassignMemberFromStackAsync(int teamId, int humanResourceId, int technologyStackId);
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
                .ThenInclude(hr => hr.City).ThenInclude(c => c!.Country)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.HumanResource)
                .ThenInclude(hr => hr.EmployeeCategory)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.TeamRole)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.MemberTechnologyStacks).ThenInclude(mts => mts.TechnologyStack)
            .Include(t => t.TeamTechnologyStacks).ThenInclude(tts => tts.TechnologyStack)
            .Include(t => t.CapitalProjectTeams).ThenInclude(cpt => cpt.CapitalProject)
            .AsSplitQuery()
            .AsNoTracking().OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<List<Team>> GetAllNamesAsync()
    {
        if (_cache.TryGetValue(TeamNamesCacheKey, out List<Team>? cached) && cached is not null)
        {
            return cached;
        }

        await using var db = await _ctx.CreateDbContextAsync();
        var teams = await db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync();

        _cache.Set(TeamNamesCacheKey, teams, TimeSpan.FromMinutes(5));
        return teams;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyCollection<int>>> GetTeamStackIdsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var rows = await db.TeamTechnologyStacks
            .AsNoTracking()
            .Select(tts => new { tts.TeamId, tts.TechnologyStackId })
            .ToListAsync();

        return rows
            .GroupBy(r => r.TeamId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<int>)g.Select(r => r.TechnologyStackId).ToList());
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
                .ThenInclude(hr => hr.City).ThenInclude(c => c!.Country)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.TeamRole)
            .Include(t => t.TeamMembers).ThenInclude(tm => tm.MemberTechnologyStacks).ThenInclude(mts => mts.TechnologyStack)
            .Include(t => t.TeamTechnologyStacks).ThenInclude(tts => tts.TechnologyStack)
            .Include(t => t.CapitalProjectTeams)
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

        if (existing.IsArtSharedTeam && !team.IsArtSharedTeam)
        {
            var artNames = await db.CapitalProjectTeams
                .Where(cpt => cpt.TeamId == team.Id)
                .Select(cpt => cpt.CapitalProject.Name)
                .ToListAsync();
            if (artNames.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Team '{existing.Name}' belongs to {artNames.Count} ARTs ({string.Join(", ", artNames)}). Remove it from all but one before clearing the shared flag.");
            }
        }

        if (!existing.IsArtSharedTeam && team.IsArtSharedTeam)
        {
            var derived = await db.Sprints
                .Where(s => s.TeamId == team.Id && s.SourceArtSprintId != null)
                .ToListAsync();
            foreach (var sprint in derived)
            {
                sprint.SourceArtSprintId = null;
            }
        }

        existing.Name = team.Name;
        existing.FullName = team.FullName;
        existing.OptionalTeamTag = team.OptionalTeamTag;
        existing.Description = team.Description;
        existing.IsArtSharedTeam = team.IsArtSharedTeam;
        existing.JiraName = string.IsNullOrWhiteSpace(team.JiraName) ? null : team.JiraName.Trim();

        await db.SaveChangesAsync();
        InvalidateCache();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var team = await db.Teams.FindAsync(id);
        if (team is null)
        {
            return false;
        }

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
        if (entry is not null)
        { db.TeamMembers.Remove(entry); await db.SaveChangesAsync(); }
    }

    public async Task SetTeamMemberRoleAsync(int teamId, int humanResourceId, int? teamRoleId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var entry = await db.TeamMembers.FirstOrDefaultAsync(
            tm => tm.TeamId == teamId && tm.HumanResourceId == humanResourceId);
        if (entry is not null)
        { entry.TeamRoleId = teamRoleId; await db.SaveChangesAsync(); }
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
        if (e is not null)
        {
            db.TeamTechnologyStacks.Remove(e);
            db.TeamMemberTechnologyStacks.RemoveRange(
                db.TeamMemberTechnologyStacks.Where(
                    mts => mts.TeamId == teamId && mts.TechnologyStackId == technologyStackId));
            await db.SaveChangesAsync();
        }
    }

    public async Task AssignMemberToStackAsync(int teamId, int humanResourceId, int technologyStackId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var exists = await db.TeamMemberTechnologyStacks.AnyAsync(
            mts => mts.TeamId == teamId && mts.HumanResourceId == humanResourceId && mts.TechnologyStackId == technologyStackId);
        if (!exists)
        {
            db.TeamMemberTechnologyStacks.Add(new TeamMemberTechnologyStack
            {
                TeamId = teamId,
                HumanResourceId = humanResourceId,
                TechnologyStackId = technologyStackId,
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task UnassignMemberFromStackAsync(int teamId, int humanResourceId, int technologyStackId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.TeamMemberTechnologyStacks.FirstOrDefaultAsync(
            mts => mts.TeamId == teamId && mts.HumanResourceId == humanResourceId && mts.TechnologyStackId == technologyStackId);
        if (e is not null)
        { db.TeamMemberTechnologyStacks.Remove(e); await db.SaveChangesAsync(); }
    }

    public void Dispose()
    {
        _cache.Remove(TeamNamesCacheKey);
    }
}
