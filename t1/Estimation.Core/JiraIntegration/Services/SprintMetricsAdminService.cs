using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.JiraIntegration.Services;

public class TeamBoardMappingRow
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string Trains { get; set; } = string.Empty;
    public List<string> ProjectKeys { get; set; } = [];
    public int? JiraBoardId { get; set; }
    public int SprintCount { get; set; }
    public int MappedSprintCount { get; set; }
}

/// <summary>
/// Admin-facing operations for the sprint-metrics area: engine settings, the agile-API
/// capability probe, and the team→board mapping with sprint-ID discovery. All Jira calls
/// run under the Jira sync service account.
/// </summary>
public interface ISprintMetricsAdminService
{
    Task<SprintMetricsSyncSettings> GetSettingsAsync();
    Task<SprintMetricsSyncSettings> UpdateSettingsAsync(SprintMetricsSyncSettings incoming);
    Task<JiraAgileProbeResult> RunProbeAsync();
    Task<List<TeamBoardMappingRow>> GetTeamBoardMappingsAsync();
    Task SetTeamBoardAsync(int teamId, int? boardId);
    Task<List<JiraBoard>> GetBoardsForTeamAsync(int teamId);
    Task<SprintDiscoveryResult> DiscoverTeamSprintsAsync(int teamId);
}

public class SprintMetricsAdminService : ISprintMetricsAdminService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IJiraAgileService _agile;
    private readonly ISprintJiraDiscoveryService _discovery;

    public SprintMetricsAdminService(
        IDbContextFactory<EstimationDbContext> ctx,
        IJiraAgileService agile,
        ISprintJiraDiscoveryService discovery)
    {
        _ctx = ctx;
        _agile = agile;
        _discovery = discovery;
    }

    public async Task<SprintMetricsSyncSettings> GetSettingsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var settings = await db.SprintMetricsSyncSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new SprintMetricsSyncSettings();
            db.SprintMetricsSyncSettings.Add(settings);
            await db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task<SprintMetricsSyncSettings> UpdateSettingsAsync(SprintMetricsSyncSettings incoming)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var settings = await db.SprintMetricsSyncSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new SprintMetricsSyncSettings();
            db.SprintMetricsSyncSettings.Add(settings);
        }

        settings.CycleCooldownMinutes = Math.Max(1, incoming.CycleCooldownMinutes);
        settings.BackfillBatchSize = Math.Max(1, incoming.BackfillBatchSize);
        settings.IssueTypesCsv = string.IsNullOrWhiteSpace(incoming.IssueTypesCsv)
            ? "Task,Story,Bug"
            : incoming.IssueTypesCsv.Trim();
        settings.DoneStatusesCsv = string.IsNullOrWhiteSpace(incoming.DoneStatusesCsv)
            ? null
            : incoming.DoneStatusesCsv.Trim();

        await db.SaveChangesAsync();
        return settings;
    }

    public async Task<JiraAgileProbeResult> RunProbeAsync()
    {
        var serviceUser = await GetServiceAccountUserNameAsync();
        var result = await _agile.ProbeAsync(serviceUser);

        await using var db = await _ctx.CreateDbContextAsync();
        var settings = await db.SprintMetricsSyncSettings.FirstOrDefaultAsync();
        if (settings is null)
        {
            settings = new SprintMetricsSyncSettings();
            db.SprintMetricsSyncSettings.Add(settings);
        }
        settings.AgileApiAvailable = result.AgileApiAvailable;
        settings.SprintReportAvailable = result.SprintReportAvailable;
        settings.LastProbedAt = DateTime.UtcNow;
        settings.LastProbeMessage = result.Message.Length <= 2000 ? result.Message : result.Message[..2000];
        await db.SaveChangesAsync();

        return result;
    }

    public async Task<List<TeamBoardMappingRow>> GetTeamBoardMappingsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var raw = await db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.JiraBoardId,
                TrainNames = t.CapitalProjectTeams
                    .Select(cpt => cpt.CapitalProject!.Name)
                    .ToList(),
                ProjectKeys = t.CapitalProjectTeams
                    .Where(cpt => cpt.CapitalProject!.JiraKey != null && cpt.CapitalProject.JiraKey != "")
                    .Select(cpt => cpt.CapitalProject!.JiraKey!)
                    .Distinct()
                    .ToList(),
                SprintCount = db.Sprints.Count(s => s.TeamId == t.Id),
                MappedSprintCount = db.Sprints.Count(s => s.TeamId == t.Id && s.JiraSprintId != null),
            })
            .ToListAsync();

        return raw.Select(t => new TeamBoardMappingRow
        {
            TeamId = t.Id,
            TeamName = t.Name,
            JiraBoardId = t.JiraBoardId,
            Trains = string.Join(", ", t.TrainNames.OrderBy(n => n)),
            ProjectKeys = t.ProjectKeys,
            SprintCount = t.SprintCount,
            MappedSprintCount = t.MappedSprintCount,
        }).ToList();
    }

    public async Task SetTeamBoardAsync(int teamId, int? boardId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId)
            ?? throw new InvalidOperationException($"Team {teamId} not found.");
        team.JiraBoardId = boardId;
        await db.SaveChangesAsync();
    }

    public async Task<List<JiraBoard>> GetBoardsForTeamAsync(int teamId)
    {
        var serviceUser = await GetServiceAccountUserNameAsync();

        await using var db = await _ctx.CreateDbContextAsync();
        var projectKeys = await db.CapitalProjectTeams
            .AsNoTracking()
            .Where(cpt => cpt.TeamId == teamId
                          && cpt.CapitalProject!.JiraKey != null
                          && cpt.CapitalProject.JiraKey != "")
            .Select(cpt => cpt.CapitalProject!.JiraKey!)
            .Distinct()
            .ToListAsync();

        if (projectKeys.Count == 0)
        {
            return await _agile.GetBoardsAsync(serviceUser);
        }

        var boards = new Dictionary<int, JiraBoard>();
        foreach (var key in projectKeys)
        {
            foreach (var board in await _agile.GetBoardsAsync(serviceUser, key))
            {
                boards[board.Id] = board;
            }
        }
        return boards.Values.OrderBy(b => b.Name).ToList();
    }

    public async Task<SprintDiscoveryResult> DiscoverTeamSprintsAsync(int teamId)
    {
        var serviceUser = await GetServiceAccountUserNameAsync();
        return await _discovery.DiscoverForTeamAsync(serviceUser, teamId);
    }

    private async Task<string> GetServiceAccountUserNameAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var syncSettings = await db.JiraSyncSettings.AsNoTracking().FirstOrDefaultAsync();
        return syncSettings?.ServiceAccountUserName ?? Models.JiraSyncSettings.DefaultServiceAccountUserName;
    }
}
