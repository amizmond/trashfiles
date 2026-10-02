using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.JiraIntegration.Services;

public sealed record IssueLinkSnapshot(string IssueKey, IReadOnlyList<JiraIssueLink>? Links);

public static class IssueLinkStore
{
    public static async Task<bool> ApplyAsync(
        IDbContextFactory<EstimationDbContext> ctx,
        IEnumerable<IssueLinkSnapshot> snapshots,
        bool prune,
        CancellationToken cancellationToken = default)
    {
        var linksByKey = new Dictionary<string, IReadOnlyList<JiraIssueLink>>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            var key = snapshot.IssueKey?.Trim();
            if (snapshot.Links is null || string.IsNullOrEmpty(key) || key.Length > IssueLink.MaxKeyLength)
            {
                continue;
            }
            linksByKey[key] = snapshot.Links;
        }

        if (linksByKey.Count == 0)
        {
            return true;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = await ctx.CreateDbContextAsync(cancellationToken);
                await StageAsync(db, linksByKey, prune, cancellationToken);
                if (db.ChangeTracker.HasChanges())
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                return true;
            }
            catch (Exception ex) when (attempt == 1 && ex is not OperationCanceledException)
            {
                Log.Information(ex, "Storing linked issues failed once and is tried again");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Linked issues of {Count} issue(s) could not be stored", linksByKey.Count);
                return false;
            }
        }
    }

    public static async Task<bool> ForgetAsync(
        IDbContextFactory<EstimationDbContext> ctx,
        IReadOnlyCollection<string> jiraLinkIds,
        CancellationToken cancellationToken = default)
    {
        var ids = jiraLinkIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct().ToList();
        if (ids.Count == 0)
        {
            return true;
        }

        try
        {
            await using var db = await ctx.CreateDbContextAsync(cancellationToken);
            var rows = await db.IssueLinks.Where(l => ids.Contains(l.JiraLinkId)).ToListAsync(cancellationToken);
            if (rows.Count > 0)
            {
                db.IssueLinks.RemoveRange(rows);
                await db.SaveChangesAsync(cancellationToken);
            }
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "{Count} removed link(s) could not be deleted from the store", ids.Count);
            return false;
        }
    }

    private static async Task StageAsync(
        EstimationDbContext db,
        Dictionary<string, IReadOnlyList<JiraIssueLink>> linksByKey,
        bool prune,
        CancellationToken cancellationToken)
    {
        var keys = linksByKey.Keys.ToList();
        var ids = linksByKey.Values
            .SelectMany(links => links)
            .Where(Storable)
            .Select(link => link.Id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var rows = await db.IssueLinks
            .Where(l => keys.Contains(l.FromKey) || keys.Contains(l.ToKey) || ids.Contains(l.JiraLinkId))
            .ToListAsync(cancellationToken);

        var rowsById = new Dictionary<string, IssueLink>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!rowsById.TryAdd(row.JiraLinkId, row))
            {
                db.IssueLinks.Remove(row);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, links) in linksByKey)
        {
            foreach (var link in links.Where(Storable))
            {
                var id = link.Id.Trim();
                var otherKey = link.OtherKey.Trim();
                seen.Add(id);

                if (!rowsById.TryGetValue(id, out var row))
                {
                    row = new IssueLink { JiraLinkId = id };
                    db.IssueLinks.Add(row);
                    rowsById[id] = row;
                }

                row.TypeName = Fit(link.TypeName) ?? string.Empty;
                row.OutwardLabel = Fit(link.OutwardLabel);
                row.InwardLabel = Fit(link.InwardLabel);
                row.FromKey = link.IsOutward ? key : otherKey;
                row.ToKey = link.IsOutward ? otherKey : key;
            }
        }

        if (!prune)
        {
            return;
        }

        foreach (var row in rowsById.Values)
        {
            if (!seen.Contains(row.JiraLinkId)
                && (linksByKey.ContainsKey(row.FromKey.Trim()) || linksByKey.ContainsKey(row.ToKey.Trim())))
            {
                db.IssueLinks.Remove(row);
            }
        }
    }

    private static bool Storable(JiraIssueLink link)
    {
        var id = link.Id?.Trim();
        var otherKey = link.OtherKey?.Trim();
        return !string.IsNullOrEmpty(id) && id.Length <= IssueLink.MaxLinkIdLength
               && !string.IsNullOrEmpty(otherKey) && otherKey.Length <= IssueLink.MaxKeyLength;
    }

    private static string? Fit(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        return trimmed.Length > IssueLink.MaxTextLength ? trimmed[..IssueLink.MaxTextLength] : trimmed;
    }
}
