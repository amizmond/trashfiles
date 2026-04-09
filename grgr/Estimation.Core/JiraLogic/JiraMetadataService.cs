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

    public async Task<List<string>> GetLabelsAsync(string userName, string projectKey)
    {
        var cacheKey = $"jira_labels_{projectKey}";
        if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached is not null)
            return cached;

        var token = await _authService.GetStoredTokenAsync(userName);
        if (token is null) return new();

        var jql = $"project={projectKey}";
        var baseUrl = $"{BaseUrl}/rest/api/2/search";

        try
        {
            var labelsSet = new HashSet<string>(StringComparer.Ordinal);
            var startAt = 0;
            const int pageSize = 100;
            var encodedJql = Uri.EscapeDataString(jql);

            while (true)
            {
                var fullUrl = $"{baseUrl}?jql={encodedJql}&fields=labels&maxResults={pageSize}&startAt={startAt}";
                var body = await GetJsonAsync("GET", baseUrl, token.AccessToken, fullUrl);
                var json = JsonNode.Parse(body);

                var total = json?["total"]?.GetValue<int>() ?? 0;
                var issues = json?["issues"]?.AsArray();

                if (issues is not null)
                {
                    foreach (var issue in issues)
                    {
                        var labelsArr = issue?["fields"]?["labels"]?.AsArray();
                        if (labelsArr is null) continue;
                        foreach (var label in labelsArr)
                        {
                            var val = label?.GetValue<string>();
                            if (!string.IsNullOrEmpty(val))
                                labelsSet.Add(val);
                        }
                    }
                }

                startAt += pageSize;
                if (startAt >= total) break;
            }

            var labels = labelsSet.OrderBy(l => l).ToList();

            if (labels.Count == 0)
                Log.Warning("JQL label query returned 0 labels for project {Project}", projectKey);

            _cache.Set(cacheKey, labels, TimeSpan.FromDays(1));
            return labels;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch Jira labels for {Project}", projectKey);
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

        var jql = Uri.EscapeDataString($"project={projectKey} AND issuetype=\"Business Outcome\"");
        var baseUrl = $"{BaseUrl}/rest/api/2/search";
        var fullUrl = $"{baseUrl}?jql={jql}&fields=summary&maxResults=1000";

        try
        {
            var body = await GetJsonAsync("GET", baseUrl, token.AccessToken, fullUrl);
            var json = JsonNode.Parse(body);
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
