using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraIntegration.Client;

public class JiraIssueService : IJiraIssueService
{
    public const string HttpClientName = "jira";

    private readonly IJiraAuthService _authService;
    private readonly JiraSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JiraIssueParser _parser;

    public JiraIssueService(
        IJiraAuthService authService,
        IOptions<JiraSettings> settings,
        IHttpClientFactory httpClientFactory)
    {
        _authService = authService;
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _parser = new JiraIssueParser(_settings);
    }

    public async Task<string> CreateIssueAsync(string userName, JiraCreateIssueRequest request)
    {
        var token = await GetTokenOrThrowAsync(userName);
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

        if (!string.IsNullOrEmpty(request.RagExplain) && !string.IsNullOrEmpty(_settings.RagExplainCustomFieldId))
        {
            fields[_settings.RagExplainCustomFieldId] = request.RagExplain;
        }

        if (!string.IsNullOrEmpty(request.AcceptanceCriteria) && !string.IsNullOrEmpty(_settings.AcceptanceCriteriaCustomFieldId))
        {
            fields[_settings.AcceptanceCriteriaCustomFieldId] = request.AcceptanceCriteria;
        }

        if (!string.IsNullOrEmpty(request.NavigatorId) && !string.IsNullOrEmpty(_settings.NavigatorIdCustomFieldId))
        {
            fields[_settings.NavigatorIdCustomFieldId] = request.NavigatorId;
        }

        if (request.Labels is { Count: > 0 })
        {
            fields["labels"] = ToJsonArray(request.Labels);
        }

        if (!string.IsNullOrEmpty(_settings.ParentLinkCustomFieldId))
        {
            var parentKey = request.ParentJiraKey ?? request.BusinessOutcomeKey;
            if (!string.IsNullOrEmpty(parentKey))
            {
                fields[_settings.ParentLinkCustomFieldId] = parentKey;
            }
        }

        if (!string.IsNullOrEmpty(request.PlanningIncrement) && !string.IsNullOrEmpty(_settings.PlanningIncrementCustomFieldId))
        {
            fields[_settings.PlanningIncrementCustomFieldId] = new JsonArray
            {
                new JsonObject { ["value"] = request.PlanningIncrement },
            };
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

        if (request.GfedTeams is { Count: > 0 } && !string.IsNullOrEmpty(_settings.GfedTeamCustomFieldId))
        {
            fields[_settings.GfedTeamCustomFieldId] = ToValueObjectArray(request.GfedTeams);
        }

        var body = new JsonObject { ["fields"] = fields };
        var json = body.ToJsonString();

        Log.Debug("Creating Jira issue: {Json}", json);

        var responseBody = await SendJsonAsync(HttpMethod.Post, url, json, token.AccessToken);
        var result = JsonNode.Parse(responseBody);
        var issueKey = result?["key"]?.GetValue<string>()
                       ?? throw new Exception("Jira response did not contain issue key.");

        Log.Information("Created Jira issue {IssueKey} for user {UserName}", issueKey, userName);
        return issueKey;
    }

    public async Task UpdateIssueAsync(string userName, string issueKey, JiraUpdateIssueRequest request)
    {
        var token = await GetTokenOrThrowAsync(userName);
        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}";

        var fields = new JsonObject();

        TryWrite(fields, JiraUpdateFields.Summary, "summary", request.Summary, request);
        TryWrite(fields, JiraUpdateFields.Description, "description", request.Description, request);

        if (!string.IsNullOrEmpty(_settings.FeatureNameCustomFieldId))
        {
            TryWrite(fields, JiraUpdateFields.FeatureName, _settings.FeatureNameCustomFieldId, request.FeatureName, request);
        }

        if (!string.IsNullOrEmpty(_settings.RagExplainCustomFieldId))
        {
            TryWrite(fields, JiraUpdateFields.RagExplain, _settings.RagExplainCustomFieldId, request.RagExplain, request);
        }

        if (!string.IsNullOrEmpty(_settings.AcceptanceCriteriaCustomFieldId))
        {
            TryWrite(fields, JiraUpdateFields.AcceptanceCriteria, _settings.AcceptanceCriteriaCustomFieldId, request.AcceptanceCriteria, request);
        }

        if (!string.IsNullOrEmpty(_settings.NavigatorIdCustomFieldId))
        {
            TryWrite(fields, JiraUpdateFields.NavigatorId, _settings.NavigatorIdCustomFieldId, request.NavigatorId, request);
        }

        if (ShouldInclude(request, JiraUpdateFields.Labels, request.Labels is not null))
        {
            fields["labels"] = request.Labels is null ? null : ToJsonArray(request.Labels);
        }

        if (!string.IsNullOrEmpty(_settings.ParentLinkCustomFieldId))
        {
            var parentKey = request.ParentJiraKey ?? request.BusinessOutcomeKey;
            if (ShouldInclude(request, JiraUpdateFields.ParentLink, !string.IsNullOrEmpty(parentKey)))
            {
                fields[_settings.ParentLinkCustomFieldId] = parentKey;
            }
        }

        if (!string.IsNullOrEmpty(_settings.PlanningIncrementCustomFieldId)
            && ShouldInclude(request, JiraUpdateFields.PlanningIncrement, !string.IsNullOrEmpty(request.PlanningIncrement)))
        {
            fields[_settings.PlanningIncrementCustomFieldId] = string.IsNullOrEmpty(request.PlanningIncrement)
                ? null
                : new JsonArray { new JsonObject { ["value"] = request.PlanningIncrement } };
        }

        if (!string.IsNullOrEmpty(_settings.TargetStartCustomFieldId)
            && ShouldInclude(request, JiraUpdateFields.TargetStart, request.TargetStart.HasValue))
        {
            fields[_settings.TargetStartCustomFieldId] = request.TargetStart?.ToString("yyyy-MM-dd");
        }

        if (!string.IsNullOrEmpty(_settings.TargetEndCustomFieldId)
            && ShouldInclude(request, JiraUpdateFields.TargetEnd, request.TargetEnd.HasValue))
        {
            fields[_settings.TargetEndCustomFieldId] = request.TargetEnd?.ToString("yyyy-MM-dd");
        }

        if (!string.IsNullOrEmpty(_settings.StoryPointsCustomFieldId)
            && ShouldInclude(request, JiraUpdateFields.StoryPoints, request.StoryPoints.HasValue))
        {
            fields[_settings.StoryPointsCustomFieldId] = request.StoryPoints.HasValue
                ? JsonValue.Create(request.StoryPoints.Value)
                : null;
        }

        if (!string.IsNullOrEmpty(_settings.GfedTeamCustomFieldId)
            && ShouldInclude(request, JiraUpdateFields.GfedTeam, request.GfedTeams is { Count: > 0 }))
        {
            fields[_settings.GfedTeamCustomFieldId] = request.GfedTeams is { Count: > 0 }
                ? ToValueObjectArray(request.GfedTeams)
                : null;
        }

        if (ShouldInclude(request, JiraUpdateFields.Assignee, !string.IsNullOrEmpty(request.AssigneeUserName)))
        {
            // Jira Server/DC identifies users by login name; a null assignee unassigns the issue.
            fields["assignee"] = string.IsNullOrEmpty(request.AssigneeUserName)
                ? null
                : new JsonObject { ["name"] = request.AssigneeUserName };
        }

        if (fields.Count == 0)
        {
            Log.Debug("Update of Jira issue {IssueKey} had no fields to write; skipping PUT", issueKey);
            return;
        }

        var body = new JsonObject { ["fields"] = fields };
        var json = body.ToJsonString();

        Log.Debug("Updating Jira issue {IssueKey}: {Json}", issueKey, json);

        await SendJsonAsync(HttpMethod.Put, url, json, token.AccessToken);
        Log.Information("Updated Jira issue {IssueKey} for user {UserName}", issueKey, userName);
    }

