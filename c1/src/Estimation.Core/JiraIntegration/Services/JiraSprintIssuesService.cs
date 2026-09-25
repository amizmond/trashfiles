using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Estimation.Core.JiraIntegration.Client;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.JiraIntegration.Services;

public enum SprintLookupStatus
{
    Found,

    NotFound,

    AmbiguousName,

    Error,
}

public class SprintIssuesResult
{
    public SprintLookupStatus Status { get; set; }

    public List<JiraIssueResponse> Issues { get; set; } = [];

    public string? ErrorMessage { get; set; }
}

public interface IJiraSprintIssuesService
{
    Task<SprintIssuesResult> GetSprintIssuesAsync(string userName, string sprintName);
}

public class JiraSprintIssuesService : IJiraSprintIssuesService
{
    private static readonly Regex UnknownProjectPattern = new(
        "The value '([^']+)' does not exist for the field 'project'",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IJiraIssueService _jira;

    public JiraSprintIssuesService(
        IDbContextFactory<EstimationDbContext> ctx,
        IJiraIssueService jira)
    {
        _ctx = ctx;
        _jira = jira;
    }

    public async Task<SprintIssuesResult> GetSprintIssuesAsync(string userName, string sprintName)
    {
        if (string.IsNullOrWhiteSpace(sprintName))
        {
            return new SprintIssuesResult
            {
                Status = SprintLookupStatus.Error,
                ErrorMessage = "Sprint name is empty.",
            };
        }

        var projectKeys = await GetConfiguredProjectKeysAsync();
        if (projectKeys.Count == 0)
        {
            Log.Warning("No Jira project keys configured for sync; sprint search runs unrestricted");
        }

        var name = sprintName.Trim();
        while (true)
        {
            var jql = BuildJql(name, projectKeys);
            try
            {
                var issues = await _jira.SearchIssuesAsync(userName, jql);
                return new SprintIssuesResult { Status = SprintLookupStatus.Found, Issues = issues };
            }
            catch (JiraSearchException ex)
            {
                var rejected = RejectedProjectKeys(ex, projectKeys);
                if (rejected.Count == 0 || rejected.Count >= projectKeys.Count)
                {
                    return ClassifySearchFailure(sprintName, ex);
                }

                Log.Information("Sprint search for '{SprintName}' skips Jira projects the user cannot see: {Keys}",
                    sprintName, string.Join(", ", rejected));
                projectKeys = projectKeys.Where(k => !rejected.Contains(k)).ToList();
            }
        }
    }

    private static HashSet<string> RejectedProjectKeys(JiraSearchException ex, List<string> projectKeys)
    {
        if (ex.StatusCode != HttpStatusCode.BadRequest)
        {
            return [];
        }

        var configured = projectKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return UnknownProjectPattern.Matches(ExtractErrorMessages(ex.ResponseBody))
            .Select(m => m.Groups[1].Value)
            .Where(configured.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<List<string>> GetConfiguredProjectKeysAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var syncKeys = await db.JiraSyncKeys.Select(k => k.JiraKey).ToListAsync();
        var artKeys = await db.CapitalProjectJiraKeys.Select(k => k.JiraKey).ToListAsync();
        return syncKeys.Concat(artKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SprintIssuesResult ClassifySearchFailure(string sprintName, JiraSearchException ex)
    {
        var message = ExtractErrorMessages(ex.ResponseBody);

        if (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            if (message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Sprint '{SprintName}' not found on Jira: {Message}", sprintName, message);
                return new SprintIssuesResult { Status = SprintLookupStatus.NotFound, ErrorMessage = message };
            }
            if (message.Contains("matches several", StringComparison.OrdinalIgnoreCase)
                || message.Contains("more than one", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Sprint '{SprintName}' is ambiguous on Jira: {Message}", sprintName, message);
                return new SprintIssuesResult { Status = SprintLookupStatus.AmbiguousName, ErrorMessage = message };
            }
        }

        Log.Warning(ex, "Sprint issue search failed for '{SprintName}'", sprintName);
        return new SprintIssuesResult { Status = SprintLookupStatus.Error, ErrorMessage = message };
    }

    private static string BuildJql(string sprintName, List<string> projectKeys)
    {
        var sb = new StringBuilder();
        sb.Append("sprint = ").Append(Quote(sprintName));
        if (projectKeys.Count > 0)
        {
            sb.Append(" AND project in (").Append(string.Join(", ", projectKeys.Select(Quote))).Append(')');
        }
        sb.Append(" ORDER BY assignee ASC, status ASC");
        return sb.ToString();
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string ExtractErrorMessages(string body)
    {
        try
        {
            var json = JsonNode.Parse(body);
            if (json?["errorMessages"] is JsonArray arr)
            {
                var messages = arr.Select(m => m?.GetValue<string>())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Cast<string>()
                    .ToList();
                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }
        }
        catch
        {
            // Body is not the standard Jira error JSON — fall back to the raw text.
        }
        return body;
    }
}
