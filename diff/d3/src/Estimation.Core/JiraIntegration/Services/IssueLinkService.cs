using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.JiraIntegration.Services;

public interface IIssueLinkService
{
    Task LoadAsync(JiraIssue issue, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, IReadOnlyList<LinkedIssue>>> GetForIssuesAsync(
        IReadOnlyCollection<string> issueKeys, CancellationToken cancellationToken = default);

    Task<List<LinkedIssue>> FromJiraAsync(
        IReadOnlyList<JiraIssueLink> links, CancellationToken cancellationToken = default);

    Task<bool> ApplyAsync(
        IReadOnlyCollection<IssueLinkSnapshot> snapshots, bool prune, CancellationToken cancellationToken = default);
}

public class IssueLinkService : IIssueLinkService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public IssueLinkService(IDbContextFactory<EstimationDbContext> ctx)
    {
        _ctx = ctx;
    }

    public async Task LoadAsync(JiraIssue issue, CancellationToken cancellationToken = default)
    {
        var key = issue.JiraId?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            issue.LinkedIssues = [];
            return;
        }

        var byKey = await GetForIssuesAsync([key], cancellationToken);
        issue.LinkedIssues = byKey.TryGetValue(key, out var links) ? links.ToList() : [];
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<LinkedIssue>>> GetForIssuesAsync(
        IReadOnlyCollection<string> issueKeys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, IReadOnlyList<LinkedIssue>>(StringComparer.OrdinalIgnoreCase);
        var keys = issueKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0)
        {
            return result;
        }

        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);
        var rows = await db.IssueLinks.AsNoTracking()
            .Where(l => keys.Contains(l.FromKey) || keys.Contains(l.ToKey))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return result;
        }

        var wanted = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        var views = new Dictionary<string, List<LinkedIssue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var from = row.FromKey.Trim();
            var to = row.ToKey.Trim();
            if (wanted.Contains(from))
            {
                ViewsOf(views, from).Add(View(row.JiraLinkId, row.TypeName, row.OutwardLabel, true, to));
            }
            if (wanted.Contains(to) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                ViewsOf(views, to).Add(View(row.JiraLinkId, row.TypeName, row.InwardLabel, false, from));
            }
        }

        var features = await FeaturesByKeyAsync(db, views.Values.SelectMany(v => v).Select(v => v.Key), cancellationToken);
        foreach (var (key, links) in views)
        {
            result[key] = Ordered(links.Select(link => Resolve(link, features)));
        }
        return result;
    }

    public async Task<List<LinkedIssue>> FromJiraAsync(
        IReadOnlyList<JiraIssueLink> links, CancellationToken cancellationToken = default)
    {
        var views = links
            .Where(l => !string.IsNullOrWhiteSpace(l.Id) && !string.IsNullOrWhiteSpace(l.OtherKey))
            .Select(l => View(l.Id.Trim(), l.TypeName, l.IsOutward ? l.OutwardLabel : l.InwardLabel, l.IsOutward, l.OtherKey.Trim())
                with { Name = l.OtherSummary, Status = l.OtherStatus })
            .DistinctBy(v => v.JiraLinkId, StringComparer.Ordinal)
            .ToList();
        if (views.Count == 0)
        {
            return views;
        }

        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);
        var features = await FeaturesByKeyAsync(db, views.Select(v => v.Key), cancellationToken);
        return Ordered(views.Select(view => Resolve(view, features)));
    }

    public Task<bool> ApplyAsync(
        IReadOnlyCollection<IssueLinkSnapshot> snapshots, bool prune, CancellationToken cancellationToken = default) =>
        IssueLinkStore.ApplyAsync(_ctx, snapshots, prune, cancellationToken);

    private sealed record FeatureRef(int Id, string JiraId, string? Name, string Summary, string? Status);

    private static List<LinkedIssue> ViewsOf(Dictionary<string, List<LinkedIssue>> views, string key)
    {
        if (!views.TryGetValue(key, out var list))
        {
            list = [];
            views[key] = list;
        }
        return list;
    }

    private static LinkedIssue View(string linkId, string typeName, string? label, bool isOutward, string otherKey) =>
        new(linkId, typeName, string.IsNullOrWhiteSpace(label) ? typeName : label.Trim(), isOutward, otherKey);

    private static LinkedIssue Resolve(LinkedIssue link, Dictionary<string, FeatureRef> features) =>
        features.TryGetValue(link.Key, out var feature)
            ? link with
            {
                FeatureId = feature.Id,
                Name = string.IsNullOrWhiteSpace(feature.Name) ? feature.Summary : feature.Name,
                Status = feature.Status,
            }
            : link;

    private static List<LinkedIssue> Ordered(IEnumerable<LinkedIssue> links) =>
        links
            .OrderBy(l => l.Relation, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => ProjectOf(l.Key), StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => NumberOf(l.Key))
            .ThenBy(l => l.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ProjectOf(string key)
    {
        var dash = key.LastIndexOf('-');
        return dash > 0 ? key[..dash] : key;
    }

    private static long NumberOf(string key)
    {
        var dash = key.LastIndexOf('-');
        return dash >= 0 && long.TryParse(key[(dash + 1)..], out var number) ? number : long.MaxValue;
    }

    private static async Task<Dictionary<string, FeatureRef>> FeaturesByKeyAsync(
        EstimationDbContext db, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, FeatureRef>(StringComparer.OrdinalIgnoreCase);
        var wanted = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return map;
        }

        var found = await db.Features.AsNoTracking()
            .Where(f => f.JiraId != null && wanted.Contains(f.JiraId))
            .Select(f => new FeatureRef(f.Id, f.JiraId!, f.Name, f.Summary, f.Status))
            .ToListAsync(cancellationToken);
        foreach (var feature in found)
        {
            map.TryAdd(feature.JiraId.Trim(), feature);
        }
        return map;
    }
}
