using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraLogic;

public class JiraMetadataService : IJiraMetadataService
{
    private readonly IJiraAuthService _authService;
    private readonly JiraOAuthSettings _settings;
    private readonly IMemoryCache _cache;

    public JiraMetadataService(
        IJiraAuthService authService,
        IOptions<JiraOAuthSettings> settings,
        IMemoryCache cache)
    {
        _authService = authService;
        _settings = settings.Value;
        _cache = cache;
    }

    public async Task<List<JiraProjectItem>> GetProjectsAsync(string userName)
    {
        const string cacheKey = "jira_projects";
        if (_cache.TryGetValue(cacheKey, out List<JiraProjectItem>? cached) && cached is not null)
            return cached;

        var token = await _authService.GetStoredTokenAsync(userName);
        if (token is null) return new();

        var url = $"{BaseUrl}/rest/api/2/project";

        try
        {
            var body = await GetJsonAsync("GET", url, token.AccessToken);
            var json = JsonNode.Parse(body)?.AsArray();
            if (json is null) return new();

            var projects = json.Select(p => new JiraProjectItem
            {
                Key = p?["key"]?.GetValue<string>() ?? "",
                Name = p?["name"]?.GetValue<string>() ?? ""
            }).Where(p => !string.IsNullOrEmpty(p.Key)).ToList();

            _cache.Set(cacheKey, projects, TimeSpan.FromDays(1));
            return projects;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch Jira projects");
            return new();
        }
    }

    public async Task<List<JiraIssueTypeItem>> GetIssueTypesAsync(string userName, string projectKey)
    {
        var cacheKey = $"jira_issuetypes_{projectKey}";
        if (_cache.TryGetValue(cacheKey, out List<JiraIssueTypeItem>? cached) && cached is not null)
            return cached;

        var token = await _authService.GetStoredTokenAsync(userName);
        if (token is null) return new();

        var url = $"{BaseUrl}/rest/api/2/project/{Uri.EscapeDataString(projectKey)}";

        try
        {
            var body = await GetJsonAsync("GET", url, token.AccessToken);
            var json = JsonNode.Parse(body);
            var issueTypes = json?["issueTypes"]?.AsArray();
            if (issueTypes is null) return new();

            var result = issueTypes.Select(it => new JiraIssueTypeItem
            {
                Id = it?["id"]?.GetValue<string>() ?? "",
                Name = it?["name"]?.GetValue<string>() ?? ""
            }).Where(it => !string.IsNullOrEmpty(it.Name)).ToList();

            _cache.Set(cacheKey, result, TimeSpan.FromDays(1));
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch issue types for {Project}", projectKey);
            return new();
        }
    }

    public async Task<List<string>> GetLabelsAsync(string userName)
    {
        const string cacheKey = "jira_labels";
        if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached is not null)
            return cached;

        var token = await _authService.GetStoredTokenAsync(userName);
        if (token is null) return new();

        try
        {
            var allLabels = new List<string>();
            var startAt = 0;
            const int pageSize = 1000;

            while (true)
            {
                var url = $"{BaseUrl}/rest/api/2/label?maxResults={pageSize}&startAt={startAt}";
                var body = await GetJsonAsync("GET", url, token.AccessToken);
                var json = JsonNode.Parse(body);

                var total = json?["total"]?.GetValue<int>() ?? 0;
                var values = json?["values"]?.AsArray();
                if (values is null) break;

                foreach (var v in values)
                {
                    var name = v?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name))
                        allLabels.Add(name);
                }

                startAt += pageSize;
                if (startAt >= total) break;
            }

            var labels = allLabels.Distinct().OrderBy(l => l).ToList();
            _cache.Set(cacheKey, labels, TimeSpan.FromDays(1));
            return labels;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch Jira labels");
            return new();
        }
    }

    public async Task<List<JiraBusinessOutcomeItem>> GetBusinessOutcomesAsync(string userName, string projectKey)
    {
        var cacheKey = $"jira_bo_{projectKey}";
        if (_cache.TryGetValue(cacheKey, out List<JiraBusinessOutcomeItem>? cached) && cached is not null)
            return cached;

        var token = await _authService.GetStoredTokenAsync(userName);
        if (token is null) return new();

        try
        {
            var json = await PostSearchAsync(token.AccessToken,
                $"project={projectKey} AND issuetype=\"Business Outcome\"",
                new[] { "summary" }, 1000, 0);
            var issues = json?["issues"]?.AsArray();
            if (issues is null) return new();

            var result = issues.Select(i => new JiraBusinessOutcomeItem
            {
                Key = i?["key"]?.GetValue<string>() ?? "",
                Summary = i?["fields"]?["summary"]?.GetValue<string>() ?? ""
            }).Where(bo => !string.IsNullOrEmpty(bo.Key)).ToList();

            _cache.Set(cacheKey, result, TimeSpan.FromHours(1));
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch business outcomes for {Project}", projectKey);
            return new();
        }
    }

    // ── Helpers ──

    private string BaseUrl => _settings.Url.TrimEnd('/');

    /// <summary>
    /// POST to /rest/api/2/search with JQL in the JSON body.
    /// This avoids query-parameter OAuth signing issues.
    /// </summary>
    private async Task<JsonNode?> PostSearchAsync(
        string accessToken, string jql, string[] fields, int maxResults, int startAt)
    {
        var url = $"{BaseUrl}/rest/api/2/search";

        var requestBody = new JsonObject
        {
            ["jql"] = jql,
            ["fields"] = new JsonArray(fields.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
            ["maxResults"] = maxResults,
            ["startAt"] = startAt,
        };

        using var httpClient = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Authorization",
            _authService.BuildOAuthHeader("POST", url, accessToken));

        var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Jira POST search failed ({Status}): {Body}", response.StatusCode, body);
            throw new Exception($"Jira search failed ({response.StatusCode})");
        }

        return JsonNode.Parse(body);
    }

    /// <summary>
    /// Sends a GET request with OAuth header. Signs against <paramref name="signUrl"/>
    /// but sends the request to <paramref name="requestUrl"/> (defaults to signUrl).
    /// This allows query parameters to be excluded from OAuth signing.
    /// </summary>
    private async Task<string> GetJsonAsync(string method, string signUrl, string accessToken, string? requestUrl = null)
    {
        requestUrl ??= signUrl;

        using var httpClient = new HttpClient();
        var request = new HttpRequestMessage(new HttpMethod(method), requestUrl);
        request.Headers.Add("Authorization",
            _authService.BuildOAuthHeader(method, signUrl, accessToken));

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            Log.Warning("Jira {Method} {Url} failed ({Status}): {Body}",
                method, requestUrl, response.StatusCode, errorBody);
            throw new Exception($"Jira request failed ({response.StatusCode})");
        }

        return await response.Content.ReadAsStringAsync();
    }
}
