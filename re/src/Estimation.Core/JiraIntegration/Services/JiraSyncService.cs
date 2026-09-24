using System.Globalization;
using System.Text;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.JiraIntegration.Services;

public interface IJiraSyncService
{
    Task<JiraSyncSettings> GetSettingsAsync();
    Task<JiraSyncSettings> StartAsync(int cooldownMinutes);
    Task<JiraSyncSettings> StopAsync();
    Task UpdateJiraTimeZoneAsync(string? timeZoneId);
    Task<List<JiraSyncProjectSettings>> GetProjectSettingsAsync();
    Task<JiraSyncProjectSettings> UpsertProjectSettingsAsync(JiraSyncProjectSettings settings);
    Task ResetWatermarkAsync(int capitalProjectId);
    Task<bool> IsServiceAccountConnectedAsync();
    Task<List<JiraSyncHistory>> GetHistoryAsync(int maxRows = 100);
    Task<JiraSyncHistory> RunSyncAsync(string triggeredBy, CancellationToken cancellationToken = default);
    DateTime CalculateNextRun(JiraSyncSettings settings, DateTime fromUtc);
}

public class JiraSyncService : IJiraSyncService
{
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

    public async Task<List<JiraSyncProjectSettings>> GetProjectSettingsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.JiraSyncProjectSettings
            .Include(p => p.Art)
            .AsNoTracking()
            .OrderBy(p => p.Art!.Name)
            .ToListAsync();
    }

    public async Task<JiraSyncProjectSettings> UpsertProjectSettingsAsync(JiraSyncProjectSettings settings)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.JiraSyncProjectSettings
            .FirstOrDefaultAsync(p => p.CapitalProjectId == settings.CapitalProjectId);

        if (existing is null)
        {
            db.JiraSyncProjectSettings.Add(settings);
        }
        else
        {
            existing.CreateNotExisted = settings.CreateNotExisted;
            existing.UpdateExisted = settings.UpdateExisted;
            existing.IssueTypesCsv = settings.IssueTypesCsv;
            existing.LabelsCsv = settings.LabelsCsv;
            existing.StatusesCsv = settings.StatusesCsv;
            existing.ExcludeCreateStatusesCsv = settings.ExcludeCreateStatusesCsv;
            existing.DateFilterMode = settings.DateFilterMode;
            existing.SinceFloorUtc = settings.SinceFloorUtc;
        }

        await db.SaveChangesAsync();
        return await db.JiraSyncProjectSettings.AsNoTracking()
            .FirstAsync(p => p.CapitalProjectId == settings.CapitalProjectId);
    }

    public async Task ResetWatermarkAsync(int capitalProjectId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.JiraSyncProjectSettings
            .FirstOrDefaultAsync(p => p.CapitalProjectId == capitalProjectId);
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

            var projects = await db.JiraSyncProjectSettings
                .Include(p => p.Art)
                .Where(p => (p.CreateNotExisted || p.UpdateExisted)
                            && p.Art != null
                            && p.Art.JiraKey != null
                            && p.Art.JiraKey != "")
                .ToListAsync(cancellationToken);

            int processed = 0, fetched = 0, created = 0, updated = 0, skipped = 0, failed = 0;
            var projectLines = new List<string>();
            var requests = new List<string>();
            var createdKeys = new List<string>();
            var updatedKeys = new List<string>();
            var warnings = new List<string>();

            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var projectKey = project.Art!.JiraKey!;
                var issueTypes = SplitCsv(project.IssueTypesCsv);
                if (issueTypes.Count == 0)
                {
                    projectLines.Add($"{projectKey}: skipped — no issue types selected");
                    continue;
                }

                var runStartUtc = DateTime.UtcNow;
                var watermarkInJira = project.LastSyncedWatermarkUtc.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(project.LastSyncedWatermarkUtc.Value, DateTimeKind.Utc), jiraTz)
                    : (DateTime?)null;
                var since = MaxDate(project.SinceFloorUtc, watermarkInJira);

                try
                {
                    var jql = BuildJql(projectKey, issueTypes, SplitCsv(project.LabelsCsv),
                        SplitCsv(project.StatusesCsv), project.DateFilterMode, since);
                    requests.Add($"{projectKey}: {jql}");

                    var issues = await _jira.SearchIssuesAsync(serviceUser, jql);
                    fetched += issues.Count;

                    var byType = RouteByEntityType(issues, ref skipped);

                    var result = await ApplyProjectAsync(db, project, projectKey, byType, cancellationToken);
                    created += result.Created;
                    updated += result.Updated;
                    createdKeys.AddRange(result.CreatedKeys);
                    updatedKeys.AddRange(result.UpdatedKeys);
                    warnings.AddRange(result.Warnings);

                    project.LastSyncedWatermarkUtc = runStartUtc;
                    processed++;

                    var line = $"{projectKey}: fetched {issues.Count}, created {result.Created}, updated {result.Updated}";
                    if (result.ExcludedFromCreate > 0)
                    {
                        line += $", {result.ExcludedFromCreate} not created (status)";
                    }
                    if (result.Warnings.Count > 0)
                    {
                        line += $", {result.Warnings.Count} warning(s)";
                    }
                    projectLines.Add(line);
                }
                catch (Exception ex)
                {
                    failed++;
                    var detail = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
                    projectLines.Add($"{projectKey}: FAILED — {detail}");
                }
            }

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
                projectLines, requests, createdKeys, updatedKeys, warnings);

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

    private static Dictionary<HierarchyEntityType, List<JiraIssueResponse>> RouteByEntityType(
        List<JiraIssueResponse> issues, ref int skipped)
    {
        var byType = new Dictionary<HierarchyEntityType, List<JiraIssueResponse>>();
        foreach (var issue in issues)
        {
            var type = IJiraHierarchySyncService.MapEntityType(issue.IssueType);
            if (type is null)
            {
                skipped++;
                continue;
            }

            if (!byType.TryGetValue(type.Value, out var list))
            {
                list = new List<JiraIssueResponse>();
                byType[type.Value] = list;
            }
            list.Add(issue);
        }
        return byType;
    }

    private sealed record ProjectApplyResult(
        int Created, int Updated, List<string> CreatedKeys, List<string> UpdatedKeys,
        List<string> Warnings, int ExcludedFromCreate);

    private async Task<ProjectApplyResult> ApplyProjectAsync(
        EstimationDbContext db,
        JiraSyncProjectSettings project,
        string projectKey,
        Dictionary<HierarchyEntityType, List<JiraIssueResponse>> byType,
        CancellationToken cancellationToken)
    {
        var excludeCreateStatuses = SplitCsv(project.ExcludeCreateStatusesCsv).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filtered = new Dictionary<HierarchyEntityType, List<JiraIssueResponse>>();
        var createdKeys = new List<string>();
        var updatedKeys = new List<string>();
        var excludedFromCreate = 0;

        foreach (var (type, issues) in byType)
        {
            var keys = issues.Select(i => i.Key).ToList();
            var existing = await ExistingJiraIdsAsync(ExistingQuery(db, type), keys, cancellationToken);
            var (toApply, excluded) = Partition(issues, existing, project, excludeCreateStatuses);
            excludedFromCreate += excluded;
            foreach (var issue in toApply)
            {
                (existing.Contains(issue.Key) ? updatedKeys : createdKeys).Add(issue.Key);
            }
            if (toApply.Count > 0)
            {
                filtered[type] = toApply;
            }
        }

        if (filtered.Count == 0)
        {
            return new ProjectApplyResult(0, 0, createdKeys, updatedKeys, new List<string>(), excludedFromCreate);
        }

        var result = await _writer.WriteProjectAsync(
            project.CapitalProjectId, projectKey, filtered, background: true, cancellationToken);

        return new ProjectApplyResult(
            result.Created, result.Updated, createdKeys, updatedKeys, result.Warnings.ToList(), excludedFromCreate);
    }

    private static string BuildMessage(
        int processed, int fetched, int created, int updated, int skipped, int failed,
        List<string> projectLines, List<string> requests, List<string> createdKeys,
        List<string> updatedKeys, List<string> warnings)
    {
        var sb = new StringBuilder();
        sb.Append($"{processed} project(s) processed, {fetched} fetched, {created} created, {updated} updated, ")
          .Append($"{skipped} issue(s) skipped, {failed} project(s) failed.");

        AppendBullets(sb, "Projects", projectLines);
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

    private static (List<JiraIssueResponse> ToApply, int ExcludedFromCreate) Partition(
        List<JiraIssueResponse> items,
        HashSet<string> existing,
        JiraSyncProjectSettings project,
        HashSet<string> excludeCreateStatuses)
    {
        var toApply = new List<JiraIssueResponse>();
        var excludedFromCreate = 0;

        foreach (var item in items)
        {
            var exists = existing.Contains(item.Key);
            if (exists)
            {
                if (!project.UpdateExisted)
                {
                    continue;
                }
            }
            else
            {
                if (!project.CreateNotExisted)
                {
                    continue;
                }
                if (item.Status is not null && excludeCreateStatuses.Contains(item.Status))
                {
                    excludedFromCreate++;
                    continue;
                }
            }
            toApply.Add(item);
        }

        return (toApply, excludedFromCreate);
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

        sb.Append(" ORDER BY ").Append(dateField).Append(" ASC");
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
