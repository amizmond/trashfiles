using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraIntegration.Client;

public class JiraAgileService : IJiraAgileService
{
    private readonly IJiraAuthService _authService;
    private readonly JiraSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;

    public JiraAgileService(
        IJiraAuthService authService,
        IOptions<JiraSettings> settings,
        IHttpClientFactory httpClientFactory)
    {
        _authService = authService;
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<JiraBoard>> GetBoardsAsync(
        string userName, string? projectKeyOrId = null, int maxTotal = 200, CancellationToken cancellationToken = default)
    {
        var results = new List<JiraBoard>();
        var startAt = 0;
        const int pageSize = 50;

        while (results.Count < maxTotal)
        {
            var url = $"{BaseUrl()}/rest/agile/1.0/board?type=scrum&startAt={startAt}&maxResults={pageSize}";
            if (!string.IsNullOrWhiteSpace(projectKeyOrId))
            {
                url += $"&projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}";
            }

            var json = await GetJsonAsync(userName, url, cancellationToken);
            if (json?["values"] is not JsonArray values)
            {
                throw new JiraAgileException("Jira board list response had no 'values' array.");
            }

            foreach (var v in values)
            {
                var id = v?["id"]?.GetValue<int>();
                if (id is null)
                {
                    continue;
                }
                results.Add(new JiraBoard
                {
                    Id = id.Value,
                    Name = v?["name"]?.GetValue<string>() ?? string.Empty,
                    Type = v?["type"]?.GetValue<string>(),
                });
            }

            if (json["isLast"]?.GetValue<bool>() ?? values.Count < pageSize)
            {
                break;
            }
            startAt += pageSize;
        }

        return results;
    }

    public async Task<List<JiraAgileSprint>> GetBoardSprintsAsync(
        string userName, int boardId, string? state = null, int maxTotal = 2000, CancellationToken cancellationToken = default)
    {
        var results = new List<JiraAgileSprint>();
        var startAt = 0;
        const int pageSize = 50;

        while (results.Count < maxTotal)
        {
            var url = $"{BaseUrl()}/rest/agile/1.0/board/{boardId}/sprint?startAt={startAt}&maxResults={pageSize}";
            if (!string.IsNullOrWhiteSpace(state))
            {
                url += $"&state={Uri.EscapeDataString(state)}";
            }

            var json = await GetJsonAsync(userName, url, cancellationToken);
            if (json?["values"] is not JsonArray values)
            {
                throw new JiraAgileException($"Jira sprint list response for board {boardId} had no 'values' array.");
            }

            foreach (var v in values)
            {
                var id = v?["id"]?.GetValue<int>();
                if (id is null)
                {
                    continue;
                }
                results.Add(new JiraAgileSprint
                {
                    Id = id.Value,
                    Name = v?["name"]?.GetValue<string>() ?? string.Empty,
                    State = v?["state"]?.GetValue<string>(),
                    StartDate = ParseInstant(v?["startDate"]),
                    EndDate = ParseInstant(v?["endDate"]),
                    CompleteDate = ParseInstant(v?["completeDate"]),
                });
            }

            if (json["isLast"]?.GetValue<bool>() ?? values.Count < pageSize)
            {
                break;
            }
            startAt += pageSize;
        }

        return results;
    }

    public async Task<JiraSprintReport> GetSprintReportAsync(
        string userName, int boardId, int sprintId, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl()}/rest/greenhopper/1.0/rapid/charts/sprintreport?rapidViewId={boardId}&sprintId={sprintId}";
        var json = await GetJsonAsync(userName, url, cancellationToken);

        // Shape validation is deliberate: a drifted payload must fail loudly, never write
        // plausible-but-empty metrics.
        if (json?["contents"] is not JsonObject contents)
        {
            throw new JiraAgileException($"Sprint report for sprint {sprintId} had no 'contents' object.");
        }
        if (contents["completedIssues"] is not JsonArray completed
            || contents["issuesNotCompletedInCurrentSprint"] is not JsonArray notCompleted)
        {
            throw new JiraAgileException(
                $"Sprint report for sprint {sprintId} is missing the completed/not-completed issue arrays.");
        }

        var report = new JiraSprintReport
        {
            SprintId = json["sprint"]?["id"]?.GetValue<int>() ?? sprintId,
            SprintName = json["sprint"]?["name"]?.GetValue<string>(),
            SprintState = json["sprint"]?["state"]?.GetValue<string>(),
            CompletedIssues = ParseReportIssues(completed),
            NotCompletedIssues = ParseReportIssues(notCompleted),
            PuntedIssues = ParseReportIssues(contents["puntedIssues"] as JsonArray),
            CompletedInAnotherSprintIssues = ParseReportIssues(contents["issuesCompletedInAnotherSprint"] as JsonArray),
        };

        if (contents["issueKeysAddedDuringSprint"] is JsonObject addedKeys)
        {
            foreach (var kv in addedKeys)
            {
                report.AddedDuringSprintKeys.Add(kv.Key);
            }
        }

        return report;
    }

