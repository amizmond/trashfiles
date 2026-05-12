using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraLogic;

public class JiraIssueService : IJiraIssueService
{
    private readonly IJiraAuthService _authService;
    private readonly JiraSettings _settings;

    public JiraIssueService(IJiraAuthService authService, IOptions<JiraSettings> settings)
    {
        _authService = authService;
        _settings = settings.Value;
    }

    public async Task<string> CreateIssueAsync(string userName, JiraCreateIssueRequest request)
    {
        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira. Please log in first.");

        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/issue";

        var fields = new JsonObject
        {
            ["project"] = new JsonObject { ["key"] = request.ProjectKey },
            ["issuetype"] = new JsonObject { ["name"] = request.IssueType },
            ["summary"] = request.Summary,
        };

        if (!string.IsNullOrEmpty(request.Description))
        {
            fields["description"] = request.Description;
        }

        if (!string.IsNullOrEmpty(request.FeatureName) && !string.IsNullOrEmpty(_settings.FeatureNameCustomFieldId))
        {
            fields[_settings.FeatureNameCustomFieldId] = request.FeatureName;
        }

        if (request.Labels is { Count: > 0 })
        {
            var labelsArray = new JsonArray();
            foreach (var label in request.Labels)
            {
                labelsArray.Add(JsonValue.Create(label));
            }
            fields["labels"] = labelsArray;
        }

        if (!string.IsNullOrEmpty(request.BusinessOutcomeKey) && !string.IsNullOrEmpty(_settings.BusinessOutcomeCustomFieldId))
        {
            fields[_settings.BusinessOutcomeCustomFieldId] = request.BusinessOutcomeKey;
        }

        if (request.TargetStart.HasValue && !string.IsNullOrEmpty(_settings.TargetStartCustomFieldId))
        {
            fields[_settings.TargetStartCustomFieldId] = request.TargetStart.Value.ToString("yyyy-MM-dd");
        }

        if (request.TargetEnd.HasValue && !string.IsNullOrEmpty(_settings.TargetEndCustomFieldId))
        {
            fields[_settings.TargetEndCustomFieldId] = request.TargetEnd.Value.ToString("yyyy-MM-dd");
        }

        if (request.StoryPoints.HasValue && !string.IsNullOrEmpty(_settings.StoryPointsCustomFieldId))
        {
            fields[_settings.StoryPointsCustomFieldId] = request.StoryPoints.Value;
        }

        var body = new JsonObject { ["fields"] = fields };
        var json = body.ToJsonString();

        Log.Debug("Creating Jira issue: {Json}", json);

        using var httpClient = new HttpClient();
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Add("Authorization",
            _authService.BuildOAuthHeader("POST", url, token.AccessToken));

        var response = await httpClient.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Jira create failed ({response.StatusCode}): {responseBody}");
        }

        var result = JsonNode.Parse(responseBody);
        var issueKey = result?["key"]?.GetValue<string>()
                       ?? throw new Exception("Jira response did not contain issue key.");

