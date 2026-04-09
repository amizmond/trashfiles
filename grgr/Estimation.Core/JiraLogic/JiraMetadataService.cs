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

    private static readonly TimeSpan LabelsCacheDuration = TimeSpan.FromDays(1);

    public JiraMetadataService(
        IJiraAuthService authService,
        IOptions<JiraOAuthSettings> settings,
        IMemoryCache cache)
    {
        _authService = authService;
        _settings = settings.Value;
        _cache = cache;
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
        var allLabels = new List<string>();
        var startAt = 0;
        const int pageSize = 1000;

        while (true)
        {
            var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/label?maxResults={pageSize}&startAt={startAt}";

            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization",
                _authService.BuildOAuthHeader("GET", url, accessToken));

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Labels endpoint returned {StatusCode} at startAt={StartAt}",
                    (int)response.StatusCode, startAt);

                if (startAt == 0)
                    return await GetLabelsByJql(accessToken, "labels is not EMPTY");

                break;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("values", out var values))
            {
                foreach (var v in values.EnumerateArray())
                {
                    var name = v.GetString();
                    if (!string.IsNullOrEmpty(name))
                        allLabels.Add(name);
                }
            }

            var total = doc.RootElement.TryGetProperty("total", out var totalEl)
                ? totalEl.GetInt32()
                : allLabels.Count;

            startAt += pageSize;
            if (startAt >= total)
                break;
        }

        Log.Information("Loaded {Count} labels from /rest/api/2/label", allLabels.Count);

        return allLabels
            .Select(l => new JiraLabel { Name = l })
            .OrderBy(l => l.Name)
            .ToList();
    }

    private async Task<List<JiraLabel>> GetLabelsByJql(string accessToken, string jql)
    {
        var labelsSet = new HashSet<string>();
        var startAt = 0;
        const int pageSize = 100;
        var encodedJql = Uri.EscapeDataString(jql);

        while (true)
        {
            var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/search?jql={encodedJql}&fields=labels&maxResults={pageSize}&startAt={startAt}";

            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization",
                _authService.BuildOAuthHeader("GET", url, accessToken));

            var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                Log.Error("Labels JQL query failed with {StatusCode} for JQL: {Jql}",
                    (int)response.StatusCode, jql);
                break;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var total = doc.RootElement.TryGetProperty("total", out var totalEl)
                ? totalEl.GetInt32() : 0;

            if (doc.RootElement.TryGetProperty("issues", out var issues))
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    if (issue.TryGetProperty("fields", out var fields) &&
                        fields.TryGetProperty("labels", out var labels))
                    {
                        foreach (var label in labels.EnumerateArray())
                        {
                            var val = label.GetString();
                            if (!string.IsNullOrEmpty(val))
                                labelsSet.Add(val);
                        }
                    }
                }
            }

            startAt += pageSize;
            if (startAt >= total)
                break;
        }

        if (labelsSet.Count == 0)
            Log.Warning("JQL label query returned 0 labels for: {Jql}", jql);

        return labelsSet.Select(l => new JiraLabel { Name = l }).OrderBy(l => l.Name).ToList();
    }
}