    public async Task<JiraAgileProbeResult> ProbeAsync(string userName, CancellationToken cancellationToken = default)
    {
        List<JiraBoard> boards;
        try
        {
            boards = await GetBoardsAsync(userName, projectKeyOrId: null, maxTotal: 10, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Jira Agile API probe failed at the board-list call");
            return new JiraAgileProbeResult
            {
                AgileApiAvailable = false,
                SprintReportAvailable = null,
                Message = $"Agile board API not reachable: {Shorten(ex.Message)}",
            };
        }

        if (boards.Count == 0)
        {
            return new JiraAgileProbeResult
            {
                AgileApiAvailable = true,
                SprintReportAvailable = null,
                Message = "Agile API is reachable but no scrum boards are visible to the service account.",
            };
        }

        foreach (var board in boards)
        {
            List<JiraAgileSprint> closed;
            try
            {
                closed = await GetBoardSprintsAsync(userName, board.Id, state: "closed", maxTotal: 1, cancellationToken);
            }
            catch (Exception ex)
            {
                // Boards that do not support sprints answer 400; try the next board.
                Log.Debug(ex, "Probe skipped board {BoardId} ({BoardName})", board.Id, board.Name);
                continue;
            }

            if (closed.Count == 0)
            {
                continue;
            }

            try
            {
                var report = await GetSprintReportAsync(userName, board.Id, closed[0].Id, cancellationToken);
                return new JiraAgileProbeResult
                {
                    AgileApiAvailable = true,
                    SprintReportAvailable = true,
                    Message = $"OK — validated with sprint \"{report.SprintName ?? closed[0].Name}\" "
                              + $"on board \"{board.Name}\" ({report.CompletedIssues.Count} completed, "
                              + $"{report.NotCompletedIssues.Count} not completed).",
                };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Sprint report probe failed for board {BoardId}, sprint {SprintId}",
                    board.Id, closed[0].Id);
                return new JiraAgileProbeResult
                {
                    AgileApiAvailable = true,
                    SprintReportAvailable = false,
                    Message = $"Agile API works, but the sprint report endpoint failed: {Shorten(ex.Message)}",
                };
            }
        }

        return new JiraAgileProbeResult
        {
            AgileApiAvailable = true,
            SprintReportAvailable = null,
            Message = "Agile API works, but no closed sprint was found on the first boards to probe the sprint report with.",
        };
    }

    private static List<JiraSprintReportIssue> ParseReportIssues(JsonArray? issues)
    {
        var result = new List<JiraSprintReportIssue>();
        if (issues is null)
        {
            return result;
        }

        foreach (var issue in issues)
        {
            var key = issue?["key"]?.GetValue<string>();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }
            result.Add(new JiraSprintReportIssue
            {
                Key = key,
                TypeName = issue?["typeName"]?.GetValue<string>(),
                StatusName = issue?["statusName"]?.GetValue<string>() ?? issue?["status"]?["name"]?.GetValue<string>(),
                Summary = issue?["summary"]?.GetValue<string>(),
                EstimateAtStart = JiraIssueParser.ParseJiraDecimal(issue?["estimateStatistic"]?["statFieldValue"]?["value"]),
                EstimateAtEnd = JiraIssueParser.ParseJiraDecimal(issue?["currentEstimateStatistic"]?["statFieldValue"]?["value"]),
            });
        }
        return result;
    }

    private async Task<JsonNode?> GetJsonAsync(string userName, string url, CancellationToken cancellationToken)
    {
        var token = await _authService.GetStoredTokenAsync(userName)
            ?? throw new InvalidOperationException("Not authenticated to Jira. Please log in first.");

        var httpClient = _httpClientFactory.CreateClient(JiraIssueService.HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, token.AccessToken));

        var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new JiraAgileException(
                $"Jira agile GET failed ({(int)response.StatusCode} {response.StatusCode}).",
                response.StatusCode, body);
        }
        return JsonNode.Parse(body);
    }

    /// <summary>Agile timestamps are ISO-8601 with offset; stored as UTC.</summary>
    private static DateTime? ParseInstant(JsonNode? node)
    {
        var s = node?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        return DateTimeOffset.TryParse(s, out var dto) ? dto.UtcDateTime : null;
    }

    private string BaseUrl()
    {
        return _settings.Url.TrimEnd('/');
    }

    private static string Shorten(string message)
    {
        return message.Length <= 300 ? message : message[..300];
    }
}
