using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Train.Services;

public enum ArtSprintCascadeKind
{
    Created,
    Updated,
    Deleted,
}

public record ArtSprintCascadeAction(
    ArtSprintCascadeKind Kind,
    string TeamName,
    string SprintName,
    DateTime StartDate,
    DateTime EndDate);

public class ArtSprintPlan
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<ArtSprintCascadeAction> Actions { get; } = [];

    public bool HasErrors => Errors.Count > 0;
}

public interface IArtSprintService
{
    Task<List<CapitalProjectSprint>> GetForProjectAsync(int capitalProjectId);
    Task<ArtSprintPlan> PlanSaveAsync(CapitalProjectSprint entity);
    Task<ArtSprintPlan> SaveAsync(CapitalProjectSprint entity);
    Task<ArtSprintPlan> PlanDeleteAsync(int id);
    Task DeleteAsync(int id, bool deleteTeamSprints);
    Task<ArtSprintPlan> PlanTeamBackfillAsync(int capitalProjectId, int teamId);
    Task<ArtSprintPlan> BackfillTeamAsync(int capitalProjectId, int teamId);
    Task UnlinkTeamAsync(int teamId);
}

public class ArtSprintService : IArtSprintService
{
    private const int SprintNameMaxLength = 100;

    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public ArtSprintService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<CapitalProjectSprint>> GetForProjectAsync(int capitalProjectId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjectSprints
            .Include(s => s.Pi)
            .Where(s => s.CapitalProjectId == capitalProjectId)
            .AsNoTracking()
            .OrderByDescending(s => s.StartDate)
            .ToListAsync();
    }

    public async Task<ArtSprintPlan> PlanSaveAsync(CapitalProjectSprint entity)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        Normalize(entity);
        var plan = await ValidateAsync(db, entity);
        if (plan.HasErrors)
        {
            return plan;
        }

        var teams = await GetManagedTeamsAsync(db, entity.CapitalProjectId);
        foreach (var team in teams)
        {
            var sprints = await db.Sprints.Where(s => s.TeamId == team.Id).AsNoTracking().ToListAsync();
            BuildTeamActions(plan, team, entity, sprints);
        }

