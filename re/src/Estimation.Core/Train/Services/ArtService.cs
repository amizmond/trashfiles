using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Train.Services;

public interface IArtService
{
    Task<List<Art>> GetAllAsync();
    Task<List<Art>> GetAllLightAsync();

    Task<Art?> GetByIdAsync(int id);
    Task<Art> CreateAsync(Art project);
    Task<Art> UpdateAsync(Art project);
    Task<bool> DeleteAsync(int id);
    Task<bool> JiraKeyExistsAsync(string jiraKey);
    Task AddTeamAsync(int projectId, int teamId);
    Task RemoveTeamAsync(int projectId, int teamId);
}

public class ArtService : IArtService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public ArtService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<Art>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects
            .Include(cp => cp.CapitalProjectTeams).ThenInclude(cpt => cpt.Team)
            .AsNoTracking().OrderBy(cp => cp.Name).ToListAsync();
    }

    public async Task<List<Art>> GetAllLightAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects
            .AsNoTracking().OrderBy(cp => cp.Id).ToListAsync();
    }

    public async Task<Art?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects
            .Include(cp => cp.CapitalProjectTeams).ThenInclude(cpt => cpt.Team)
            .AsNoTracking().FirstOrDefaultAsync(cp => cp.Id == id);
    }

    public async Task<Art> CreateAsync(Art project)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.CapitalProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    public async Task<Art> UpdateAsync(Art project)
    {
        await using var context = await _ctx.CreateDbContextAsync();

        var existing = await context.CapitalProjects
            .Include(cp => cp.CapitalProjectTeams)
                .ThenInclude(cpt => cpt.Team)
            .FirstOrDefaultAsync(cp => cp.Id == project.Id);

        if (existing is null)
        {
            Log.Warning("Art {ProjectId} not found", project.Id);
            throw new KeyNotFoundException($"Art {project.Id} not found.");
        }

        existing.JiraKey = project.JiraKey;
        existing.Name = project.Name;
        existing.Description = project.Description;

        await context.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var cp = await db.CapitalProjects.FindAsync(id);
        if (cp is null)
        {
            return false;
        }
        db.CapitalProjects.Remove(cp);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> JiraKeyExistsAsync(string jiraKey)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects.AnyAsync(cp => cp.JiraKey == jiraKey);
    }

    public async Task AddTeamAsync(int projectId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        if (await db.CapitalProjectTeams.AnyAsync(
                x => x.CapitalProjectId == projectId && x.TeamId == teamId))
        {
            return;
        }

        await EnsureTeamCanJoinAsync(db, projectId, teamId);

        db.CapitalProjectTeams.Add(
            new ArtTeam { CapitalProjectId = projectId, TeamId = teamId });
        await db.SaveChangesAsync();
    }

    public async Task RemoveTeamAsync(int projectId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.CapitalProjectTeams.FirstOrDefaultAsync(
            x => x.CapitalProjectId == projectId && x.TeamId == teamId);
        if (e is null)
        {
            return;
        }

        var derived = await db.Sprints
            .Where(s => s.TeamId == teamId
                && s.SourceArtSprintId != null
                && s.SourceArtSprint!.CapitalProjectId == projectId)
            .ToListAsync();
        foreach (var sprint in derived)
        {
            sprint.SourceArtSprintId = null;
        }

        db.CapitalProjectTeams.Remove(e);
        await db.SaveChangesAsync();
    }

    public static async Task<string?> FindBlockingArtNameAsync(EstimationDbContext db, int projectId, int teamId)
    {
        var isShared = await db.Teams
            .Where(t => t.Id == teamId)
            .Select(t => t.IsArtSharedTeam)
            .FirstOrDefaultAsync();
        if (isShared)
        {
            return null;
        }

        return await db.CapitalProjectTeams
            .Where(x => x.TeamId == teamId && x.CapitalProjectId != projectId)
            .Select(x => x.Art.Name)
            .FirstOrDefaultAsync();
    }

    public static async Task EnsureTeamCanJoinAsync(EstimationDbContext db, int projectId, int teamId)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team is null)
        {
            throw new InvalidOperationException($"Team {teamId} not found.");
        }

        var otherProject = await FindBlockingArtNameAsync(db, projectId, teamId);
        if (otherProject is not null)
        {
            throw new InvalidOperationException(
                $"Team '{team.Name}' already belongs to ART '{otherProject}'. Mark the team as shared between ARTs to add it here.");
        }
    }
}
