using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraLogic;

public class JiraIssueService : IJiraIssueService
{
    private readonly IJiraAuthService _authService;
    private readonly JiraOAuthSettings _settings;

    public JiraIssueService(IJiraAuthService authService, IOptions<JiraOAuthSettings> settings)
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
            fields["description"] = request.Description;

        if (!string.IsNullOrEmpty(request.FeatureName) && !string.IsNullOrEmpty(_settings.FeatureNameCustomFieldId))
            fields[_settings.FeatureNameCustomFieldId] = request.FeatureName;

        if (request.Labels is { Count: > 0 })
        {
            var labelsArray = new JsonArray();
            foreach (var label in request.Labels)
                labelsArray.Add(JsonValue.Create(label));
            fields["labels"] = labelsArray;
        }

        if (!string.IsNullOrEmpty(request.BusinessOutcomeKey) && !string.IsNullOrEmpty(_settings.BusinessOutcomeCustomFieldId))
            fields[_settings.BusinessOutcomeCustomFieldId] = request.BusinessOutcomeKey;

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
            throw new Exception($"Jira create failed ({response.StatusCode}): {responseBody}");

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
            fields["description"] = request.Description;

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
            return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Jira GET failed ({response.StatusCode}): {body}");
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(responseBody);

        return new JiraIssueResponse
        {
            Key = json?["key"]?.GetValue<string>() ?? issueKey,
            Summary = json?["fields"]?["summary"]?.GetValue<string>(),
            Description = json?["fields"]?["description"]?.GetValue<string>(),
            Status = json?["fields"]?["status"]?["name"]?.GetValue<string>(),
            IssueType = json?["fields"]?["issuetype"]?["name"]?.GetValue<string>(),
        };
    }
}