    public async Task UpdateIssueWithStatusAsync(
        string userName,
        string issueKey,
        JiraUpdateIssueRequest request,
        string? targetStatusName)
    {
        await UpdateIssueAsync(userName, issueKey, request);

        if (string.IsNullOrWhiteSpace(targetStatusName))
        {
            return;
        }

        await TransitionToStatusAsync(userName, issueKey, targetStatusName);
    }

    public async Task<JiraIssueResponse?> GetIssueAsync(string userName, string issueKey)
    {
        var token = await GetTokenOrThrowAsync(userName);
        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}";

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, token.AccessToken));

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
        if (json is null)
        {
            return null;
        }
        return _parser.Parse(json, issueKey);
    }

    public async Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql)
    {
        var page = await SearchCoreAsync(userName, jql, maxResults: null);
        return page.Issues;
    }

    public async Task<JiraSearchPage> SearchIssuesPageAsync(string userName, string jql, int maxResults)
    {
        return await SearchCoreAsync(userName, jql, maxResults);
    }

    private async Task<JiraSearchPage> SearchCoreAsync(string userName, string jql, int? maxResults)
    {
        var fieldsParam = Uri.EscapeDataString(JiraIssueFields.BuildFieldList(_settings));
        var (issues, total) = await SearchPagedAsync(
            userName, jql, fieldsParam, node => _parser.Parse(node), maxResults, CancellationToken.None);
        return new JiraSearchPage { Issues = issues, Total = total };
    }

    public async Task<List<SprintSnapshotIssue>> SearchSprintSnapshotAsync(
        string userName, string jql, CancellationToken cancellationToken = default)
    {
        var fieldsParam = Uri.EscapeDataString(JiraIssueFields.BuildSprintSnapshotFieldList(_settings));
        var (issues, _) = await SearchPagedAsync(
            userName, jql, fieldsParam, node => _parser.ParseSnapshot(node), maxResults: null, cancellationToken);
        return issues;
    }

    private async Task<(List<T> Issues, int Total)> SearchPagedAsync<T>(
        string userName,
        string jql,
        string fieldsParam,
        Func<JsonNode, T> parse,
        int? maxResults,
        CancellationToken cancellationToken)
    {
        var token = await GetTokenOrThrowAsync(userName);

        var results = new List<T>();
        var serverTotal = 0;
        var startAt = 0;
        const int pageSize = 50;
        var baseUrl = $"{_settings.Url.TrimEnd('/')}/rest/api/2/search";
        var encodedJql = Uri.EscapeDataString(jql);

        while (true)
        {
            var url = $"{baseUrl}?jql={encodedJql}&fields={fieldsParam}&maxResults={pageSize}&startAt={startAt}";

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Options.Set(JiraOAuthSigningHandler.SignerKey,
                () => _authService.BuildOAuthHeader("GET", url, token.AccessToken));

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new JiraSearchException(response.StatusCode, body);
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonNode.Parse(responseBody);
            serverTotal = json?["total"]?.GetValue<int>() ?? 0;

            if (json?["issues"] is JsonArray issues)
            {
                foreach (var issue in issues)
                {
                    if (issue is null)
                    {
                        continue;
                    }
                    results.Add(parse(issue));
                    if (maxResults.HasValue && results.Count >= maxResults.Value)
                    {
                        break;
                    }
                }
            }

            if (maxResults.HasValue && results.Count >= maxResults.Value)
            {
                break;
            }

            startAt += pageSize;
            if (startAt >= serverTotal)
            {
                break;
            }
        }

        Log.Information("Jira search returned {Count} of {Total} issues for JQL: {Jql}",
            results.Count, serverTotal, jql);
        return (results, serverTotal);
    }

    public async Task<List<JiraTransition>> GetTransitionsAsync(string userName, string issueKey)
    {
        var token = await GetTokenOrThrowAsync(userName);
        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}/transitions";

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, token.AccessToken));

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Jira GET transitions failed ({response.StatusCode}): {body}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(responseBody);

        var result = new List<JiraTransition>();
        if (json?["transitions"] is JsonArray arr)
        {
            foreach (var t in arr)
            {
                var id = t?["id"]?.GetValue<string>();
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }
                result.Add(new JiraTransition
                {
                    Id = id,
                    Name = t?["name"]?.GetValue<string>() ?? string.Empty,
                    ToStatusName = t?["to"]?["name"]?.GetValue<string>() ?? string.Empty,
                });
            }
        }
        return result;
    }

    public async Task<bool> TransitionToStatusAsync(string userName, string issueKey, string targetStatusName)
    {
        if (string.IsNullOrWhiteSpace(targetStatusName))
        {
            return false;
        }

        var current = await GetIssueAsync(userName, issueKey);
        if (current is null)
        {
            return false;
        }
        if (string.Equals(current.Status, targetStatusName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var transitions = await GetTransitionsAsync(userName, issueKey);
        var match = transitions.FirstOrDefault(t =>
            string.Equals(t.ToStatusName, targetStatusName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new Exception(
                $"No Jira transition available from '{current.Status}' to '{targetStatusName}' for issue {issueKey}.");
        }

        var token = await GetTokenOrThrowAsync(userName);
        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}/transitions";

        var body = new JsonObject
        {
            ["transition"] = new JsonObject { ["id"] = match.Id },
        };

        await SendJsonAsync(HttpMethod.Post, url, body.ToJsonString(), token.AccessToken);

        Log.Information("Transitioned Jira issue {IssueKey} to status {Status}", issueKey, targetStatusName);
        return true;
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

    private async Task<JiraToken> GetTokenOrThrowAsync(string userName)
    {
        return await _authService.GetStoredTokenAsync(userName)
            ?? throw new InvalidOperationException("Not authenticated to Jira. Please log in first.");
    }

    private async Task<string> SendJsonAsync(HttpMethod method, string url, string json, string accessToken)
    {
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader(method.Method, url, accessToken));

        var response = await httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Jira {method.Method} failed ({response.StatusCode}): {responseBody}");
        }
        return responseBody;
    }

    private static void TryWrite(
        JsonObject fields,
        string fieldKey,
        string jiraName,
        string? value,
        JiraUpdateIssueRequest request)
    {
        if (ShouldInclude(request, fieldKey, !string.IsNullOrEmpty(value)))
        {
            fields[jiraName] = value ?? string.Empty;
        }
    }

    private static bool ShouldInclude(JiraUpdateIssueRequest request, string fieldKey, bool valueIsPresent)
    {
        if (request.FieldsToUpdate is { Count: > 0 })
        {
            return request.FieldsToUpdate.Contains(fieldKey);
        }
        return valueIsPresent;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values)
        {
            arr.Add(JsonValue.Create(v));
        }
        return arr;
    }

    private static JsonArray ToValueObjectArray(IEnumerable<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values)
        {
            arr.Add(new JsonObject { ["value"] = v });
        }
        return arr;
    }
}
