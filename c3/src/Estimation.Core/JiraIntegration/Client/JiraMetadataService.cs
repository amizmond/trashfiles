using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Services;
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
    private readonly TimeProvider _time;

    private static readonly TimeSpan LabelsCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RecentFailureDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StatusesCacheDuration = TimeSpan.FromDays(1);
    private const int LabelPageSize = 100;
    private const int MaxParallelPageFetches = 4;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LabelLocks = new(StringComparer.Ordinal);

    public JiraMetadataService(
        IJiraAuthService authService,
        IOptions<JiraSettings> settings,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<EstimationDbContext> contextFactory,
        TimeProvider time)
    {
        _authService = authService;
        _settings = settings.Value;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _contextFactory = contextFactory;
        _time = time;
    }

    public async Task<List<JiraLabel>> GetLabelsAsync(string userName, string projectKey)
    {
        var key = LabelKey(projectKey);
        if (key is null)
        {
            return [];
        }

        if (TryGetCachedLabels(key, out var cached))
        {
            return cached;
        }

        var persisted = await LoadLabelsAsync(key);
        if (persisted is not null)
        {
            _cache.Set(LabelsMemoryKey(key), persisted, LabelsCacheDuration);
            return persisted;
        }

        var gate = LabelLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (TryGetCachedLabels(key, out cached))
            {
                return cached;
            }

            var fetchAs = await ConnectedServiceAccountAsync(CancellationToken.None) ?? userName;
            var failureKey = $"jira_labels_failure_{key}_{fetchAs}";
            var fetchedAt = _time.GetUtcNow();
            if (_cache.TryGetValue(failureKey, out RecentFailure? recent) && recent is not null
                && fetchedAt - recent.At < RecentFailureDuration)
            {
                throw new InvalidOperationException(recent.Error);
            }

            List<JiraLabel> labels;
            try
            {
                labels = await FetchLabelsAsync(fetchAs, key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _cache.Set(failureKey, new RecentFailure(ex.Message, fetchedAt), RecentFailureDuration);
                throw;
            }

            _cache.Remove(failureKey);
            await SaveLabelsAsync(key, labels, fetchedAt.UtcDateTime, CancellationToken.None);
            _cache.Set(LabelsMemoryKey(key), labels, LabelsCacheDuration);
            return labels;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<JiraLabelRefreshResult> RefreshLabelsAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        var key = LabelKey(projectKey);
        if (key is null)
        {
            return JiraLabelRefreshResult.Failure(projectKey, $"'{projectKey}' is not a Jira project key.");
        }

        var serviceUser = await ConnectedServiceAccountAsync(cancellationToken);
        if (serviceUser is null)
        {
            return JiraLabelRefreshResult.Failure(key, "The Jira service account is not connected.");
        }

        var gate = LabelLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var attemptedAt = _time.GetUtcNow().UtcDateTime;
            List<JiraLabel> labels;
            try
            {
                labels = await FetchLabelsAsync(serviceUser, key, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning(ex, "Refreshing Jira labels failed for {Key}", key);
                await SaveFailureAsync(key, attemptedAt, ex.Message, cancellationToken);
                return JiraLabelRefreshResult.Failure(key, ex.Message);
            }

            if (!await SaveLabelsAsync(key, labels, attemptedAt, cancellationToken))
            {
                return JiraLabelRefreshResult.Failure(key, $"The labels of {key} were fetched from Jira but could not be saved.");
            }

            _cache.Set(LabelsMemoryKey(key), labels, LabelsCacheDuration);
            return JiraLabelRefreshResult.Success(key, labels);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string? LabelKey(string? projectKey)
    {
        var key = JiraProjectKeys.Normalize(projectKey);
        return key is not null && ArtService.IsValidKey(key) ? key : null;
    }

    private static string LabelsMemoryKey(string key) => $"jira_labels_{key}";

    private bool TryGetCachedLabels(string key, out List<JiraLabel> labels)
    {
        if (_cache.TryGetValue(LabelsMemoryKey(key), out List<JiraLabel>? cached) && cached is not null)
        {
            labels = cached;
            return true;
        }

        labels = [];
        return false;
    }

    private async Task<string?> ConnectedServiceAccountAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var configured = await context.JiraSyncSettings
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => s.ServiceAccountUserName)
            .FirstOrDefaultAsync(cancellationToken);
        var userName = string.IsNullOrWhiteSpace(configured) ? JiraSyncSettings.DefaultServiceAccountUserName : configured;

        return await context.JiraTokens.AnyAsync(t => t.UserName == userName, cancellationToken) ? userName : null;
    }

    private async Task<List<JiraLabel>?> LoadLabelsAsync(string key)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var json = await context.JiraLabelCaches
                .AsNoTracking()
                .Where(c => c.CacheKey == key && c.UpdatedAt != null)
                .Select(c => c.LabelsJson)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var names = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return names.Select(n => new JiraLabel { Name = n }).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load persisted Jira labels for {Key}", key);
            return null;
        }
    }

    private Task<bool> SaveLabelsAsync(string key, List<JiraLabel> labels, DateTime fetchedAt, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(labels.Select(l => l.Name).ToList());
        return UpsertLabelCacheAsync(key, row =>
        {
            row.LabelsJson = json;
            row.UpdatedAt = fetchedAt;
            row.LastAttemptAt = fetchedAt;
            row.LastError = null;
        }, cancellationToken);
    }

    private Task<bool> SaveFailureAsync(string key, DateTime attemptedAt, string error, CancellationToken cancellationToken) =>
        UpsertLabelCacheAsync(key, row =>
        {
            row.LastAttemptAt = attemptedAt;
            row.LastError = error.Length > JiraLabelCache.MaxErrorLength ? error[..JiraLabelCache.MaxErrorLength] : error;
        }, cancellationToken);

    private async Task<bool> UpsertLabelCacheAsync(string key, Action<JiraLabelCache> update, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var row = await context.JiraLabelCaches.FirstOrDefaultAsync(c => c.CacheKey == key, cancellationToken);
            if (row is null)
            {
                row = new JiraLabelCache { CacheKey = key, LabelsJson = "[]" };
                context.JiraLabelCaches.Add(row);
            }

            update(row);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warning(ex, "Failed to persist Jira labels for {Key}", key);
            return false;
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
                _cache.Set(cacheKey, statuses, StatusesCacheDuration);
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

    private async Task<List<JiraLabel>> FetchLabelsAsync(string userName, string key, CancellationToken cancellationToken)
    {
        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira.");

        var encodedJql = Uri.EscapeDataString($"project = \"{key}\" AND labels is not EMPTY ORDER BY key ASC");
        var baseUrl = $"{_settings.Url.TrimEnd('/')}/rest/api/2/search";

        var first = await FetchLabelPageAsync(token.AccessToken, baseUrl, encodedJql, key, 0, cancellationToken);
        var labels = new HashSet<string>(first.Labels, StringComparer.Ordinal);

        var pageSize = first.MaxResults > 0 ? first.MaxResults
            : first.IssueCount > 0 ? first.IssueCount
            : LabelPageSize;

        if (first.Total > first.IssueCount)
        {
            using var gate = new SemaphoreSlim(MaxParallelPageFetches);
            string? failure = null;

            var pages = await Task.WhenAll(
                Enumerable.Range(1, (first.Total - 1) / pageSize)
                    .Select(async page =>
                    {
                        await gate.WaitAsync(cancellationToken);
                        try
                        {
                            if (Volatile.Read(ref failure) is not null)
                            {
                                return null;
                            }
                            return await FetchLabelPageAsync(token.AccessToken, baseUrl, encodedJql, key, page * pageSize, cancellationToken);
                        }
                        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                        {
                            Interlocked.CompareExchange(ref failure, ex.Message, null);
                            return null;
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));

            if (failure is not null)
            {
                throw new InvalidOperationException(failure);
            }

            foreach (var page in pages)
            {
                labels.UnionWith(page!.Labels);
            }
        }

        return labels.Select(l => new JiraLabel { Name = l }).OrderBy(l => l.Name).ToList();
    }

    private async Task<LabelPage> FetchLabelPageAsync(
        string accessToken, string baseUrl, string encodedJql, string key, int startAt, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl}?jql={encodedJql}&fields=labels&maxResults={LabelPageSize}&startAt={startAt}";

        var httpClient = _httpClientFactory.CreateClient(JiraIssueService.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, accessToken));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Jira returned {(int)response.StatusCode} for the labels of {key} (startAt={startAt}).");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var total = doc.RootElement.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : 0;
        var maxResults = doc.RootElement.TryGetProperty("maxResults", out var maxEl) ? maxEl.GetInt32() : 0;

        var labels = new List<string>();
        var issueCount = 0;
        if (doc.RootElement.TryGetProperty("issues", out var issues))
        {
            foreach (var issue in issues.EnumerateArray())
            {
                issueCount++;
                if (issue.TryGetProperty("fields", out var fields) &&
                    fields.TryGetProperty("labels", out var labelsArr) &&
                    labelsArr.ValueKind == JsonValueKind.Array)
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

        return new LabelPage(labels, total, maxResults, issueCount);
    }

    private sealed record LabelPage(List<string> Labels, int Total, int MaxResults, int IssueCount);

    private sealed record RecentFailure(string Error, DateTimeOffset At);
}
