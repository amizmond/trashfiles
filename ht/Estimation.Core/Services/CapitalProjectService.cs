using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public interface ICapitalProjectService
{
    Task<List<CapitalProject>> GetAllAsync();
    Task<CapitalProject?> GetByIdAsync(int id);
    Task<CapitalProject> CreateAsync(CapitalProject project);
    Task<CapitalProject> UpdateAsync(CapitalProject project);
    Task<bool> DeleteAsync(int id);
    Task AddProgramAsync(int projectId, int programId);
    Task RemoveProgramAsync(int projectId, int programId);
    Task AddTeamAsync(int projectId, int teamId);
    Task RemoveTeamAsync(int projectId, int teamId);
}

public class CapitalProjectService : ICapitalProjectService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public CapitalProjectService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<CapitalProject>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects
            .Include(cp => cp.CapitalProjectStrategicObjectives).ThenInclude(cpp => cpp.StrategicObjective)
            .Include(cp => cp.CapitalProjectTeams).ThenInclude(cpt => cpt.Team)
            .AsNoTracking().OrderBy(cp => cp.Name).ToListAsync();
    }

    public async Task<CapitalProject?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects
            .Include(cp => cp.CapitalProjectStrategicObjectives).ThenInclude(cpp => cpp.StrategicObjective)
            .Include(cp => cp.CapitalProjectTeams).ThenInclude(cpt => cpt.Team)
            .AsNoTracking().FirstOrDefaultAsync(cp => cp.Id == id);
    }

    public async Task<CapitalProject> CreateAsync(CapitalProject project)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        db.CapitalProjects.Add(project); await db.SaveChangesAsync(); return project;
    }

    public async Task<CapitalProject> UpdateAsync(CapitalProject project)
    {
        await using var context = await _ctx.CreateDbContextAsync();

        var existing = await context.CapitalProjects
            .Include(cp => cp.CapitalProjectStrategicObjectives)
                .ThenInclude(cpp => cpp.StrategicObjective)
            .Include(cp => cp.CapitalProjectTeams)
                .ThenInclude(cpt => cpt.Team)
            .FirstOrDefaultAsync(cp => cp.Id == project.Id);

        if (existing is null)
        {
            Log.Warning("CapitalProject {ProjectId} not found", project.Id);
            throw new KeyNotFoundException($"CapitalProject {project.Id} not found.");
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
        if (cp is null) return false;
        db.CapitalProjects.Remove(cp); await db.SaveChangesAsync(); return true;
    }


    public async Task AddProgramAsync(int projectId, int programId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        if (!await db.CapitalProjectStrategicObjectives.AnyAsync(
                x => x.CapitalProjectId == projectId && x.StrategicObjectiveId == programId))
        {
            db.CapitalProjectStrategicObjectives.Add(
                new CapitalProjectStrategicObjective { CapitalProjectId = projectId, StrategicObjectiveId = programId });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveProgramAsync(int projectId, int programId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.CapitalProjectStrategicObjectives.FirstOrDefaultAsync(
            x => x.CapitalProjectId == projectId && x.StrategicObjectiveId == programId);
        if (e is not null) { db.CapitalProjectStrategicObjectives.Remove(e); await db.SaveChangesAsync(); }
    }

    public async Task AddTeamAsync(int projectId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        if (!await db.CapitalProjectTeams.AnyAsync(
                x => x.CapitalProjectId == projectId && x.TeamId == teamId))
        {
            db.CapitalProjectTeams.Add(
                new CapitalProjectTeam { CapitalProjectId = projectId, TeamId = teamId });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveTeamAsync(int projectId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var e = await db.CapitalProjectTeams.FirstOrDefaultAsync(
            x => x.CapitalProjectId == projectId && x.TeamId == teamId);
        if (e is not null) { db.CapitalProjectTeams.Remove(e); await db.SaveChangesAsync(); }
    }
}