        Log.Information("Created Jira issue {IssueKey} for user {UserName}", issueKey, userName);
        return issueKey;
    }

    public async Task UpdateIssueAsync(string userName, string issueKey, JiraUpdateIssueRequest request)
    {
        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira. Please log in first.");

        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}";

        var fields = new JsonObject
        {
            ["summary"] = request.Summary,
        };

        if (request.Description is not null)
        {
            fields["description"] = request.Description;
        }

        if (!string.IsNullOrEmpty(request.FeatureName) && !string.IsNullOrEmpty(_settings.FeatureNameCustomFieldId))
        {
            fields[_settings.FeatureNameCustomFieldId] = request.FeatureName;
        }

        if (request.Labels is not null)
        {
            var labelsArray = new JsonArray();
            foreach (var label in request.Labels)
            {
                labelsArray.Add(JsonValue.Create(label));
            }
            fields["labels"] = labelsArray;
        }

        if (!string.IsNullOrEmpty(request.BusinessOutcomeKey) && !string.IsNullOrEmpty(_settings.BusinessOutcomeCustomFieldId))
        {
            fields[_settings.BusinessOutcomeCustomFieldId] = request.BusinessOutcomeKey;
        }

        if (!string.IsNullOrEmpty(_settings.TargetStartCustomFieldId))
        {
            fields[_settings.TargetStartCustomFieldId] = request.TargetStart?.ToString("yyyy-MM-dd");
        }

        if (!string.IsNullOrEmpty(_settings.TargetEndCustomFieldId))
        {
            fields[_settings.TargetEndCustomFieldId] = request.TargetEnd?.ToString("yyyy-MM-dd");
        }

        if (!string.IsNullOrEmpty(_settings.StoryPointsCustomFieldId))
        {
            fields[_settings.StoryPointsCustomFieldId] = request.StoryPoints.HasValue
                ? JsonValue.Create(request.StoryPoints.Value)
                : null;
        }

        var body = new JsonObject { ["fields"] = fields };
        var json = body.ToJsonString();

        Log.Debug("Updating Jira issue {IssueKey}: {Json}", issueKey, json);

        using var httpClient = new HttpClient();
        var httpRequest = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Add("Authorization",
            _authService.BuildOAuthHeader("PUT", url, token.AccessToken));

        var response = await httpClient.SendAsync(httpRequest);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Jira update failed ({response.StatusCode}): {responseBody}");
        }

        Log.Information("Updated Jira issue {IssueKey} for user {UserName}", issueKey, userName);
    }

    public async Task<JiraIssueResponse?> GetIssueAsync(string userName, string issueKey)
    {
        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira. Please log in first.");

        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}";

        using var httpClient = new HttpClient();
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Add("Authorization",
            _authService.BuildOAuthHeader("GET", url, token.AccessToken));

        var response = await httpClient.SendAsync(httpRequest);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Jira GET failed ({response.StatusCode}): {body}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(responseBody);

        var featureName = json?["fields"]?[_settings.FeatureNameCustomFieldId]?.GetValue<string>();
        var parentLink = json?["fields"]?[_settings.BusinessOutcomeCustomFieldId]?.GetValue<string>();

        var labels = json?["fields"]?["labels"]?.AsArray()
            .Select(l => l?.GetValue<string>())
            .Where(l => !string.IsNullOrEmpty(l))
            .Cast<string>()
            .ToList();

        DateTime? updated = null;
        var updatedStr = json?["fields"]?["updated"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(updatedStr) && DateTime.TryParse(updatedStr, out var parsedUpdated))
        {
            updated = parsedUpdated;
        }

        return new JiraIssueResponse
        {
            Key = json?["key"]?.GetValue<string>() ?? issueKey,
            Summary = json?["fields"]?["summary"]?.GetValue<string>(),
            Description = json?["fields"]?["description"]?.GetValue<string>(),
            Status = json?["fields"]?["status"]?["name"]?.GetValue<string>(),
            IssueType = json?["fields"]?["issuetype"]?["name"]?.GetValue<string>(),
            FeatureName = featureName,
            Labels = labels,
            ParentLink = parentLink,
            Updated = updated,
            TargetStart = ParseJiraDate(json?["fields"]?[_settings.TargetStartCustomFieldId]),
            TargetEnd = ParseJiraDate(json?["fields"]?[_settings.TargetEndCustomFieldId]),
            StoryPoints = ParseJiraInt(json?["fields"]?[_settings.StoryPointsCustomFieldId]),
            GfedTeam = ParseJiraMultiOption(json?["fields"]?[_settings.GfedTeamCustomFieldId]),
            PlanningIncrement = ParseJiraSingleOption(json?["fields"]?[_settings.PlanningIncrementCustomFieldId]),
        };
    }

    private static DateTime? ParseJiraDate(JsonNode? node)
    {
        var s = node?.GetValue<string>();
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }
        return DateTime.TryParse(s, out var dt) ? dt : null;
    }

    private static int? ParseJiraInt(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        try
        {
            // Story points commonly comes back as a number (double in Jira); coerce to int.
            return (int)Math.Round(node.GetValue<double>());
        }
        catch
        {
            var s = node.GetValue<string>();
            return int.TryParse(s, out var i) ? i : null;
        }
    }

    private static string? ParseJiraSingleOption(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject obj)
        {
            var v = obj["value"]?.GetValue<string>() ?? obj["name"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        try
        {
            var s = node.GetValue<string>();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseJiraMultiOption(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonArray arr)
        {
            var values = arr
                .Select(n => n is JsonObject o
                    ? (o["value"]?.GetValue<string>() ?? o["name"]?.GetValue<string>())
                    : SafeString(n))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToList();
            return values.Count == 0 ? null : string.Join(", ", values);
        }

        return ParseJiraSingleOption(node);
    }

    private static string? SafeString(JsonNode? node)
    {
        try
        {
            return node?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql)
    {
        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira. Please log in first.");

        var results = new List<JiraIssueResponse>();
        var startAt = 0;
        const int pageSize = 50;
        var baseUrl = $"{_settings.Url.TrimEnd('/')}/rest/api/2/search";
        var encodedJql = Uri.EscapeDataString(jql);

        while (true)
        {
            var url = $"{baseUrl}?jql={encodedJql}&fields=summary,description,status,issuetype,labels,updated,{_settings.FeatureNameCustomFieldId},{_settings.BusinessOutcomeCustomFieldId},{_settings.TargetStartCustomFieldId},{_settings.TargetEndCustomFieldId},{_settings.StoryPointsCustomFieldId},{_settings.GfedTeamCustomFieldId},{_settings.PlanningIncrementCustomFieldId}&maxResults={pageSize}&startAt={startAt}";

            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization",
                _authService.BuildOAuthHeader("GET", url, token.AccessToken));

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"Jira search failed ({response.StatusCode}): {body}");
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(responseBody);
            var total = json?["total"]?.GetValue<int>() ?? 0;

            if (json?["issues"] is JsonArray issues)
            {
                foreach (var issue in issues)
                {
                    var featureName = issue?["fields"]?[_settings.FeatureNameCustomFieldId]?.GetValue<string>();
                    var parentLink = issue?["fields"]?[_settings.BusinessOutcomeCustomFieldId]?.GetValue<string>();

                    var labels = issue?["fields"]?["labels"]?.AsArray()
                        .Select(l => l?.GetValue<string>())
                        .Where(l => !string.IsNullOrEmpty(l))
                        .Cast<string>()
                        .ToList();

                    DateTime? updated = null;
                    var updatedStr = issue?["fields"]?["updated"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(updatedStr) && DateTime.TryParse(updatedStr, out var parsedUpdated))
                    {
                        updated = parsedUpdated;
                    }

                    results.Add(new JiraIssueResponse
                    {
                        Key = issue?["key"]?.GetValue<string>() ?? "",
                        Summary = issue?["fields"]?["summary"]?.GetValue<string>(),
                        Description = issue?["fields"]?["description"]?.GetValue<string>(),
                        Status = issue?["fields"]?["status"]?["name"]?.GetValue<string>(),
                        IssueType = issue?["fields"]?["issuetype"]?["name"]?.GetValue<string>(),
                        FeatureName = featureName,
                        Labels = labels,
                        ParentLink = parentLink,
                        Updated = updated,
                        TargetStart = ParseJiraDate(issue?["fields"]?[_settings.TargetStartCustomFieldId]),
                        TargetEnd = ParseJiraDate(issue?["fields"]?[_settings.TargetEndCustomFieldId]),
                        StoryPoints = ParseJiraInt(issue?["fields"]?[_settings.StoryPointsCustomFieldId]),
                        GfedTeam = ParseJiraMultiOption(issue?["fields"]?[_settings.GfedTeamCustomFieldId]),
                        PlanningIncrement = ParseJiraSingleOption(issue?["fields"]?[_settings.PlanningIncrementCustomFieldId]),
                    });
                }
            }

            startAt += pageSize;
            if (startAt >= total)
            {
                break;
            }
        }

        Log.Information("Jira search returned {Count} issues for JQL: {Jql}", results.Count, jql);
        return results;
    }

    public async Task<List<JiraIssueResponse>> GetIssuesByKeysAsync(string userName, List<string> issueKeys)
    {
        if (issueKeys.Count == 0)
        {
            return [];
        }

        const int batchSize = 100;
        var results = new List<JiraIssueResponse>();

        foreach (var batch in issueKeys.Chunk(batchSize))
        {
            var keyList = string.Join(", ", batch.Select(k => $"\"{k}\""));
            var jql = $"key in ({keyList})";
            var batchResults = await SearchIssuesAsync(userName, jql);
            results.AddRange(batchResults);
        }

        return results;
    }
}
