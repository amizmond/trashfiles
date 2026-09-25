using System.Text.RegularExpressions;
using Estimation.Core.Features.Services;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Train.Services;

public sealed record IssueMatchFacts(string? ProjectKey, string? JiraId, string? Labels, string? Components);

public interface IArtService
{
    Task<List<Art>> GetAllAsync();
    Task<List<Art>> GetAllLightAsync();

    Task<Art?> GetByIdAsync(int id);
    Task<Art> CreateAsync(Art project);
    Task<Art> UpdateAsync(Art project, bool updateKeys = true);
    Task<IReadOnlyList<ArtJiraKey>> SaveJiraKeyAsync(int artId, ArtJiraKey row, bool isNew);
    Task<IReadOnlyList<ArtJiraKey>> RemoveJiraKeyAsync(int artId, string jiraKey);
    Task<bool> DeleteAsync(int id);
    Task<IReadOnlyList<IssueMatchFacts>> GetFeatureMatchFactsAsync(IReadOnlyCollection<string> jiraKeys);
    Task AddTeamAsync(int projectId, int teamId);
    Task RemoveTeamAsync(int projectId, int teamId);
}

public partial class ArtService : IArtService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    public ArtService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<List<Art>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects
            .Include(cp => cp.JiraKeys)
            .Include(cp => cp.CapitalProjectTeams).ThenInclude(cpt => cpt.Team)
            .AsSplitQuery()
            .AsNoTracking().OrderBy(cp => cp.Name).ToListAsync();
    }

    public async Task<List<Art>> GetAllLightAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects
            .Include(cp => cp.JiraKeys)
            .AsNoTracking().OrderBy(cp => cp.Id).ToListAsync();
    }

    public async Task<Art?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.CapitalProjects
            .Include(cp => cp.JiraKeys)
            .Include(cp => cp.CapitalProjectTeams).ThenInclude(cpt => cpt.Team)
            .AsSplitQuery()
            .AsNoTracking().FirstOrDefaultAsync(cp => cp.Id == id);
    }

    public async Task<Art> CreateAsync(Art project)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var rows = NormalizeRows(project.JiraKeys);
        await EnsureRowsAllowedAsync(db, project.Id, rows, rows.Select(r => r.JiraKey).ToList());

        await ResetSyncWatermarksAsync(db, rows.Select(r => r.JiraKey).ToList());
        project.JiraKeys = rows.ToList();
        db.CapitalProjects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    public async Task<Art> UpdateAsync(Art project, bool updateKeys = true)
    {
        await using var context = await _ctx.CreateDbContextAsync();

        var existing = await context.CapitalProjects
            .Include(cp => cp.JiraKeys)
            .Include(cp => cp.CapitalProjectTeams)
                .ThenInclude(cpt => cpt.Team)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cp => cp.Id == project.Id);

        if (existing is null)
        {
            Log.Warning("Art {ProjectId} not found", project.Id);
            throw new KeyNotFoundException($"Art {project.Id} not found.");
        }

        if (updateKeys)
        {
            await UpdateKeyRowsAsync(context, existing, NormalizeRows(project.JiraKeys));
        }

        existing.Name = project.Name;
        existing.Description = project.Description;
        existing.DepartmentId = project.DepartmentId;

        await context.SaveChangesAsync();
        return existing;
    }

    public Task<IReadOnlyList<ArtJiraKey>> SaveJiraKeyAsync(int artId, ArtJiraKey row, bool isNew)
    {
        var key = JiraProjectKeys.Normalize(row.JiraKey)
            ?? throw new InvalidOperationException("Enter a Jira project key.");

        return ChangeKeyRowsAsync(artId, stored =>
        {
            var has = stored.Any(k => string.Equals(k.JiraKey, key, StringComparison.OrdinalIgnoreCase));
            if (isNew && has)
            {
                throw new InvalidOperationException($"This ART already has Jira key {key}. Edit that row instead.");
            }
            if (!isNew && !has)
            {
                throw new InvalidOperationException($"This ART no longer has Jira key {key}. Reload the page.");
            }

            var edited = new ArtJiraKey { JiraKey = key, Components = row.Components, Labels = row.Labels };
            return isNew
                ? stored.Append(edited)
                : stored.Select(k => string.Equals(k.JiraKey, key, StringComparison.OrdinalIgnoreCase) ? edited : k);
        });
    }

    public Task<IReadOnlyList<ArtJiraKey>> RemoveJiraKeyAsync(int artId, string jiraKey) =>
        ChangeKeyRowsAsync(artId, stored =>
            stored.Where(k => !string.Equals(k.JiraKey, JiraProjectKeys.Normalize(jiraKey), StringComparison.OrdinalIgnoreCase)));

    private async Task<IReadOnlyList<ArtJiraKey>> ChangeKeyRowsAsync(int artId, Func<IReadOnlyList<ArtJiraKey>, IEnumerable<ArtJiraKey>> change)
    {
        await using var context = await _ctx.CreateDbContextAsync();
        var existing = await context.CapitalProjects
            .Include(cp => cp.JiraKeys)
            .FirstOrDefaultAsync(cp => cp.Id == artId)
            ?? throw new KeyNotFoundException($"Art {artId} not found.");

        var stored = existing.JiraKeys.OrderBy(k => k.Id).ToList();
        await UpdateKeyRowsAsync(context, existing, NormalizeRows(change(stored)));
        await context.SaveChangesAsync();

        return existing.JiraKeys
            .OrderBy(k => k.Id)
            .Select(k => new ArtJiraKey
            {
                Id = k.Id,
                CapitalProjectId = k.CapitalProjectId,
                JiraKey = k.JiraKey,
                Components = k.Components,
                Labels = k.Labels
            })
            .ToList();
    }

    private static async Task UpdateKeyRowsAsync(EstimationDbContext context, Art existing, IReadOnlyList<ArtJiraKey> rows)
    {
        var current = existing.JiraKeys.ToDictionary(k => k.JiraKey, StringComparer.OrdinalIgnoreCase);
        var added = rows.Where(r => !current.ContainsKey(r.JiraKey)).ToList();
        var refiltered = rows
            .Where(r => current.TryGetValue(r.JiraKey, out var stored)
                && !ArtKeyScope.Create(0, r.JiraKey, r.Components, r.Labels)
                    .HasSameFilters(ArtKeyScope.Create(0, stored.JiraKey, stored.Components, stored.Labels)))
            .ToList();
        await EnsureRowsAllowedAsync(context, existing.Id, [.. added, .. refiltered], added.Select(r => r.JiraKey).ToList());

        var keep = rows.Select(r => r.JiraKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = existing.JiraKeys.Where(k => !keep.Contains(k.JiraKey)).ToList();

        await ResetSyncWatermarksAsync(context, added.Select(r => r.JiraKey).Concat(removed.Select(k => k.JiraKey)).ToList());

        foreach (var row in removed)
        {
            existing.JiraKeys.Remove(row);
            context.CapitalProjectJiraKeys.Remove(row);
        }

        foreach (var row in refiltered)
        {
            var stored = current[row.JiraKey];
            stored.Components = row.Components;
            stored.Labels = row.Labels;
        }

        foreach (var row in added)
        {
            row.CapitalProjectId = existing.Id;
            existing.JiraKeys.Add(row);
        }
    }

    private static async Task ResetSyncWatermarksAsync(EstimationDbContext db, IReadOnlyCollection<string> jiraKeys)
    {
        if (jiraKeys.Count == 0)
        {
            return;
        }

        var rows = await db.JiraSyncKeys
            .Where(k => jiraKeys.Contains(k.JiraKey))
            .ToListAsync();
        foreach (var row in rows)
        {
            row.LastSyncedWatermarkUtc = null;
        }
    }

    public async Task<IReadOnlyList<IssueMatchFacts>> GetFeatureMatchFactsAsync(IReadOnlyCollection<string> jiraKeys)
    {
        if (jiraKeys.Count == 0)
        {
            return [];
        }

        await using var db = await _ctx.CreateDbContextAsync();
        return await db.Features
            .LikelyOnArt(jiraKeys)
            .AsNoTracking()
            .Select(f => new IssueMatchFacts(f.ProjectKey, f.JiraId, f.Labels, f.Components))
            .ToListAsync();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var cp = await db.CapitalProjects
            .Include(a => a.JiraKeys)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (cp is null)
        {
            return false;
        }

        await ResetSyncWatermarksAsync(db, cp.JiraKeys.Select(k => k.JiraKey).ToList());

        db.CapitalProjects.Remove(cp);
        await db.SaveChangesAsync();
        return true;
    }

    public static bool IsValidKey(string key) =>
        key.Length <= ArtJiraKey.MaxKeyLength && JiraKeyPattern().IsMatch(key);

    public static IReadOnlyList<ArtJiraKey> NormalizeRows(IEnumerable<ArtJiraKey> rows) =>
        rows.Select(r => new ArtJiraKey
            {
                JiraKey = JiraProjectKeys.Normalize(r.JiraKey) ?? string.Empty,
                Components = JiraListValues.Join(JiraListValues.Parse(r.Components)),
                Labels = JiraListValues.Join(JiraListValues.Parse(r.Labels))
            })
            .Where(r => r.JiraKey.Length > 0)
            .DistinctBy(r => r.JiraKey)
            .ToList();

    private static async Task EnsureRowsAllowedAsync(
        EstimationDbContext db, int artId, IReadOnlyCollection<ArtJiraKey> changedRows, IReadOnlyCollection<string> addedKeys)
    {
        var invalid = addedKeys.Where(k => !IsValidKey(k)).ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Not a valid Jira project key: {string.Join(", ", invalid)}. A key starts with a letter and has up to {ArtJiraKey.MaxKeyLength} letters, digits or underscores.");
        }

        var tooLong = changedRows
            .Where(r => r.Components?.Length > ArtJiraKey.MaxFilterLength || r.Labels?.Length > ArtJiraKey.MaxFilterLength)
            .Select(r => r.JiraKey)
            .ToList();
        if (tooLong.Count > 0)
        {
            throw new InvalidOperationException(
                $"The components or labels of {string.Join(", ", tooLong)} are longer than {ArtJiraKey.MaxFilterLength} characters.");
        }

        if (changedRows.Count == 0)
        {
            return;
        }

        var keys = changedRows.Select(r => r.JiraKey).ToList();
        var others = await db.CapitalProjectJiraKeys
            .AsNoTracking()
            .Where(k => k.CapitalProjectId != artId && keys.Contains(k.JiraKey))
            .Select(k => new { k.JiraKey, k.Components, k.Labels, ArtName = k.Art.Name })
            .ToListAsync();

        var errors = new List<string>();
        foreach (var row in changedRows)
        {
            var scope = ArtKeyScope.Create(artId, row.JiraKey, row.Components, row.Labels);
            foreach (var other in others.Where(o => string.Equals(o.JiraKey, row.JiraKey, StringComparison.OrdinalIgnoreCase)).OrderBy(o => o.ArtName))
            {
                if (!scope.HasSameFilters(ArtKeyScope.Create(0, other.JiraKey, other.Components, other.Labels)))
                {
                    continue;
                }

                errors.Add(scope.IsFiltered
                    ? $"Jira key {row.JiraKey} already has the same components and labels on ART '{other.ArtName}'."
                    : $"Jira key {row.JiraKey} is already used without components or labels by ART '{other.ArtName}'. Give one of the two ARTs components or labels for this key.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex JiraKeyPattern();

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
