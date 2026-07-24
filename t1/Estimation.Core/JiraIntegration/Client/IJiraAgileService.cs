using System.Net;

namespace Estimation.Core.JiraIntegration.Client;

public class JiraBoard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Board type: "scrum" or "kanban" (only scrum boards have sprints).</summary>
    public string? Type { get; set; }
}

public class JiraAgileSprint
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>"future", "active", or "closed".</summary>
    public string? State { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? CompleteDate { get; set; }
}

public class JiraSprintReportIssue
{
    public string Key { get; set; } = string.Empty;
    public string? TypeName { get; set; }
    public string? StatusName { get; set; }
    public string? Summary { get; set; }

    /// <summary>Estimate when the sprint started (or when the issue entered it).</summary>
    public decimal? EstimateAtStart { get; set; }

    /// <summary>Estimate at sprint completion.</summary>
    public decimal? EstimateAtEnd { get; set; }
}

/// <summary>
/// Jira's own sprint report (greenhopper), frozen by Jira at sprint close — the authoritative
/// committed/added/removed partition for historical sprints.
/// </summary>
public class JiraSprintReport
{
    public int SprintId { get; set; }
    public string? SprintName { get; set; }
    public string? SprintState { get; set; }
    public List<JiraSprintReportIssue> CompletedIssues { get; set; } = [];
    public List<JiraSprintReportIssue> NotCompletedIssues { get; set; } = [];

    /// <summary>Issues removed from the sprint before close ("punted").</summary>
    public List<JiraSprintReportIssue> PuntedIssues { get; set; } = [];

    public List<JiraSprintReportIssue> CompletedInAnotherSprintIssues { get; set; } = [];

    /// <summary>Keys of issues added after the sprint started.</summary>
    public HashSet<string> AddedDuringSprintKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class JiraAgileProbeResult
{
    public bool AgileApiAvailable { get; set; }

    /// <summary>Null when undecided (agile API down, or no closed sprint existed to probe with).</summary>
    public bool? SprintReportAvailable { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>Non-2xx or malformed response from the Jira Agile / greenhopper endpoints.</summary>
public class JiraAgileException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? ResponseBody { get; }

    public JiraAgileException(string message, HttpStatusCode? statusCode = null, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Jira Agile REST client (/rest/agile/1.0 board and sprint endpoints) plus the greenhopper
/// sprint report. Both are Server/DC APIs signed with the same per-user OAuth1 tokens as the
/// core issue client; the sprint-metrics engine calls them under the sync service account.
/// </summary>
public interface IJiraAgileService
{
    /// <summary>Lists scrum boards, optionally filtered by Jira project key; capped at <paramref name="maxTotal"/>.</summary>
    Task<List<JiraBoard>> GetBoardsAsync(string userName, string? projectKeyOrId = null, int maxTotal = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a board's sprints, optionally filtered by state ("active,future" or "closed");
    /// capped at <paramref name="maxTotal"/> (Jira orders them oldest first).
    /// </summary>
    Task<List<JiraAgileSprint>> GetBoardSprintsAsync(string userName, int boardId, string? state = null, int maxTotal = 2000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and parses the greenhopper sprint report. Throws <see cref="JiraAgileException"/>
    /// when the endpoint fails or the payload does not have the expected shape — never returns
    /// a silently empty report.
    /// </summary>
    Task<JiraSprintReport> GetSprintReportAsync(string userName, int boardId, int sprintId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies both capabilities against the live instance: the agile board API, and the
    /// sprint report shape (probed with the first closed sprint found on any scrum board).
    /// </summary>
    Task<JiraAgileProbeResult> ProbeAsync(string userName, CancellationToken cancellationToken = default);
}
