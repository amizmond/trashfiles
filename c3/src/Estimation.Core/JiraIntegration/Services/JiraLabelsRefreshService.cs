using System.Text.Json;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.JiraIntegration.Services;

public sealed record JiraLabelsKeyStatus(
    string JiraKey,
    int LabelCount,
    DateTime? RefreshedAt,
    DateTime? LastAttemptAt,
    string? LastError,
    bool IsFresh)
{
    public bool LastAttemptFailed => LastError is not null;
}

public interface IJiraLabelsRefreshService
{
    Task<string?> GetNextDueKeyAsync(CancellationToken cancellationToken = default);
    Task<List<JiraLabelsKeyStatus>> GetStatusAsync();
}

public class JiraLabelsRefreshService : IJiraLabelsRefreshService
{
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(6);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly TimeProvider _time;

    public JiraLabelsRefreshService(IDbContextFactory<EstimationDbContext> ctx, TimeProvider time)
    {
        _ctx = ctx;
        _time = time;
    }

    public async Task<string?> GetNextDueKeyAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);
        var keys = await ConfiguredKeysAsync(db, cancellationToken);
        if (keys.Count == 0)
        {
            return null;
        }

        var rows = await db.JiraLabelCaches
            .AsNoTracking()
            .Where(c => keys.Contains(c.CacheKey))
            .Select(c => new KeyState(c.CacheKey, c.UpdatedAt, c.LastAttemptAt))
            .ToListAsync(cancellationToken);
        var byKey = new Dictionary<string, KeyState>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            byKey.TryAdd(row.Key, row);
        }
        var now = _time.GetUtcNow().UtcDateTime;

        return keys
            .Select(key => byKey.TryGetValue(key, out var state) ? state with { Key = key } : new KeyState(key, null, null))
            .Where(s => (s.RefreshedAt is null || s.RefreshedAt <= now - RefreshAfter)
                        && (s.LastAttemptAt is null || s.LastAttemptAt <= now - RetryAfter))
            .OrderBy(s => s.RefreshedAt.HasValue)
            .ThenBy(s => s.RefreshedAt)
            .ThenBy(s => s.LastAttemptAt)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => s.Key)
            .FirstOrDefault();
    }

    public async Task<List<JiraLabelsKeyStatus>> GetStatusAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var keys = await ConfiguredKeysAsync(db, CancellationToken.None);
        var rows = await RowsByKeyAsync(db, keys, CancellationToken.None);
        var now = _time.GetUtcNow().UtcDateTime;

        return keys
            .Select(key =>
            {
                if (!rows.TryGetValue(key, out var row))
                {
                    return new JiraLabelsKeyStatus(key, 0, null, null, null, false);
                }

                return new JiraLabelsKeyStatus(
                    key,
                    row.UpdatedAt is null ? 0 : CountLabels(row.LabelsJson),
                    row.UpdatedAt,
                    row.LastAttemptAt,
                    row.LastError,
                    row.UpdatedAt > now - RefreshAfter);
            })
            .ToList();
    }

    private static async Task<List<string>> ConfiguredKeysAsync(EstimationDbContext db, CancellationToken cancellationToken)
    {
        var syncKeys = await db.JiraSyncKeys.AsNoTracking().Select(k => k.JiraKey).ToListAsync(cancellationToken);
        var artKeys = await db.CapitalProjectJiraKeys.AsNoTracking().Select(k => k.JiraKey).ToListAsync(cancellationToken);

        return syncKeys
            .Concat(artKeys)
            .Select(JiraProjectKeys.Normalize)
            .OfType<string>()
            .Where(ArtService.IsValidKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<Dictionary<string, JiraLabelCacheRow>> RowsByKeyAsync(
        EstimationDbContext db, List<string> keys, CancellationToken cancellationToken)
    {
        var rows = await db.JiraLabelCaches
            .AsNoTracking()
            .Where(c => keys.Contains(c.CacheKey))
            .Select(c => new JiraLabelCacheRow(c.CacheKey, c.LabelsJson, c.UpdatedAt, c.LastAttemptAt, c.LastError))
            .ToListAsync(cancellationToken);

        var byKey = new Dictionary<string, JiraLabelCacheRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            byKey.TryAdd(row.CacheKey, row);
        }
        return byKey;
    }

    private static int CountLabels(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json)?.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private sealed record KeyState(string Key, DateTime? RefreshedAt, DateTime? LastAttemptAt);

    private sealed record JiraLabelCacheRow(
        string CacheKey, string LabelsJson, DateTime? UpdatedAt, DateTime? LastAttemptAt, string? LastError);
}
