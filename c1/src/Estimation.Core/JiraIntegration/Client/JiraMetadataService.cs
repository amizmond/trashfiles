using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraIntegration.Client;

public class JiraMetadataService : IJiraMetadataService
{
    private readonly IJiraAuthService _authService;
    private readonly JiraSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<EstimationDbContext> _contextFactory;

    private static readonly TimeSpan LabelsCacheDuration = TimeSpan.FromDays(1);
    private static readonly TimeSpan StatusesCacheDuration = TimeSpan.FromDays(1);
    private static readonly TimeSpan LabelsStaleThreshold = TimeSpan.FromHours(6);
    private const int MaxParallelPageFetches = 4;

    private static readonly ConcurrentDictionary<string, byte> RefreshInFlight = new();

    public JiraMetadataService(
        IJiraAuthService authService,
        IOptions<JiraSettings> settings,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<EstimationDbContext> contextFactory)
    {
        _authService = authService;
        _settings = settings.Value;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _contextFactory = contextFactory;
    }

    public async Task<List<JiraLabel>> GetLabelsAsync(string userName, string? projectKey = null)
    {
        var cacheKey = $"jira_labels_{projectKey ?? "_global"}";
        var dbKey = projectKey ?? "_global";

        if (_cache.TryGetValue(cacheKey, out List<JiraLabel>? cached) && cached is not null)
        {
            return cached;
        }

        var persisted = await TryLoadLabelsFromDbAsync(dbKey);
        if (persisted is not null)
        {
            var (persistedLabels, updatedAt) = persisted.Value;
            _cache.Set(cacheKey, persistedLabels, LabelsCacheDuration);

            if (DateTime.UtcNow - updatedAt > LabelsStaleThreshold)
            {
                _ = RefreshLabelsInBackgroundAsync(userName, projectKey, cacheKey, dbKey);
            }

            return persistedLabels;
        }

        var labels = await FetchLabelsFromJiraAsync(userName, projectKey);
        _cache.Set(cacheKey, labels, LabelsCacheDuration);
        await SaveLabelsToDbAsync(dbKey, labels);
        return labels;
    }

    private async Task<List<JiraLabel>> FetchLabelsFromJiraAsync(string userName, string? projectKey)
    {
        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira.");

        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            return await GetLabelsByJql(token.AccessToken, $"project = \"{projectKey}\" AND labels is not EMPTY");
        }

