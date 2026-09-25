using System.Globalization;
using System.Text;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.JiraIntegration.Services;

public interface IJiraSyncService
{
    Task<JiraSyncSettings> GetSettingsAsync();
    Task<JiraSyncSettings> StartAsync(int cooldownMinutes);
    Task<JiraSyncSettings> StopAsync();
    Task UpdateJiraTimeZoneAsync(string? timeZoneId);
    Task SetLabelsSyncEnabledAsync(bool enabled);
    Task<List<JiraSyncKeyOverview>> GetKeysAsync();
    Task<JiraSyncKeySaveResult> SaveKeyAsync(JiraSyncKey settings);
    Task<JiraSyncKey> AddKeyAsync(string jiraKey);
    Task<JiraSyncKeyItemCounts> CountSyncedItemsAsync(string jiraKey);
    Task RemoveKeyAsync(string jiraKey);
    Task ResetWatermarkAsync(string jiraKey);
    Task<bool> IsServiceAccountConnectedAsync();
    Task<List<JiraSyncHistory>> GetHistoryAsync(int maxRows = 100);
    Task<JiraSyncHistory> RunSyncAsync(string triggeredBy, CancellationToken cancellationToken = default);
    DateTime CalculateNextRun(JiraSyncSettings settings, DateTime fromUtc);
}

public class JiraSyncService : IJiraSyncService
{
    private static readonly HierarchyEntityType[] PassOrder =
    {
        HierarchyEntityType.StrategicObjective,
        HierarchyEntityType.PortfolioEpic,
        HierarchyEntityType.BusinessOutcome,
        HierarchyEntityType.Feature,
    };

    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IJiraIssueService _jira;
    private readonly IJiraSyncWriter _writer;

    public JiraSyncService(
        IDbContextFactory<EstimationDbContext> ctx,
        IJiraIssueService jira,
        IJiraSyncWriter writer)
    {
        _ctx = ctx;
        _jira = jira;
        _writer = writer;
    }

    public async Task<JiraSyncSettings> GetSettingsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var settings = await db.JiraSyncSettings.FirstOrDefaultAsync();

        if (settings is null)
        {
            settings = new JiraSyncSettings { Enabled = false };
            db.JiraSyncSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        return settings;
    }

    public async Task<JiraSyncSettings> StartAsync(int cooldownMinutes)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.JiraSyncSettings.FirstOrDefaultAsync();

        if (existing is null)
        {
            existing = new JiraSyncSettings();
            db.JiraSyncSettings.Add(existing);
        }

