using Estimation.Core.Administration.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Administration.Services;

public interface IHousekeepingService
{
    Task<IReadOnlyDictionary<HousekeepingTable, int>> CountJiraKeysAsync(CancellationToken cancellationToken = default);

    Task<HousekeepingCheckResult> CheckIssueTypesAsync(
        HousekeepingTable table,
        string jiraUserName,
        IProgress<HousekeepingCheckProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<HousekeepingRemoveResult> RemoveAsync(IReadOnlyCollection<HousekeepingItemKey> items, CancellationToken cancellationToken = default);
}

public class HousekeepingService : IHousekeepingService
{
    public const int BatchSize = 100;

    private static readonly TimeSpan DefaultPause = TimeSpan.FromMilliseconds(300);

    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IJiraIssueTypeLookup _jira;
    private readonly TimeSpan _pause;

    public HousekeepingService(
        IDbContextFactory<EstimationDbContext> ctx,
        IJiraIssueTypeLookup jira,
        TimeSpan? pauseBetweenRequests = null)
    {
        _ctx = ctx;
        _jira = jira;
        _pause = pauseBetweenRequests ?? DefaultPause;
    }

    private sealed record Row(int Id, string JiraKey, string Summary);

    public async Task<IReadOnlyDictionary<HousekeepingTable, int>> CountJiraKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);
        var counts = new Dictionary<HousekeepingTable, int>();
        foreach (var table in HousekeepingTables.All)
        {
            counts[table] = JiraIssueIds.Keys((await LoadRowsAsync(db, table, cancellationToken)).Select(r => r.JiraKey)).Count;
        }
        return counts;
    }

    public async Task<HousekeepingCheckResult> CheckIssueTypesAsync(
        HousekeepingTable table,
        string jiraUserName,
        IProgress<HousekeepingCheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<Row> rows;
        await using (var db = await _ctx.CreateDbContextAsync(cancellationToken))
        {
            rows = await LoadRowsAsync(db, table, cancellationToken);
        }

        var keys = JiraIssueIds.Keys(rows.Select(r => r.JiraKey));
        var found = new Dictionary<string, JiraIssueTypeInfo>(StringComparer.OrdinalIgnoreCase);
        var notFound = new List<string>();
        var checkedCount = 0;
        var cancelled = false;
        string? error = null;

        progress?.Report(new HousekeepingCheckProgress(0, keys.Count));

        try
        {
            var first = true;
            foreach (var batch in keys.Chunk(BatchSize))
            {
                if (!first)
                {
                    await Task.Delay(_pause, cancellationToken);
                }
                first = false;

                var issues = await _jira.SearchAsync(jiraUserName, batch, cancellationToken);
                await ResolveBatchAsync(jiraUserName, batch, issues, found, cancellationToken);
                notFound.AddRange(batch.Where(k => !found.ContainsKey(k)));
                checkedCount += batch.Length;
                progress?.Report(new HousekeepingCheckProgress(checkedCount, keys.Count));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Housekeeping issue type check of {Table} stopped after {Checked} of {Total} issues",
                table, checkedCount, keys.Count);
            error = ex.Message.Length > 300 ? ex.Message[..300] + "…" : ex.Message;
        }

        var wrong = rows
            .Where(r => found.TryGetValue(r.JiraKey, out var issue) && HousekeepingTables.IsWrongIssueType(table, issue.IssueType))
            .Select(r => (Row: r, Jira: found[r.JiraKey]))
            .ToList();

        Dictionary<HousekeepingTable, HashSet<string>> copies;
        await using (var db = await _ctx.CreateDbContextAsync(CancellationToken.None))
        {
            copies = await FindCopiesAsync(db, wrong.Select(w => w.Jira).ToList(), CancellationToken.None);
        }

        var items = wrong
            .Select(w =>
            {
                var issueTypeTable = HousekeepingTables.ForIssueType(w.Jira.IssueType);
                var exists = issueTypeTable is { } t
                    && copies.TryGetValue(t, out var copyKeys)
                    && copyKeys.Contains(w.Jira.Key);
                var moved = !string.Equals(w.Row.JiraKey, w.Jira.Key, StringComparison.OrdinalIgnoreCase);
                return new HousekeepingItem(table, w.Row.Id, w.Row.JiraKey, moved ? w.Jira.Key : null, w.Row.Summary,
                    w.Jira.IssueType!.Trim(), w.Jira.Status, issueTypeTable, exists);
            })
            .OrderBy(i => i.JiraKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id)
            .ToList();

        var movedKeys = found
            .Where(f => !string.Equals(f.Key, f.Value.Key, StringComparison.OrdinalIgnoreCase))
            .Select(f => new HousekeepingMovedKey(f.Key, f.Value.Key))
            .OrderBy(m => m.StoredKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new HousekeepingCheckResult(
            table,
            keys.Count,
            checkedCount,
            items,
            notFound.Order(StringComparer.OrdinalIgnoreCase).ToList(),
            movedKeys,
            cancelled,
            error);
    }

    public async Task<HousekeepingRemoveResult> RemoveAsync(IReadOnlyCollection<HousekeepingItemKey> items, CancellationToken cancellationToken = default)
    {
        var requested = items.DistinctBy(i => (i.Table, i.Id)).ToList();
        if (requested.Count == 0)
        {
            return new HousekeepingRemoveResult(0, 0);
        }

        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);

        var objectiveIds = IdsOf(requested, HousekeepingTable.StrategicObjectives);
        var objectives = StillMatching(requested, HousekeepingTable.StrategicObjectives, x => x.Id,
            await db.StrategicObjectives.Where(x => objectiveIds.Contains(x.Id)).ToListAsync(cancellationToken));
        var removedObjectiveIds = objectives.Select(x => x.Id).ToList();

        var epicIds = IdsOf(requested, HousekeepingTable.PortfolioEpics);
        var epics = StillMatching(requested, HousekeepingTable.PortfolioEpics, x => x.Id,
            await db.PortfolioEpics.Where(x => epicIds.Contains(x.Id)).ToListAsync(cancellationToken));
        var removedEpicIds = epics.Select(x => x.Id).ToList();

        var outcomeIds = IdsOf(requested, HousekeepingTable.BusinessOutcomes);
        var outcomes = StillMatching(requested, HousekeepingTable.BusinessOutcomes, x => x.Id,
            await db.BusinessOutcomes.Where(x => outcomeIds.Contains(x.Id)).ToListAsync(cancellationToken));

        var featureIds = IdsOf(requested, HousekeepingTable.Features);
        var features = StillMatching(requested, HousekeepingTable.Features, x => x.Id,
            await db.Features.Where(x => featureIds.Contains(x.Id)).ToListAsync(cancellationToken));

        if (removedObjectiveIds.Count > 0)
        {
            db.CapitalProjectStrategicObjectives.RemoveRange(await db.CapitalProjectStrategicObjectives
                .Where(l => removedObjectiveIds.Contains(l.StrategicObjectiveId))
                .ToListAsync(cancellationToken));
        }

        if (removedObjectiveIds.Count > 0 || removedEpicIds.Count > 0)
        {
            db.StrategicObjectivePortfolioEpics.RemoveRange(await db.StrategicObjectivePortfolioEpics
                .Where(l => removedObjectiveIds.Contains(l.StrategicObjectiveId) || removedEpicIds.Contains(l.PortfolioEpicId))
                .ToListAsync(cancellationToken));
        }

        db.Features.RemoveRange(features);
        db.BusinessOutcomes.RemoveRange(outcomes);
        db.PortfolioEpics.RemoveRange(epics);
        db.StrategicObjectives.RemoveRange(objectives);

        await db.SaveChangesAsync(cancellationToken);

        var removed = objectives.Count + epics.Count + outcomes.Count + features.Count;
        return new HousekeepingRemoveResult(removed, requested.Count - removed);
    }

    private async Task ResolveBatchAsync(
        string jiraUserName,
        string[] batch,
        IReadOnlyList<JiraIssueTypeInfo> issues,
        Dictionary<string, JiraIssueTypeInfo> found,
        CancellationToken cancellationToken)
    {
        var requested = new HashSet<string>(batch, StringComparer.OrdinalIgnoreCase);
        var others = new List<JiraIssueTypeInfo>();
        foreach (var issue in issues)
        {
            if (requested.Contains(issue.Key))
            {
                found[issue.Key] = issue;
            }
            else
            {
                others.Add(issue);
            }
        }

        if (others.Count == 0)
        {
            return;
        }

        var missing = batch.Where(k => !found.ContainsKey(k)).ToList();
        if (missing.Count == 1 && others.Count == 1)
        {
            found[missing[0]] = others[0];
            return;
        }

        foreach (var key in missing)
        {
            await Task.Delay(_pause, cancellationToken);
            if (await _jira.GetAsync(jiraUserName, key, cancellationToken) is { } issue)
            {
                found[key] = issue;
            }
        }
    }

    private static async Task<List<Row>> LoadRowsAsync(EstimationDbContext db, HousekeepingTable table, CancellationToken cancellationToken)
    {
        var rows = table switch
        {
            HousekeepingTable.StrategicObjectives => await db.StrategicObjectives.AsNoTracking()
                .Where(x => x.JiraId != null)
                .Select(x => new Row(x.Id, x.JiraId!, x.Summary))
                .ToListAsync(cancellationToken),
            HousekeepingTable.PortfolioEpics => await db.PortfolioEpics.AsNoTracking()
                .Where(x => x.JiraId != null)
                .Select(x => new Row(x.Id, x.JiraId!, x.Summary))
                .ToListAsync(cancellationToken),
            HousekeepingTable.BusinessOutcomes => await db.BusinessOutcomes.AsNoTracking()
                .Where(x => x.JiraId != null)
                .Select(x => new Row(x.Id, x.JiraId!, x.Summary))
                .ToListAsync(cancellationToken),
            _ => await db.Features.AsNoTracking()
                .Where(x => x.JiraId != null)
                .Select(x => new Row(x.Id, x.JiraId!, x.Summary))
                .ToListAsync(cancellationToken),
        };

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.JiraKey))
            .Select(r => r with { JiraKey = r.JiraKey.Trim() })
            .ToList();
    }

    private static List<int> IdsOf(IEnumerable<HousekeepingItemKey> items, HousekeepingTable table) =>
        items.Where(i => i.Table == table).Select(i => i.Id).Distinct().ToList();

    private static List<T> StillMatching<T>(
        IEnumerable<HousekeepingItemKey> requested, HousekeepingTable table, Func<T, int> id, List<T> rows) where T : JiraIssue
    {
        var keysById = requested
            .Where(r => r.Table == table)
            .ToDictionary(r => r.Id, r => r.JiraKey.Trim());
        return rows
            .Where(x => keysById.TryGetValue(id(x), out var key)
                && string.Equals(x.JiraId?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static async Task<Dictionary<HousekeepingTable, HashSet<string>>> FindCopiesAsync(
        EstimationDbContext db, IReadOnlyList<JiraIssueTypeInfo> issues, CancellationToken cancellationToken)
    {
        var result = new Dictionary<HousekeepingTable, HashSet<string>>();

        foreach (var group in issues.GroupBy(i => HousekeepingTables.ForIssueType(i.IssueType)))
        {
            if (group.Key is not { } table)
            {
                continue;
            }

            var keys = JiraIssueIds.Keys(group.Select(i => i.Key));
            result[table] = table switch
            {
                HousekeepingTable.StrategicObjectives => await KeysInTableAsync(db.StrategicObjectives, keys, cancellationToken),
                HousekeepingTable.PortfolioEpics => await KeysInTableAsync(db.PortfolioEpics, keys, cancellationToken),
                HousekeepingTable.BusinessOutcomes => await KeysInTableAsync(db.BusinessOutcomes, keys, cancellationToken),
                _ => await KeysInTableAsync(db.Features, keys, cancellationToken),
            };
        }

        return result;
    }

    private static async Task<HashSet<string>> KeysInTableAsync<T>(
        IQueryable<T> query, List<string> keys, CancellationToken cancellationToken) where T : JiraIssue
    {
        var found = await query.AsNoTracking()
            .Where(x => x.JiraId != null && keys.Contains(x.JiraId))
            .Select(x => x.JiraId!)
            .ToListAsync(cancellationToken);

        return found.Select(k => k.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