        return await GetLabelsFromEndpoint(token.AccessToken);
    }

    private async Task RefreshLabelsInBackgroundAsync(string userName, string? projectKey, string cacheKey, string dbKey)
    {
        if (!RefreshInFlight.TryAdd(dbKey, 0))
        {
            return;
        }

        try
        {
            var fresh = await FetchLabelsFromJiraAsync(userName, projectKey);
            _cache.Set(cacheKey, fresh, LabelsCacheDuration);
            await SaveLabelsToDbAsync(dbKey, fresh);
            Log.Information("Background refresh of Jira labels completed for {Key} ({Count})", dbKey, fresh.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Background refresh of Jira labels failed for {Key}", dbKey);
        }
        finally
        {
            RefreshInFlight.TryRemove(dbKey, out _);
        }
    }

    private async Task<(List<JiraLabel> Labels, DateTime UpdatedAt)?> TryLoadLabelsFromDbAsync(string dbKey)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var row = await context.JiraLabelCaches
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CacheKey == dbKey);

            if (row is null || string.IsNullOrWhiteSpace(row.LabelsJson))
            {
                return null;
            }

            var names = JsonSerializer.Deserialize<List<string>>(row.LabelsJson) ?? [];
            var labels = names.Select(n => new JiraLabel { Name = n }).ToList();
            return (labels, row.UpdatedAt);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load persisted Jira labels for {Key}", dbKey);
            return null;
        }
    }

    private async Task SaveLabelsToDbAsync(string dbKey, List<JiraLabel> labels)
    {
        try
        {
            var json = JsonSerializer.Serialize(labels.Select(l => l.Name).ToList());

            await using var context = await _contextFactory.CreateDbContextAsync();
            var row = await context.JiraLabelCaches.FirstOrDefaultAsync(c => c.CacheKey == dbKey);

            if (row is null)
            {
                context.JiraLabelCaches.Add(new JiraLabelCache
                {
                    CacheKey = dbKey,
                    LabelsJson = json,
                    UpdatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                row.LabelsJson = json;
                row.UpdatedAt = DateTime.UtcNow;
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist Jira labels for {Key}", dbKey);
        }
    }

    public async Task<List<JiraStatus>> GetStatusesAsync(string userName, string projectKey)
    {
        var cacheKey = $"jira_statuses_{projectKey}";

        if (_cache.TryGetValue(cacheKey, out List<JiraStatus>? cached) && cached is not null)
        {
            return cached;
        }

        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira.");

        var statuses = new List<JiraStatus>();

        try
        {
            var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/project/{Uri.EscapeDataString(projectKey)}/statuses";

            var httpClient = _httpClientFactory.CreateClient(JiraIssueService.HttpClientName);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Options.Set(JiraOAuthSigningHandler.SignerKey,
                () => _authService.BuildOAuthHeader("GET", url, token.AccessToken));

            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var statusNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var issueType in doc.RootElement.EnumerateArray())
                {
                    if (issueType.TryGetProperty("statuses", out var statusesArr))
                    {
                        foreach (var status in statusesArr.EnumerateArray())
                        {
                            if (status.TryGetProperty("name", out var nameEl))
                            {
                                var name = nameEl.GetString();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    statusNames.Add(name);
                                }
                            }
                        }
                    }
                }

                statuses = statusNames
                    .Select(n => new JiraStatus { Name = n })
                    .OrderBy(s => s.Name)
                    .ToList();
            }
            else
            {
                Log.Warning("Project statuses endpoint returned {StatusCode} for project {Project}",
                    (int)response.StatusCode, projectKey);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to fetch statuses for project {Project}", projectKey);
        }

        _cache.Set(cacheKey, statuses, StatusesCacheDuration);
        return statuses;
    }

    public async Task<JiraProject?> GetProjectAsync(string userName, string projectKey)
    {
        var cacheKey = $"jira_project_{projectKey}";
        if (_cache.TryGetValue(cacheKey, out JiraProject? cached) && cached is not null)
        {
            return cached;
        }

        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira.");

        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/project/{Uri.EscapeDataString(projectKey)}";
        var httpClient = _httpClientFactory.CreateClient(JiraIssueService.HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, token.AccessToken));

        var response = await httpClient.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Jira returned {(int)response.StatusCode} for project {projectKey}.");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var project = new JiraProject
        {
            Key = doc.RootElement.TryGetProperty("key", out var keyEl) ? keyEl.GetString() ?? projectKey : projectKey,
            Name = doc.RootElement.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
        };

        _cache.Set(cacheKey, project, StatusesCacheDuration);
        return project;
    }

    private async Task<List<JiraLabel>> GetLabelsFromEndpoint(string accessToken)
    {
        const int pageSize = 1000;
        var baseUrl = $"{_settings.Url.TrimEnd('/')}/rest/api/2/label";

        var firstPage = await FetchLabelPage(accessToken, baseUrl, 0, pageSize);
        if (firstPage == null)
        {
            return await GetLabelsByJql(accessToken, "labels is not EMPTY");
        }

        var allLabels = new List<string>(firstPage.Labels);
        var total = firstPage.Total;

        if (total > pageSize)
        {
            using var gate = new SemaphoreSlim(MaxParallelPageFetches);
            var tasks = new List<Task<LabelPage?>>();
            for (var startAt = pageSize; startAt < total; startAt += pageSize)
            {
                var capturedStart = startAt;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        return await FetchLabelPage(accessToken, baseUrl, capturedStart, pageSize);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }

            var pages = await Task.WhenAll(tasks);
            foreach (var page in pages)
            {
                if (page != null)
                {
                    allLabels.AddRange(page.Labels);
                }
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

        var httpClient = _httpClientFactory.CreateClient(JiraIssueService.HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, accessToken));

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
                {
                    labels.Add(name);
                }
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

        var firstPage = await FetchJqlPage(accessToken, baseUrl, encodedJql, 0, pageSize);
        if (firstPage == null)
        {
            return [];
        }

        var labelsSet = new HashSet<string>(firstPage.Labels);
        var total = firstPage.Total;

        if (total > pageSize)
        {
            using var gate = new SemaphoreSlim(MaxParallelPageFetches);
            var tasks = new List<Task<LabelPage?>>();
            for (var startAt = pageSize; startAt < total; startAt += pageSize)
            {
                var capturedStart = startAt;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        return await FetchJqlPage(accessToken, baseUrl, encodedJql, capturedStart, pageSize);
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }

            var pages = await Task.WhenAll(tasks);
            foreach (var page in pages)
            {
                if (page != null)
                {
                    labelsSet.UnionWith(page.Labels);
                }
            }
        }

        if (labelsSet.Count == 0)
        {
            Log.Warning("JQL label query returned 0 labels for: {Jql}", jql);
        }

        return labelsSet.Select(l => new JiraLabel { Name = l }).OrderBy(l => l.Name).ToList();
    }

    private async Task<LabelPage?> FetchJqlPage(string accessToken, string baseUrl, string encodedJql, int startAt, int pageSize)
    {
        var url = $"{baseUrl}?jql={encodedJql}&fields=labels&maxResults={pageSize}&startAt={startAt}";

        var httpClient = _httpClientFactory.CreateClient(JiraIssueService.HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, accessToken));

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
                        {
                            labels.Add(val);
                        }
                    }
                }
            }
        }

        return new LabelPage(labels, total);
    }

    private sealed record LabelPage(List<string> Labels, int Total);
}
