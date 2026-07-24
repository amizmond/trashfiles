using Estimation.Core.JiraIntegration.Client;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.JiraIntegration.Services;

public class SprintDiscoveryResult
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int LocalSprints { get; set; }
    public int MatchedByName { get; set; }
    public int MatchedByDates { get; set; }
    public List<string> Unmatched { get; set; } = [];
    public string? Error { get; set; }
}

public interface ISprintJiraDiscoveryService
{
    /// <summary>
    /// Fetches all sprints of the team's mapped Jira board and stamps matching local Sprint
    /// rows with the Jira sprint id, state, and actual start/complete instants. Matched ids
    /// let sprint fetches use the unambiguous <c>sprint = &lt;id&gt;</c> JQL.
    /// </summary>
    Task<SprintDiscoveryResult> DiscoverForTeamAsync(string userName, int teamId, CancellationToken cancellationToken = default);
}

public class SprintJiraDiscoveryService : ISprintJiraDiscoveryService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IJiraAgileService _agile;

    public SprintJiraDiscoveryService(
        IDbContextFactory<EstimationDbContext> ctx,
        IJiraAgileService agile)
    {
        _ctx = ctx;
        _agile = agile;
    }

    public async Task<SprintDiscoveryResult> DiscoverForTeamAsync(
        string userName, int teamId, CancellationToken cancellationToken = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);
        if (team is null)
        {
            return new SprintDiscoveryResult { TeamId = teamId, Error = "Team not found." };
        }

        var result = new SprintDiscoveryResult { TeamId = teamId, TeamName = team.Name };
        if (team.JiraBoardId is null)
        {
            result.Error = "No Jira board is mapped to this team.";
            return result;
        }

        var localSprints = await db.Sprints
            .Where(s => s.TeamId == teamId)
            .OrderBy(s => s.StartDate)
            .ToListAsync(cancellationToken);
        result.LocalSprints = localSprints.Count;
        if (localSprints.Count == 0)
        {
            return result;
        }

        List<JiraAgileSprint> jiraSprints;
        try
        {
            jiraSprints = await _agile.GetBoardSprintsAsync(
                userName, team.JiraBoardId.Value, state: null, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Sprint discovery failed for team {TeamId} on board {BoardId}",
                teamId, team.JiraBoardId);
            result.Error = $"Board {team.JiraBoardId} sprint fetch failed: {ex.Message}";
            return result;
        }

        var matches = SprintJiraMatcher.Match(localSprints, jiraSprints);
        foreach (var match in matches)
        {
            switch (match.Kind)
            {
                case SprintMatchKind.ByName:
                    result.MatchedByName++;
                    break;
                case SprintMatchKind.ByDates:
                    result.MatchedByDates++;
                    break;
                default:
                    result.Unmatched.Add(match.Local.Name);
                    continue;
            }

            match.Local.JiraSprintId = match.Jira!.Id;
            match.Local.JiraState = match.Jira.State;
            match.Local.JiraStartDate = match.Jira.StartDate;
            match.Local.JiraCompleteDate = match.Jira.CompleteDate;
        }

        await db.SaveChangesAsync(cancellationToken);
        Log.Information(
            "Sprint discovery for team {TeamName}: {ByName} by name, {ByDates} by dates, {Unmatched} unmatched of {Total}",
            team.Name, result.MatchedByName, result.MatchedByDates, result.Unmatched.Count, result.LocalSprints);
        return result;
    }
}