        existing.CycleCooldownMinutes = cooldownMinutes < 1 ? 1 : cooldownMinutes;
        existing.Enabled = true;
        existing.NextRunAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return await db.JiraSyncSettings.AsNoTracking().FirstAsync();
    }

    public async Task<JiraSyncSettings> StopAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.JiraSyncSettings.FirstOrDefaultAsync();

        if (existing is not null)
        {
            existing.Enabled = false;
            existing.NextRunAt = null;
            await db.SaveChangesAsync();
        }

        return await GetSettingsAsync();
    }

    public async Task UpdateJiraTimeZoneAsync(string? timeZoneId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.JiraSyncSettings.FirstOrDefaultAsync();
        if (existing is null)
        {
            existing = new JiraSyncSettings();
            db.JiraSyncSettings.Add(existing);
        }

        existing.JiraTimeZoneId = string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId;
        await db.SaveChangesAsync();
    }

    public async Task SetLabelsSyncEnabledAsync(bool enabled)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.JiraSyncSettings.FirstOrDefaultAsync();
        if (existing is null)
        {
            existing = new JiraSyncSettings();
            db.JiraSyncSettings.Add(existing);
        }

        existing.LabelsSyncEnabled = enabled;
        await db.SaveChangesAsync();
    }

    public async Task<List<JiraSyncKeyOverview>> GetKeysAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var settings = await db.JiraSyncKeys.AsNoTracking().ToListAsync();
        var artRows = await db.CapitalProjectJiraKeys
            .AsNoTracking()
            .OrderBy(k => k.Id)
            .Select(k => new { k.JiraKey, k.CapitalProjectId, ArtName = k.Art.Name, k.Components, k.Labels })
            .ToListAsync();

        var matcher = new ArtMatcher(artRows.Select(r => ArtKeyScope.Create(r.CapitalProjectId, r.JiraKey, r.Components, r.Labels)));
        var conflicts = matcher.Conflicts;
        var settingsByKey = settings.ToDictionary(s => s.JiraKey, StringComparer.OrdinalIgnoreCase);
        var artsByKey = artRows
            .GroupBy(r => r.JiraKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<JiraSyncKeyArt>)g
                    .Select(r => new JiraSyncKeyArt(r.CapitalProjectId, r.ArtName, r.Components, r.Labels))
                    .OrderBy(a => a.ArtName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        return settingsByKey.Keys
            .Concat(artsByKey.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(k => new JiraSyncKeyOverview(
                k,
                settingsByKey.GetValueOrDefault(k),
                artsByKey.GetValueOrDefault(k) ?? [],
                conflicts.Where(c => string.Equals(c.JiraKey, k, StringComparison.OrdinalIgnoreCase)).ToList()))
            .ToList();
    }

    public async Task<JiraSyncKeySaveResult> SaveKeyAsync(JiraSyncKey settings)
    {
        var key = JiraProjectKeys.Normalize(settings.JiraKey)
                  ?? throw new InvalidOperationException("Enter a Jira project key.");

        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.JiraSyncKeys.FirstOrDefaultAsync(k => k.JiraKey == key);

        if (existing is null)
        {
            if (!await db.CapitalProjectJiraKeys.AnyAsync(k => k.JiraKey == key))
            {
                throw new InvalidOperationException($"Jira key {key} is no longer on the list. Reload the page.");
            }

            existing = new JiraSyncKey { JiraKey = key };
            db.JiraSyncKeys.Add(existing);
        }

        var reset = existing.LastSyncedWatermarkUtc is not null && NeedsRefetch(existing, settings);

        existing.CreateNotExisted = settings.CreateNotExisted;
        existing.UpdateExisted = settings.UpdateExisted;
        existing.IssueTypesCsv = settings.IssueTypesCsv;
        existing.LabelsCsv = settings.LabelsCsv;
        existing.StatusesCsv = settings.StatusesCsv;
        existing.ExcludeCreateStatusesCsv = settings.ExcludeCreateStatusesCsv;
        existing.DateFilterMode = settings.DateFilterMode;
        existing.SinceFloorUtc = settings.SinceFloorUtc;
        if (reset)
        {
            existing.LastSyncedWatermarkUtc = null;
        }

        await db.SaveChangesAsync();
        return new JiraSyncKeySaveResult(existing, reset);
    }

    public async Task<JiraSyncKey> AddKeyAsync(string jiraKey)
    {
        var key = JiraProjectKeys.Normalize(jiraKey)
                  ?? throw new InvalidOperationException("Enter a Jira project key.");
        if (!ArtService.IsValidKey(key))
        {
            throw new InvalidOperationException(
                $"Not a valid Jira project key: {key}. A key starts with a letter and has up to {ArtJiraKey.MaxKeyLength} letters, digits or underscores.");
        }

        await using var db = await _ctx.CreateDbContextAsync();
        if (await db.JiraSyncKeys.AnyAsync(k => k.JiraKey == key))
        {
            throw new InvalidOperationException($"Jira key {key} is already on the list.");
        }

        var artNames = await ArtNamesUsingAsync(db, key);
        if (artNames.Count > 0)
        {
            throw new InvalidOperationException(
                $"Jira key {key} is already on the list: {string.Join(", ", artNames)} use{(artNames.Count == 1 ? "s" : "")} it.");
        }

        var row = new JiraSyncKey { JiraKey = key };
        db.JiraSyncKeys.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    public async Task<JiraSyncKeyItemCounts> CountSyncedItemsAsync(string jiraKey)
    {
        var key = JiraProjectKeys.Normalize(jiraKey) ?? string.Empty;
        await using var db = await _ctx.CreateDbContextAsync();
        return new JiraSyncKeyItemCounts(
            await db.Features.CountAsync(x => x.ProjectKey == key),
            await db.BusinessOutcomes.CountAsync(x => x.ProjectKey == key),
            await db.PortfolioEpics.CountAsync(x => x.ProjectKey == key),
            await db.StrategicObjectives.CountAsync(x => x.ProjectKey == key));
    }

    public async Task RemoveKeyAsync(string jiraKey)
    {
        var key = JiraProjectKeys.Normalize(jiraKey)
                  ?? throw new InvalidOperationException("Enter a Jira project key.");

        await using var db = await _ctx.CreateDbContextAsync();
        var artNames = await ArtNamesUsingAsync(db, key);
        if (artNames.Count > 0)
        {
            throw new InvalidOperationException(
                $"Jira key {key} can't be removed: {string.Join(", ", artNames)} use{(artNames.Count == 1 ? "s" : "")} it. Remove it from the ART first.");
        }

        var row = await db.JiraSyncKeys.FirstOrDefaultAsync(k => k.JiraKey == key);
        if (row is null)
        {
            return;
        }

        db.JiraSyncKeys.Remove(row);
        db.JiraLabelCaches.RemoveRange(db.JiraLabelCaches.Where(c => c.CacheKey == key));
        await db.SaveChangesAsync();
    }

    public async Task ResetWatermarkAsync(string jiraKey)
    {
        var key = JiraProjectKeys.Normalize(jiraKey) ?? string.Empty;
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.JiraSyncKeys.FirstOrDefaultAsync(k => k.JiraKey == key);
        if (existing is not null)
        {
            existing.LastSyncedWatermarkUtc = null;
            await db.SaveChangesAsync();
        }
    }

    public async Task<bool> IsServiceAccountConnectedAsync()
    {
        var settings = await GetSettingsAsync();
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.JiraTokens.AnyAsync(t => t.UserName == settings.ServiceAccountUserName);
    }

    public async Task<List<JiraSyncHistory>> GetHistoryAsync(int maxRows = 100)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.JiraSyncHistory
            .AsNoTracking()
            .OrderByDescending(h => h.StartedAt)
            .Take(maxRows)
            .ToListAsync();
    }

    public DateTime CalculateNextRun(JiraSyncSettings settings, DateTime fromUtc)
    {
        return fromUtc.AddMinutes(Math.Max(1, settings.CycleCooldownMinutes));
    }

    private static TimeZoneInfo ResolveJiraTimeZone(JiraSyncSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.JiraTimeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(settings.JiraTimeZoneId);
            }
            catch (Exception)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private static async Task<List<string>> ArtNamesUsingAsync(EstimationDbContext db, string key) =>
        await db.CapitalProjectJiraKeys
            .Where(k => k.JiraKey == key)
            .Select(k => k.Art.Name)
            .OrderBy(n => n)
            .ToListAsync();

    private static bool NeedsRefetch(JiraSyncKey before, JiraSyncKey after) =>
        (!before.CreateNotExisted && after.CreateNotExisted)
        || (!before.UpdateExisted && after.UpdateExisted)
        || !SameValues(before.IssueTypesCsv, after.IssueTypesCsv)
        || !SameValues(before.LabelsCsv, after.LabelsCsv)
        || !SameValues(before.StatusesCsv, after.StatusesCsv)
        || !SameValues(before.ExcludeCreateStatusesCsv, after.ExcludeCreateStatusesCsv)
        || before.DateFilterMode != after.DateFilterMode
        || before.SinceFloorUtc != after.SinceFloorUtc;

    private static bool SameValues(string? a, string? b) =>
        JiraListValues.Parse(a).SetEquals(JiraListValues.Parse(b));

    private sealed class KeyRun
    {
        public required JiraSyncKey Key { get; init; }
        public required IReadOnlyList<string> IssueTypes { get; init; }
        public required DateTime? Since { get; init; }
        public required DateTime? LoadedWatermark { get; init; }
        public int Fetched { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int ExcludedFromCreate { get; set; }
        public List<string> CreatedKeys { get; } = [];
        public List<string> UpdatedKeys { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<ArtMatch> NewFeatures { get; } = [];
        public string? Error { get; set; }
    }

    public async Task<JiraSyncHistory> RunSyncAsync(string triggeredBy, CancellationToken cancellationToken = default)
    {
        var history = new JiraSyncHistory
        {
            StartedAt = DateTime.UtcNow,
            Status = "Running",
            TriggeredBy = triggeredBy,
        };

        await using (var db = await _ctx.CreateDbContextAsync(cancellationToken))
        {
            db.JiraSyncHistory.Add(history);
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await using var db = await _ctx.CreateDbContextAsync(cancellationToken);

            var settings = await db.JiraSyncSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
                           ?? new JiraSyncSettings();
            var serviceUser = string.IsNullOrWhiteSpace(settings.ServiceAccountUserName)
                ? JiraSyncSettings.DefaultServiceAccountUserName
                : settings.ServiceAccountUserName;

            var connected = await db.JiraTokens.AnyAsync(t => t.UserName == serviceUser, cancellationToken);
            if (!connected)
            {
                throw new InvalidOperationException(
                    $"Jira service account '{serviceUser}' is not connected. Connect it on the Jira Sync admin page.");
            }

            var jiraTz = ResolveJiraTimeZone(settings);
            var runStartUtc = DateTime.UtcNow;

            var runMatcher = await ArtMatcher.LoadAsync(db);
            var artNames = await db.CapitalProjects.AsNoTracking()
                .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
            var keys = await db.JiraSyncKeys
                .Where(k => k.CreateNotExisted || k.UpdateExisted)
                .OrderBy(k => k.JiraKey)
                .ToListAsync(cancellationToken);

            int processed = 0, fetched = 0, created = 0, updated = 0, skipped = 0, failed = 0;
            var keyLines = new List<string>();
            var requests = new List<string>();
            var createdKeys = new List<string>();
            var updatedKeys = new List<string>();
            var warnings = new List<string>();
            var runs = new List<KeyRun>();

            foreach (var key in keys)
            {
                var issueTypes = SplitCsv(key.IssueTypesCsv);
                if (issueTypes.Count == 0)
                {
                    keyLines.Add($"{key.JiraKey}: skipped — no issue types selected");
                    continue;
                }

                var watermarkInJira = key.LastSyncedWatermarkUtc.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(key.LastSyncedWatermarkUtc.Value, DateTimeKind.Utc), jiraTz)
                    : (DateTime?)null;

                runs.Add(new KeyRun
                {
                    Key = key,
                    IssueTypes = issueTypes,
                    Since = MaxDate(key.SinceFloorUtc, watermarkInJira),
                    LoadedWatermark = key.LastSyncedWatermarkUtc,
                });
            }

            foreach (var type in PassOrder)
            {
                foreach (var run in runs.Where(r => r.Error is null))
                {
                    var types = run.IssueTypes
                        .Where(t => IJiraHierarchySyncService.MapEntityType(t) == type)
                        .ToList();
                    if (types.Count == 0)
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var jql = BuildJql(run.Key.JiraKey, types, SplitCsv(run.Key.LabelsCsv),
                            SplitCsv(run.Key.StatusesCsv), run.Key.DateFilterMode, run.Since);
                        requests.Add($"{run.Key.JiraKey}: {jql}");

                        var issues = (await _jira.SearchIssuesAsync(serviceUser, jql))
                            .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.Last())
                            .ToList();
                        run.Fetched += issues.Count;
                        fetched += issues.Count;

                        var ofType = issues
                            .Where(i => IJiraHierarchySyncService.MapEntityType(i.IssueType) == type)
                            .ToList();
                        skipped += issues.Count - ofType.Count;

                        await ApplyAsync(db, run, type, ofType, runMatcher, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        run.Error = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
                    }
                }
            }

            foreach (var run in runs)
            {
                created += run.Created;
                updated += run.Updated;
                createdKeys.AddRange(run.CreatedKeys);
                updatedKeys.AddRange(run.UpdatedKeys);
                warnings.AddRange(run.Warnings);

                if (run.Error is not null)
                {
                    failed++;
                    keyLines.Add(run.Created > 0 || run.Updated > 0
                        ? $"{run.Key.JiraKey}: FAILED after creating {run.Created}, updating {run.Updated} — {run.Error}"
                        : $"{run.Key.JiraKey}: FAILED — {run.Error}");
                    continue;
                }

                processed++;
                run.Key.LastSyncedWatermarkUtc = runStartUtc;
                keyLines.Add(DescribeRun(run, artNames));
            }

            keyLines.AddRange(await ForgetWatermarksChangedDuringRunAsync(
                db, runMatcher, runs.Where(r => r.Error is null).ToList(), cancellationToken));
            await db.SaveChangesAsync(cancellationToken);

            var tracked = await db.JiraSyncHistory.FirstAsync(h => h.Id == history.Id, cancellationToken);
            tracked.FinishedAt = DateTime.UtcNow;
            tracked.Status = failed > 0 && processed == 0 ? "Failed" : "Success";
            tracked.ProjectsProcessed = processed;
            tracked.IssuesFetched = fetched;
            tracked.Created = created;
            tracked.Updated = updated;
            tracked.Skipped = skipped;
            tracked.Failed = failed;
            tracked.Message = BuildMessage(processed, fetched, created, updated, skipped, failed,
                keyLines, requests, createdKeys, updatedKeys, warnings);

            var settingsRow = await db.JiraSyncSettings.FirstOrDefaultAsync(cancellationToken);
            if (settingsRow is not null)
            {
                settingsRow.LastRunAt = DateTime.UtcNow;
                settingsRow.NextRunAt = settingsRow.Enabled ? CalculateNextRun(settingsRow, DateTime.UtcNow) : null;
            }

            await db.SaveChangesAsync(cancellationToken);

            return await db.JiraSyncHistory.AsNoTracking().FirstAsync(h => h.Id == history.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            await using var db = await _ctx.CreateDbContextAsync(CancellationToken.None);
            var tracked = await db.JiraSyncHistory.FirstAsync(h => h.Id == history.Id, CancellationToken.None);
            tracked.FinishedAt = DateTime.UtcNow;
            tracked.Status = "Failed";
            tracked.Message = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            return tracked;
        }
    }

    private async Task ApplyAsync(
        EstimationDbContext db,
        KeyRun run,
        HierarchyEntityType type,
        List<JiraIssueResponse> issues,
        ArtMatcher matcher,
        CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
        {
            return;
        }

        var existing = await ExistingJiraIdsAsync(ExistingQuery(db, type), issues.Select(i => i.Key).ToList(), cancellationToken);
        var excludeCreateStatuses = SplitCsv(run.Key.ExcludeCreateStatusesCsv).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var partition = Partition(issues, existing, run.Key, excludeCreateStatuses);
        run.ExcludedFromCreate += partition.ExcludedFromCreate;
        if (partition.ToApply.Count == 0)
        {
            return;
        }

        var result = await _writer.WriteAsync(type, partition.ToApply, background: true, cancellationToken);
        run.Created += result.Created;
        run.Updated += result.Updated;
        run.Warnings.AddRange(result.Warnings);

        foreach (var issue in partition.ToApply)
        {
            if (existing.Contains(issue.Key))
            {
                run.UpdatedKeys.Add(issue.Key);
                continue;
            }

            run.CreatedKeys.Add(issue.Key);
            if (type == HierarchyEntityType.Feature)
            {
                run.NewFeatures.Add(matcher.Match(issue.ToMatchFacts()));
            }
        }
    }

    private static string DescribeRun(KeyRun run, IReadOnlyDictionary<int, string> artNames)
    {
        var line = $"{run.Key.JiraKey}: fetched {run.Fetched}, created {run.Created}, updated {run.Updated}";
        if (run.ExcludedFromCreate > 0)
        {
            line += $", {run.ExcludedFromCreate} not created (status)";
        }
        if (run.Warnings.Count > 0)
        {
            line += $", {run.Warnings.Count} warning(s)";
        }
        if (run.NewFeatures.Count > 0)
        {
            var byArt = run.NewFeatures
                .GroupBy(m => ArtOutcome(m, artNames))
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key} {g.Count()}");
            line += $"; new Features by ART: {string.Join(", ", byArt)}";
        }
        return line;
    }

    private static string ArtOutcome(ArtMatch match, IReadOnlyDictionary<int, string> artNames) => match.Kind switch
    {
        ArtMatchKind.Matched when match.ArtId is { } id => artNames.TryGetValue(id, out var name) ? name : $"ART {id}",
        ArtMatchKind.Unmatched => "no matching ART",
        ArtMatchKind.Ambiguous => "several ARTs",
        _ => "no ART",
    };

    private static string BuildMessage(
        int processed, int fetched, int created, int updated, int skipped, int failed,
        List<string> keyLines, List<string> requests, List<string> createdKeys,
        List<string> updatedKeys, List<string> warnings)
    {
        var sb = new StringBuilder();
        sb.Append($"{processed} key(s) processed, {fetched} fetched, {created} created, {updated} updated, ")
          .Append($"{skipped} issue(s) skipped, {failed} key(s) failed.");

        AppendBullets(sb, "Keys", keyLines);
        AppendBullets(sb, "Jira requests", requests);
        AppendKeyList(sb, "Created", createdKeys);
        AppendKeyList(sb, "Updated", updatedKeys);
        AppendBullets(sb, "Warnings", warnings);

        var message = sb.ToString();
        const int cap = 200_000;
        return message.Length > cap ? message[..cap] + " … (truncated)" : message;
    }

    private static void AppendKeyList(StringBuilder sb, string title, List<string> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }
        sb.AppendLine().AppendLine()
          .Append(title).Append(" (").Append(keys.Count).Append("): ")
          .Append(string.Join(", ", keys));
    }

    private static void AppendBullets(StringBuilder sb, string title, List<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        sb.AppendLine().AppendLine().Append(title).Append(" (").Append(items.Count).Append("):");
        foreach (var item in items)
        {
            sb.AppendLine().Append("  • ").Append(item);
        }
    }

    private static IQueryable<string?> ExistingQuery(EstimationDbContext db, HierarchyEntityType type)
    {
        return type switch
        {
            HierarchyEntityType.Feature => db.Features.Select(x => x.JiraId),
            HierarchyEntityType.BusinessOutcome => db.BusinessOutcomes.Select(x => x.JiraId),
            HierarchyEntityType.PortfolioEpic => db.PortfolioEpics.Select(x => x.JiraId),
            HierarchyEntityType.StrategicObjective => db.StrategicObjectives.Select(x => x.JiraId),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    private static async Task<HashSet<string>> ExistingJiraIdsAsync(
        IQueryable<string?> jiraIds, List<string> keys, CancellationToken cancellationToken)
    {
        var found = await jiraIds
            .Where(id => id != null && keys.Contains(id))
            .ToListAsync(cancellationToken);
        return found.Where(id => id is not null).Select(id => id!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record PartitionResult(List<JiraIssueResponse> ToApply, int ExcludedFromCreate);

    private static PartitionResult Partition(
        List<JiraIssueResponse> items,
        HashSet<string> existing,
        JiraSyncKey key,
        HashSet<string> excludeCreateStatuses)
    {
        var toApply = new List<JiraIssueResponse>();
        var excludedFromCreate = 0;

        foreach (var item in items)
        {
            if (existing.Contains(item.Key))
            {
                if (key.UpdateExisted)
                {
                    toApply.Add(item);
                }
                continue;
            }

            if (!key.CreateNotExisted)
            {
                continue;
            }

            if (item.Status is not null && excludeCreateStatuses.Contains(item.Status))
            {
                excludedFromCreate++;
                continue;
            }
            toApply.Add(item);
        }

        return new PartitionResult(toApply, excludedFromCreate);
    }

    private static async Task<List<string>> ForgetWatermarksChangedDuringRunAsync(
        EstimationDbContext db,
        ArtMatcher runMatcher,
        IReadOnlyList<KeyRun> synced,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        if (synced.Count == 0)
        {
            return lines;
        }

        var currentMatcher = await ArtMatcher.LoadAsync(db);
        var ids = synced.Select(r => r.Key.Id).ToList();
        var current = await db.JiraSyncKeys
            .AsNoTracking()
            .Where(k => ids.Contains(k.Id))
            .ToDictionaryAsync(k => k.Id, cancellationToken);

        foreach (var run in synced)
        {
            var key = run.Key;
            if (!current.TryGetValue(key.Id, out var now))
            {
                db.Entry(key).State = EntityState.Detached;
                lines.Add($"{key.JiraKey}: the key was removed during the sync");
                continue;
            }

            var resetMeanwhile = run.LoadedWatermark is not null && now.LastSyncedWatermarkUtc is null;
            var ownersChanged = !runMatcher.ArtsForKey(key.JiraKey).ToHashSet()
                .SetEquals(currentMatcher.ArtsForKey(key.JiraKey));
            if (resetMeanwhile || ownersChanged || NeedsRefetch(key, now))
            {
                key.LastSyncedWatermarkUtc = null;
                db.Entry(key).Property(k => k.LastSyncedWatermarkUtc).IsModified = true;
                lines.Add($"{key.JiraKey}: its settings, ARTs or watermark changed during the sync, so the next sync starts over");
            }
        }

        return lines;
    }

    private static string BuildJql(
        string projectKey,
        List<string> issueTypes,
        List<string> labels,
        List<string> statuses,
        JiraSyncDateFilterMode mode,
        DateTime? since)
    {
        var sb = new StringBuilder();
        sb.Append("project = ").Append(Quote(projectKey));

        if (issueTypes.Count > 0)
        {
            sb.Append(" AND issuetype in (").Append(QuoteList(issueTypes)).Append(')');
        }
        if (labels.Count > 0)
        {
            sb.Append(" AND labels in (").Append(QuoteList(labels)).Append(')');
        }
        if (statuses.Count > 0)
        {
            sb.Append(" AND status in (").Append(QuoteList(statuses)).Append(')');
        }

        var dateField = mode == JiraSyncDateFilterMode.Created ? "created" : "updated";
        if (since.HasValue)
        {
            var ts = since.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            sb.Append(" AND ").Append(dateField).Append(" >= ").Append(Quote(ts));
        }

        sb.Append(" ORDER BY created ASC, key ASC");
        return sb.ToString();
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string QuoteList(IEnumerable<string> values)
    {
        return string.Join(", ", values.Select(Quote));
    }

    private static List<string> SplitCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new List<string>();
        }
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static DateTime? MaxDate(DateTime? a, DateTime? b)
    {
        if (a is null)
        {
            return b;
        }
        if (b is null)
        {
            return a;
        }
        return a.Value > b.Value ? a : b;
    }
}