        return plan;
    }

    public async Task<ArtSprintPlan> SaveAsync(CapitalProjectSprint entity)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        Normalize(entity);
        var plan = await ValidateAsync(db, entity);
        if (plan.HasErrors)
        {
            return plan;
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        CapitalProjectSprint stored;
        if (entity.Id == 0)
        {
            stored = new CapitalProjectSprint
            {
                CapitalProjectId = entity.CapitalProjectId,
                PiId = entity.PiId,
                Name = entity.Name,
                StartDate = entity.StartDate,
                EndDate = entity.EndDate,
                IsIpSprint = entity.IsIpSprint,
            };
            db.CapitalProjectSprints.Add(stored);
        }
        else
        {
            stored = await db.CapitalProjectSprints.FirstOrDefaultAsync(s => s.Id == entity.Id)
                ?? throw new KeyNotFoundException($"ART sprint {entity.Id} not found.");
            stored.PiId = entity.PiId;
            stored.Name = entity.Name;
            stored.StartDate = entity.StartDate;
            stored.EndDate = entity.EndDate;
            stored.IsIpSprint = entity.IsIpSprint;
        }

        await db.SaveChangesAsync();

        var teams = await GetManagedTeamsAsync(db, stored.CapitalProjectId);
        foreach (var team in teams)
        {
            var sprints = await db.Sprints.Where(s => s.TeamId == team.Id).ToListAsync();
            ApplyTeamActions(plan, db, team, stored, sprints);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        entity.Id = stored.Id;
        return plan;
    }

    public async Task<ArtSprintPlan> PlanDeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var plan = new ArtSprintPlan();

        var derived = await db.Sprints
            .Include(s => s.Team)
            .Where(s => s.SourceArtSprintId == id)
            .AsNoTracking()
            .OrderBy(s => s.Team.Name)
            .ToListAsync();

        foreach (var sprint in derived)
        {
            plan.Actions.Add(new ArtSprintCascadeAction(
                ArtSprintCascadeKind.Deleted, sprint.Team.Name, sprint.Name, sprint.StartDate, sprint.EndDate));
        }

        return plan;
    }

    public async Task DeleteAsync(int id, bool deleteTeamSprints)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var entity = await db.CapitalProjectSprints.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null)
        {
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        var derived = await db.Sprints.Where(s => s.SourceArtSprintId == id).ToListAsync();
        if (deleteTeamSprints)
        {
            db.Sprints.RemoveRange(derived);
        }
        else
        {
            foreach (var sprint in derived)
            {
                sprint.SourceArtSprintId = null;
            }
        }

        await db.SaveChangesAsync();
        db.CapitalProjectSprints.Remove(entity);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<ArtSprintPlan> PlanTeamBackfillAsync(int capitalProjectId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var plan = new ArtSprintPlan();

        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team is null || team.IsArtSharedTeam)
        {
            return plan;
        }

        var artSprints = await LoadArtSprintsAsync(db, capitalProjectId);
        var sprints = await db.Sprints.Where(s => s.TeamId == teamId).AsNoTracking().ToListAsync();
        foreach (var artSprint in artSprints)
        {
            BuildTeamActions(plan, team, artSprint, sprints);
        }

        return plan;
    }

    public async Task<ArtSprintPlan> BackfillTeamAsync(int capitalProjectId, int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var plan = new ArtSprintPlan();

        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId);
        if (team is null || team.IsArtSharedTeam)
        {
            return plan;
        }

        var artSprints = await LoadArtSprintsAsync(db, capitalProjectId);
        if (artSprints.Count == 0)
        {
            return plan;
        }

        await using var tx = await db.Database.BeginTransactionAsync();

        var sprints = await db.Sprints.Where(s => s.TeamId == teamId).ToListAsync();
        foreach (var artSprint in artSprints)
        {
            ApplyTeamActions(plan, db, team, artSprint, sprints);
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return plan;
    }

    public async Task UnlinkTeamAsync(int teamId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var derived = await db.Sprints.Where(s => s.TeamId == teamId && s.SourceArtSprintId != null).ToListAsync();
        if (derived.Count == 0)
        {
            return;
        }

        foreach (var sprint in derived)
        {
            sprint.SourceArtSprintId = null;
        }

        await db.SaveChangesAsync();
    }

    private static Task<List<CapitalProjectSprint>> LoadArtSprintsAsync(EstimationDbContext db, int capitalProjectId)
    {
        return db.CapitalProjectSprints
            .Where(s => s.CapitalProjectId == capitalProjectId)
            .AsNoTracking()
            .OrderBy(s => s.StartDate)
            .ToListAsync();
    }

    private static void Normalize(CapitalProjectSprint entity)
    {
        entity.Name = entity.Name?.Trim() ?? string.Empty;
        entity.StartDate = entity.StartDate.Date;
        entity.EndDate = entity.EndDate.Date;
    }

    private static async Task<ArtSprintPlan> ValidateAsync(EstimationDbContext db, CapitalProjectSprint entity)
    {
        var plan = new ArtSprintPlan();

        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            plan.Errors.Add("Sprint name is required.");
        }

        if (entity.PiId <= 0)
        {
            plan.Errors.Add("PI is required.");
        }

        if (entity.EndDate <= entity.StartDate)
        {
            plan.Errors.Add("End date must be after start date.");
        }

        if (plan.HasErrors)
        {
            return plan;
        }

        var siblings = await db.CapitalProjectSprints
            .Where(s => s.CapitalProjectId == entity.CapitalProjectId && s.Id != entity.Id)
            .AsNoTracking()
            .OrderBy(s => s.StartDate)
            .ToListAsync();

        var overlap = siblings.FirstOrDefault(s => s.StartDate <= entity.EndDate && s.EndDate >= entity.StartDate);
        if (overlap is not null)
        {
            plan.Errors.Add(
                $"Dates overlap sprint '{overlap.Name}' ({overlap.StartDate:yyyy-MM-dd} - {overlap.EndDate:yyyy-MM-dd}).");
        }

        if (siblings.Any(s => string.Equals(s.Name, entity.Name, StringComparison.OrdinalIgnoreCase)))
        {
            plan.Errors.Add($"This ART already has a sprint named '{entity.Name}'.");
        }

        var pi = await db.Pis.AsNoTracking().FirstOrDefaultAsync(p => p.Id == entity.PiId);
        if (pi is null)
        {
            plan.Errors.Add("Selected PI was not found.");
        }

        if (plan.HasErrors)
        {
            return plan;
        }

        var previous = siblings
            .Where(s => s.EndDate < entity.StartDate)
            .OrderByDescending(s => s.EndDate)
            .FirstOrDefault();
        if (previous is not null && previous.EndDate.AddDays(1) != entity.StartDate)
        {
            var gap = (entity.StartDate - previous.EndDate).Days - 1;
            plan.Warnings.Add(
                $"{gap} free day(s) between '{previous.Name}' (ends {previous.EndDate:yyyy-MM-dd}) and this sprint.");
        }

        var next = siblings
            .Where(s => s.StartDate > entity.EndDate)
            .OrderBy(s => s.StartDate)
            .FirstOrDefault();
        if (next is not null && entity.EndDate.AddDays(1) != next.StartDate)
        {
            var gap = (next.StartDate - entity.EndDate).Days - 1;
            plan.Warnings.Add(
                $"{gap} free day(s) between this sprint and '{next.Name}' (starts {next.StartDate:yyyy-MM-dd}).");
        }

        if (pi!.StartDate is not null && pi.EndDate is not null
            && (entity.StartDate < pi.StartDate.Value.Date || entity.EndDate > pi.EndDate.Value.Date))
        {
            plan.Warnings.Add(
                $"Dates fall outside PI '{pi.Name}' ({pi.StartDate:yyyy-MM-dd} - {pi.EndDate:yyyy-MM-dd}).");
        }

        return plan;
    }

    private static Task<List<Team>> GetManagedTeamsAsync(EstimationDbContext db, int capitalProjectId)
    {
        return db.Teams
            .Where(t => !t.IsArtSharedTeam && t.CapitalProjectTeams.Any(cpt => cpt.CapitalProjectId == capitalProjectId))
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    private static string DerivedName(Team team, CapitalProjectSprint artSprint)
    {
        var teamName = string.IsNullOrWhiteSpace(team.JiraName) ? team.Name : team.JiraName.Trim();
        var name = $"{teamName} {artSprint.Name}";
        if (name.Length > SprintNameMaxLength)
        {
            Log.Warning("Derived sprint name {Name} exceeds {Max} characters and was truncated", name, SprintNameMaxLength);
            name = name[..SprintNameMaxLength];
        }

        return name;
    }

    private static bool HasRequiredFormat(Sprint sprint, Team team, CapitalProjectSprint artSprint)
    {
        return string.Equals(sprint.Name, DerivedName(team, artSprint), StringComparison.OrdinalIgnoreCase)
            || string.Equals(sprint.Name, $"{team.Name} {artSprint.Name}", StringComparison.OrdinalIgnoreCase);
    }

    private static (Sprint? Winner, List<Sprint> Losers) Partition(
        List<Sprint> teamSprints, CapitalProjectSprint artSprint)
    {
        var winner = teamSprints.FirstOrDefault(s => s.SourceArtSprintId == artSprint.Id)
            ?? teamSprints.FirstOrDefault(s =>
                s.SourceArtSprintId is null
                && s.StartDate.Date == artSprint.StartDate
                && s.EndDate.Date == artSprint.EndDate);

        var losers = teamSprints
            .Where(s => !ReferenceEquals(s, winner)
                && s.SourceArtSprintId != artSprint.Id
                && s.StartDate.Date <= artSprint.EndDate
                && s.EndDate.Date >= artSprint.StartDate)
            .ToList();

        return (winner, losers);
    }

    private static void BuildTeamActions(
        ArtSprintPlan plan, Team team, CapitalProjectSprint artSprint, List<Sprint> teamSprints)
    {
        var (winner, losers) = Partition(teamSprints, artSprint);

        foreach (var loser in losers)
        {
            plan.Actions.Add(new ArtSprintCascadeAction(
                ArtSprintCascadeKind.Deleted, team.Name, loser.Name, loser.StartDate, loser.EndDate));
        }

        var name = winner is not null && HasRequiredFormat(winner, team, artSprint)
            ? winner.Name
            : DerivedName(team, artSprint);

        plan.Actions.Add(new ArtSprintCascadeAction(
            winner is null ? ArtSprintCascadeKind.Created : ArtSprintCascadeKind.Updated,
            team.Name, name, artSprint.StartDate, artSprint.EndDate));
    }

    private static void ApplyTeamActions(
        ArtSprintPlan plan, EstimationDbContext db, Team team, CapitalProjectSprint artSprint, List<Sprint> teamSprints)
    {
        var (winner, losers) = Partition(teamSprints, artSprint);

        var carriedComment = winner?.Comment;
        var carriedColor = winner?.ColorHex;
        foreach (var loser in losers)
        {
            carriedComment ??= loser.Comment;
            carriedColor ??= loser.ColorHex;
            plan.Actions.Add(new ArtSprintCascadeAction(
                ArtSprintCascadeKind.Deleted, team.Name, loser.Name, loser.StartDate, loser.EndDate));
            teamSprints.Remove(loser);
            db.Sprints.Remove(loser);
        }

        if (winner is null)
        {
            winner = new Sprint
            {
                TeamId = team.Id,
                Name = DerivedName(team, artSprint),
                Comment = carriedComment,
                ColorHex = carriedColor,
            };
            db.Sprints.Add(winner);
            teamSprints.Add(winner);
            plan.Actions.Add(new ArtSprintCascadeAction(
                ArtSprintCascadeKind.Created, team.Name, winner.Name, artSprint.StartDate, artSprint.EndDate));
        }
        else
        {
            if (!HasRequiredFormat(winner, team, artSprint))
            {
                winner.Name = DerivedName(team, artSprint);
            }

            winner.Comment ??= carriedComment;
            winner.ColorHex ??= carriedColor;
            plan.Actions.Add(new ArtSprintCascadeAction(
                ArtSprintCascadeKind.Updated, team.Name, winner.Name, artSprint.StartDate, artSprint.EndDate));
        }

        winner.SourceArtSprintId = artSprint.Id;
        winner.PiId = artSprint.PiId;
        winner.IsIpSprint = artSprint.IsIpSprint;
        winner.StartDate = artSprint.StartDate;
        winner.EndDate = artSprint.EndDate;
    }
}
