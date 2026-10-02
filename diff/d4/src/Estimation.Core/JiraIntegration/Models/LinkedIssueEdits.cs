using Estimation.Core.JiraIntegration.Client;

namespace Estimation.Core.JiraIntegration.Models;

public sealed record LinkTarget(int FeatureId, string Key, string? Name, string? Status);

public sealed record LinkRelation(string TypeName, bool IsOutward, string Phrase, bool Symmetric)
{
    public static List<LinkRelation> From(IEnumerable<JiraIssueLinkType> types)
    {
        var relations = new List<LinkRelation>();
        foreach (var type in types)
        {
            if (string.IsNullOrWhiteSpace(type.Name))
            {
                continue;
            }

            var outward = string.IsNullOrWhiteSpace(type.Outward) ? type.Name : type.Outward.Trim();
            var inward = string.IsNullOrWhiteSpace(type.Inward) ? type.Name : type.Inward.Trim();
            var symmetric = string.Equals(outward, inward, StringComparison.OrdinalIgnoreCase);

            relations.Add(new LinkRelation(type.Name, true, outward, symmetric));
            if (!symmetric)
            {
                relations.Add(new LinkRelation(type.Name, false, inward, false));
            }
        }

        return relations
            .OrderBy(r => r.Phrase, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed record PendingLink(string TypeName, bool IsOutward, string Relation, string Key)
{
    public bool Symmetric { get; init; }

    public int? FeatureId { get; init; }

    public string? Name { get; init; }

    public string? Status { get; init; }

    public string? Error { get; init; }

    public bool Matches(LinkedIssue link) =>
        string.Equals(TypeName, link.TypeName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Key, link.Key, StringComparison.OrdinalIgnoreCase)
        && (Symmetric || IsOutward == link.IsOutward);

    public bool Matches(PendingLink other) =>
        string.Equals(TypeName, other.TypeName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase)
        && (Symmetric || IsOutward == other.IsOutward);
}

public sealed record LinkChanges(IReadOnlyList<string> Removals, IReadOnlyList<PendingLink> Adds)
{
    public bool Any => Removals.Count > 0 || Adds.Count > 0;
}

public sealed record LinkPushFailure(string? LinkId, PendingLink? Add, string Message);

public sealed record LinkPushResult(
    IReadOnlyList<string> Removed,
    IReadOnlyList<PendingLink> Added,
    IReadOnlyList<LinkPushFailure> Failures);

public sealed class LinkedIssueEdits
{
    public const string NotShownByJira = "Jira accepted the change but does not show it yet.";

    private readonly List<PendingLink> _adds = [];
    private readonly Dictionary<string, string?> _removals = new(StringComparer.Ordinal);

    public IReadOnlyList<PendingLink> Adds => _adds;

    public bool HasChanges => _adds.Count > 0 || _removals.Count > 0;

    public int Count => _adds.Count + _removals.Count;

    public bool IsRemoved(string linkId) => _removals.ContainsKey(linkId);

    public string? RemovalError(string linkId) => _removals.GetValueOrDefault(linkId);

    public LinkChanges Changes() => new(_removals.Keys.ToList(), _adds.ToList());

    public string? TryAdd(string? issueKey, LinkRelation relation, LinkTarget target, IReadOnlyList<LinkedIssue> current)
    {
        var key = target.Key.Trim();
        if (string.Equals(key, issueKey?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "A feature cannot be linked to itself.";
        }

        var add = new PendingLink(relation.TypeName, relation.IsOutward, relation.Phrase, key)
        {
            Symmetric = relation.Symmetric,
            FeatureId = target.FeatureId,
            Name = target.Name,
            Status = target.Status,
        };

        if (current.Any(add.Matches) || _adds.Any(add.Matches))
        {
            return $"{key} is already linked as \"{relation.Phrase}\".";
        }

        _adds.Add(add);
        return null;
    }

    public void CancelAdd(PendingLink add) => _adds.Remove(add);

    public void Remove(string linkId) => _removals[linkId] = null;

    public void Restore(string linkId) => _removals.Remove(linkId);

    public void Clear()
    {
        _adds.Clear();
        _removals.Clear();
    }

    public IReadOnlyList<string> Rebase(IReadOnlyList<LinkedIssue> live)
    {
        _adds.RemoveAll(add => live.Any(add.Matches));

        var liveIds = live.Select(l => l.JiraLinkId).ToHashSet(StringComparer.Ordinal);
        var gone = _removals.Keys.Where(id => !liveIds.Contains(id)).ToList();
        foreach (var id in gone)
        {
            _removals.Remove(id);
        }
        return gone;
    }

    public string? FirstError =>
        _removals.Values.FirstOrDefault(error => error is not null) ?? _adds.FirstOrDefault(a => a.Error is not null)?.Error;

    public IReadOnlyList<string> Confirm(IReadOnlyList<LinkedIssue> live, LinkChanges sent, LinkPushResult push)
    {
        Rebase(live);
        MarkFailures(push);

        var liveIds = live.Select(l => l.JiraLinkId).ToHashSet(StringComparer.Ordinal);
        return sent.Removals.Where(id => !liveIds.Contains(id)).ToList();
    }

    public void Settle(LinkPushResult push)
    {
        foreach (var id in push.Removed)
        {
            _removals.Remove(id);
        }
        _adds.RemoveAll(add => push.Added.Any(add.Matches));
        MarkFailures(push);
    }

    public void MarkFailures(LinkPushResult push)
    {
        for (var i = 0; i < _adds.Count; i++)
        {
            var failure = push.Failures.FirstOrDefault(f => f.Add is not null && _adds[i].Matches(f.Add));
            _adds[i] = _adds[i] with { Error = failure?.Message ?? NotShownByJira };
        }

        foreach (var id in _removals.Keys.ToList())
        {
            var failure = push.Failures.FirstOrDefault(f => string.Equals(f.LinkId, id, StringComparison.Ordinal));
            _removals[id] = failure?.Message ?? NotShownByJira;
        }
    }
}
