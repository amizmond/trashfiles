using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraLogic;

public class JiraMetadataService : IJiraMetadataService
{
    private readonly IJiraAuthService _authService;
    private readonly JiraOAuthSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly TimeSpan LabelsCacheDuration = TimeSpan.FromDays(1);

    public JiraMetadataService(
        IJiraAuthService authService,
        IOptions<JiraOAuthSettings> settings,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory)
    {
        _authService = authService;
        _settings = settings.Value;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<JiraLabel>> GetLabelsAsync(string userName, string? projectKey = null)
    {
        var cacheKey = $"jira_labels_{projectKey ?? "_global"}";

        if (_cache.TryGetValue(cacheKey, out List<JiraLabel>? cached) && cached is not null)
            return cached;

        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira.");

        List<JiraLabel> labels;

        if (!string.IsNullOrWhiteSpace(projectKey))
            labels = await GetLabelsByJql(token.AccessToken, $"project = \"{projectKey}\" AND labels is not EMPTY");
        else
            labels = await GetLabelsFromEndpoint(token.AccessToken);

        _cache.Set(cacheKey, labels, LabelsCacheDuration);
        return labels;
    }

    private async Task<List<JiraLabel>> GetLabelsFromEndpoint(string accessToken)
    {
        const int pageSize = 1000;
        var baseUrl = $"{_settings.Url.TrimEnd('/')}/rest/api/2/label";

        // First request to get total count
        var firstPage = await FetchLabelPage(accessToken, baseUrl, 0, pageSize);
        if (firstPage == null)
            return await GetLabelsByJql(accessToken, "labels is not EMPTY");

        var allLabels = new List<string>(firstPage.Labels);
        var total = firstPage.Total;

        // Fetch remaining pages concurrently
        if (total > pageSize)
        {
            var tasks = new List<Task<LabelPage?>>();
            for (var startAt = pageSize; startAt < total; startAt += pageSize)
                tasks.Add(FetchLabelPage(accessToken, baseUrl, startAt, pageSize));

            var pages = await Task.WhenAll(tasks);
            foreach (var page in pages)
            {
                if (page != null)
                    allLabels.AddRange(page.Labels);
            }
        }

        Log.Information("Loaded {Count} labels from /rest/api/2/label", allLabels.Count);

        return allLabels
            .Select(l => new JiraLabel { Name = l })
            .OrderBy(l => l.Name)
            .ToList();
    }

    private async Task<LabelPage?> FetchLabelPage(string accessToken, string baseUrl, int startAt, int pageSize)
    {
        var url = $"{baseUrl}?maxResults={pageSize}&startAt={startAt}";

        using var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization",
            _authService.BuildOAuthHeader("GET", url, accessToken));

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Labels endpoint returned {StatusCode} at startAt={StartAt}",
                (int)response.StatusCode, startAt);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var labels = new List<string>();
        if (doc.RootElement.TryGetProperty("values", out var values))
        {
            foreach (var v in values.EnumerateArray())
            {
                var name = v.GetString();
                if (!string.IsNullOrEmpty(name))
                    labels.Add(name);
            }
        }

        var total = doc.RootElement.TryGetProperty("total", out var totalEl)
            ? totalEl.GetInt32()
            : labels.Count;

        return new LabelPage(labels, total);
    }

    private async Task<List<JiraLabel>> GetLabelsByJql(string accessToken, string jql)
    {
        const int pageSize = 100;
        var encodedJql = Uri.EscapeDataString(jql);
        var baseUrl = $"{_settings.Url.TrimEnd('/')}/rest/api/2/search";

        // First request to get total count
        var firstPage = await FetchJqlPage(accessToken, baseUrl, encodedJql, 0, pageSize);
        if (firstPage == null)
            return [];

        var labelsSet = new HashSet<string>(firstPage.Labels);
        var total = firstPage.Total;

        // Fetch remaining pages concurrently
        if (total > pageSize)
        {
            var tasks = new List<Task<LabelPage?>>();
            for (var startAt = pageSize; startAt < total; startAt += pageSize)
                tasks.Add(FetchJqlPage(accessToken, baseUrl, encodedJql, startAt, pageSize));

            var pages = await Task.WhenAll(tasks);
            foreach (var page in pages)
            {
                if (page != null)
                    labelsSet.UnionWith(page.Labels);
            }
        }

        if (labelsSet.Count == 0)
            Log.Warning("JQL label query returned 0 labels for: {Jql}", jql);

        return labelsSet.Select(l => new JiraLabel { Name = l }).OrderBy(l => l.Name).ToList();
    }

    private async Task<LabelPage?> FetchJqlPage(string accessToken, string baseUrl, string encodedJql, int startAt, int pageSize)
    {
        var url = $"{baseUrl}?jql={encodedJql}&fields=labels&maxResults={pageSize}&startAt={startAt}";

        using var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization",
            _authService.BuildOAuthHeader("GET", url, accessToken));

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Labels JQL query failed with {StatusCode} at startAt={StartAt}",
                (int)response.StatusCode, startAt);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var total = doc.RootElement.TryGetProperty("total", out var totalEl)
            ? totalEl.GetInt32() : 0;

        var labels = new List<string>();
        if (doc.RootElement.TryGetProperty("issues", out var issues))
        {
            foreach (var issue in issues.EnumerateArray())
            {
                if (issue.TryGetProperty("fields", out var fields) &&
                    fields.TryGetProperty("labels", out var labelsArr))
                {
                    foreach (var label in labelsArr.EnumerateArray())
                    {
                        var val = label.GetString();
                        if (!string.IsNullOrEmpty(val))
                            labels.Add(val);
                    }
                }
            }
        }

        return new LabelPage(labels, total);
    }

    private sealed record LabelPage(List<string> Labels, int Total);
}
