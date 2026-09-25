using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Train.Services;

public static class JiraProjectKeys
{
    public static string? Of(string? projectKey, string? jiraId)
    {
        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            return projectKey.Trim();
        }

        if (string.IsNullOrWhiteSpace(jiraId))
        {
            return null;
        }

        var dash = jiraId.IndexOf('-');
        return dash > 0 ? jiraId[..dash].Trim() : jiraId.Trim();
    }

    public static string? Of(JiraIssue issue) => Of(issue.ProjectKey, issue.JiraId);

    public static string? Normalize(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : key.Trim().ToUpperInvariant();
}

public static class JiraListValues
{
    public static HashSet<string> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    public static string? Join(IEnumerable<string?> values)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct.Count == 0 ? null : string.Join(",", distinct);
    }
}

public sealed record ArtKeyScope(int ArtId, string JiraKey, IReadOnlySet<string> Components, IReadOnlySet<string> Labels)
{
    public static ArtKeyScope Create(int artId, string jiraKey, string? components = null, string? labels = null) =>
        new(artId, JiraProjectKeys.Normalize(jiraKey) ?? string.Empty, JiraValueFilter.Parse(components).Tokens, JiraValueFilter.Parse(labels).Tokens);

    public JiraValueFilter ComponentFilter { get; } = JiraValueFilter.Parse(Components);

    public JiraValueFilter LabelFilter { get; } = JiraValueFilter.Parse(Labels);

    public bool IsFiltered => Components.Count > 0 || Labels.Count > 0;

    public bool Accepts(IReadOnlySet<string> issueComponents, IReadOnlySet<string> issueLabels) =>
        ComponentFilter.Accepts(issueComponents) && LabelFilter.Accepts(issueLabels);

    public bool HasSameFilters(ArtKeyScope other) =>
        Components.SetEquals(other.Components) && Labels.SetEquals(other.Labels);

    public override string ToString() => ArtJiraKey.Describe(JiraKey, Components, Labels);
}

public enum ArtMatchKind
{
    NoKey = 0,

    Unassigned = 1,

    Matched = 2,

    Ambiguous = 3,

    Unmatched = 4
}

public sealed record ArtMatch(ArtMatchKind Kind, string? ProjectKey, int? ArtId, IReadOnlyList<int> CandidateArtIds, ArtKeyScope? Scope = null)
{
    public static readonly ArtMatch NoKey = new(ArtMatchKind.NoKey, null, null, []);

    public bool IsMatched => Kind == ArtMatchKind.Matched;
}

public sealed record ArtKeyConflict(string JiraKey, IReadOnlyList<int> ArtIds, bool Unfiltered);

public sealed record SampleValues(IReadOnlyList<string> Values, bool Other)
{
    public IReadOnlySet<string> Set { get; } = new HashSet<string>(Values, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SampleValues> For(IEnumerable<JiraValueFilter> judges)
    {
        var filters = judges
            .DistinctBy(f => string.Join(",", f.Tokens.Select(t => t.ToUpperInvariant()).Order(StringComparer.Ordinal)))
            .ToList();
        var values = filters.SelectMany(f => f.Mentioned).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var other = "other";
        for (var n = 1; values.Contains(other, StringComparer.OrdinalIgnoreCase); n++)
        {
            other = $"other{n}";
        }

        var samples = new List<SampleValues> { new([], false) };
        samples.AddRange(values.Select(v => new SampleValues([v], false)));
        samples.Add(new SampleValues([other], true));

        string Signature(SampleValues sample) => new(filters.Select(f => f.Accepts(sample.Set) ? '1' : '0').ToArray());
        var singles = samples.Where(s => s.Values.Count == 1 && !s.Other)
            .ToDictionary(s => s.Values[0], Signature, StringComparer.OrdinalIgnoreCase);
        var pairs = new HashSet<string>();
        for (var i = 0; i < filters.Count; i++)
        {
            for (var j = i + 1; j < filters.Count; j++)
            {
                foreach (var a in filters[i].IncludedValues)
                {
                    foreach (var b in filters[j].IncludedValues.Where(v => !string.Equals(a, v, StringComparison.OrdinalIgnoreCase)))
                    {
                        var pair = new SampleValues([a, b], false);
                        var signature = Signature(pair);
                        if (signature != singles[a] && signature != singles[b] && pairs.Add(signature))
                        {
                            samples.Add(pair);
                        }
                    }
                }
            }
        }

        return samples;
    }

    public string Describe(string singular, string plural) => (Other, Values.Count) switch
    {
        (true, _) => $"a {singular} no ART lists",
        (_, 0) => $"no {singular}",
        (_, 1) => $"{singular} {Values[0]}",
        _ => $"{plural} {string.Join(" and ", Values)}"
    };
}

public sealed record ArtOverlap(string JiraKey, SampleValues? Components, SampleValues? Labels, IReadOnlyList<int> ArtIds)
{
    public string IssueText => (Components, Labels) switch
    {
        (null, null) => "any issue",
        ({ } components, null) => $"an issue with {components.Describe("component", "components")}",
        (null, { } labels) => $"an issue with {labels.Describe("label", "labels")}",
        ({ } components, { } labels) => $"an issue with {components.Describe("component", "components")} and {labels.Describe("label", "labels")}"
    };
}

public sealed class ArtMatcher
{
    public static readonly ArtMatcher Empty = new(Array.Empty<ArtKeyScope>());

    private readonly Dictionary<string, ArtKeyScope[]> _scopesByKey;
    private readonly Dictionary<int, ArtKeyScope[]> _scopesByArt;

    public ArtMatcher(IEnumerable<ArtKeyScope> scopes)
    {
        var rows = scopes
            .Where(s => s.JiraKey.Length > 0)
            .DistinctBy(s => (s.ArtId, s.JiraKey.ToUpperInvariant()))
            .ToList();

        _scopesByKey = rows
            .GroupBy(s => s.JiraKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.ArtId).ToArray(), StringComparer.OrdinalIgnoreCase);

        _scopesByArt = rows
            .GroupBy(s => s.ArtId)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }

    public ArtMatcher(IEnumerable<(int ArtId, string JiraKey)> keys)
        : this(keys.Select(k => ArtKeyScope.Create(k.ArtId, k.JiraKey)))
    {
    }

    public static ArtMatcher FromArts(IEnumerable<Art> arts) =>
        new(arts.SelectMany(a => a.JiraKeys.OrderBy(k => k.Id)
            .Select(k => ArtKeyScope.Create(a.Id, k.JiraKey, k.Components, k.Labels))));

    public static async Task<ArtMatcher> LoadAsync(EstimationDbContext db)
    {
        var rows = await db.CapitalProjectJiraKeys
            .AsNoTracking()
            .OrderBy(k => k.Id)
            .Select(k => new { k.CapitalProjectId, k.JiraKey, k.Components, k.Labels })
            .ToListAsync();

        return new ArtMatcher(rows.Select(r => ArtKeyScope.Create(r.CapitalProjectId, r.JiraKey, r.Components, r.Labels)));
    }

    public IReadOnlyCollection<string> AllKeys => _scopesByKey.Keys;

    public ArtMatch Match(JiraIssue issue) => Match(issue.ProjectKey, issue.JiraId, issue.Labels, issue.Components);

    public ArtMatch Match(IssueMatchFacts issue) => Match(issue.ProjectKey, issue.JiraId, issue.Labels, issue.Components);

    public ArtMatch Match(string? projectKey, string? jiraId, string? labels, string? components) =>
        Match(JiraProjectKeys.Of(projectKey, jiraId), JiraListValues.Parse(components), JiraListValues.Parse(labels));

    private ArtMatch Match(string? key, IReadOnlySet<string> issueComponents, IReadOnlySet<string> issueLabels)
    {
        if (key is null)
        {
            return ArtMatch.NoKey;
        }

        if (!_scopesByKey.TryGetValue(key, out var scopes))
        {
            return new ArtMatch(ArtMatchKind.Unassigned, key, null, []);
        }

        var filtered = scopes.Where(s => s.IsFiltered && s.Accepts(issueComponents, issueLabels)).ToList();
        var winners = filtered.Count > 0 ? filtered : scopes.Where(s => !s.IsFiltered).ToList();
        var artIds = winners.Select(s => s.ArtId).Distinct().ToList();

        return artIds.Count switch
        {
            0 => new ArtMatch(ArtMatchKind.Unmatched, key, null, scopes.Select(s => s.ArtId).Distinct().ToList()),
            1 => new ArtMatch(ArtMatchKind.Matched, key, artIds[0], artIds, winners[0]),
            _ => new ArtMatch(ArtMatchKind.Ambiguous, key, null, artIds)
        };
    }

    public int? ArtIdOf(JiraIssue issue) => Match(issue).ArtId;

    public bool BelongsTo(JiraIssue issue, int artId) => ArtIdOf(issue) == artId;

    public IReadOnlyList<string> KeysOf(int artId) =>
        _scopesByArt.TryGetValue(artId, out var scopes) ? scopes.Select(s => s.JiraKey).ToList() : [];

    public IReadOnlyList<ArtKeyScope> ScopesOf(int artId) =>
        _scopesByArt.TryGetValue(artId, out var scopes) ? scopes : [];

    public IReadOnlyList<ArtKeyScope> ScopesForKey(string? key)
    {
        var normalized = JiraProjectKeys.Normalize(key);
        return normalized is not null && _scopesByKey.TryGetValue(normalized, out var scopes) ? scopes : [];
    }

    public IReadOnlyList<int> ArtsForKey(string? key) => ScopesForKey(key).Select(s => s.ArtId).Distinct().ToList();

    public bool IsConfiguredKey(string? key) => ScopesForKey(key).Count > 0;

    public bool IsSharedKey(string? key) => ArtsForKey(key).Count > 1;

    public bool OwnsWholeKey(int artId, string? key) =>
        ScopesForKey(key) is [var only] && only.ArtId == artId && !only.IsFiltered;

    public IReadOnlyList<ArtKeyConflict> Conflicts =>
        _scopesByKey.Values
            .SelectMany(scopes => scopes
                .GroupBy(s => s, ScopeFilterComparer.Instance)
                .Where(g => g.Select(s => s.ArtId).Distinct().Count() > 1)
                .Select(g => new ArtKeyConflict(g.First().JiraKey, g.Select(s => s.ArtId).Distinct().ToList(), !g.Key.IsFiltered)))
            .ToList();

    public IReadOnlyList<ArtKeyConflict> ConflictsOf(int artId) =>
        Conflicts.Where(c => c.ArtIds.Contains(artId)).ToList();

    public IReadOnlyList<ArtOverlap> OverlapsForKey(string? key, ArtMatcher? before = null)
    {
        var scopes = ScopesForKey(key);
        if (scopes.Select(s => s.ArtId).Distinct().Count() < 2)
        {
            return [];
        }

        var jiraKey = scopes[0].JiraKey;
        var judges = scopes.Concat(before?.ScopesForKey(jiraKey) ?? []).ToList();
        var components = SampleValues.For(judges.Select(s => s.ComponentFilter));
        var labels = SampleValues.For(judges.Select(s => s.LabelFilter));
        var claimedComponents = components.Select(c => scopes.Count(s => s.ComponentFilter.Accepts(c.Set)) > 1).ToList();
        var claimedLabels = labels.Select(l => scopes.Count(s => s.LabelFilter.Accepts(l.Set)) > 1).ToList();
        var grid = new IReadOnlyList<int>?[components.Count, labels.Count];
        for (var c = 0; c < components.Count; c++)
        {
            for (var l = 0; l < labels.Count; l++)
            {
                if (!claimedComponents[c] || !claimedLabels[l])
                {
                    continue;
                }

                var match = Match(jiraKey, components[c].Set, labels[l].Set);
                if (match.Kind == ArtMatchKind.Ambiguous && !WasAmbiguous(before, jiraKey, components[c], labels[l], match.CandidateArtIds))
                {
                    grid[c, l] = match.CandidateArtIds;
                }
            }
        }

        return MergeOverlaps(jiraKey, components, labels, grid);
    }

    private static bool WasAmbiguous(ArtMatcher? before, string key, SampleValues components, SampleValues labels, IReadOnlyList<int> artIds) =>
        before?.Match(key, components.Set, labels.Set) is { Kind: ArtMatchKind.Ambiguous } old
        && old.CandidateArtIds.SequenceEqual(artIds);

    private static List<ArtOverlap> MergeOverlaps(string key, IReadOnlyList<SampleValues> components, IReadOnlyList<SampleValues> labels, IReadOnlyList<int>?[,] grid)
    {
        static bool Same(IReadOnlyList<int>? a, IReadOnlyList<int>? b) => a is not null && b is not null && a.SequenceEqual(b);

        var rows = Enumerable.Range(0, components.Count)
            .Select(c => Enumerable.Range(0, labels.Count).All(l => Same(grid[c, 0], grid[c, l])) ? grid[c, 0] : null)
            .ToList();
        var columns = Enumerable.Range(0, labels.Count)
            .Select(l => Enumerable.Range(0, components.Count).All(c => Same(grid[0, l], grid[c, l])) ? grid[0, l] : null)
            .ToList();
        if (rows.All(r => Same(rows[0], r)))
        {
            return [new ArtOverlap(key, null, null, rows[0]!)];
        }

        var overlaps = new List<ArtOverlap>();
        overlaps.AddRange(rows.Select((ids, c) => (ids, c)).Where(r => r.ids is not null)
            .Select(r => new ArtOverlap(key, components[r.c], null, r.ids!)));
        overlaps.AddRange(columns.Select((ids, l) => (ids, l)).Where(r => r.ids is not null)
            .Select(r => new ArtOverlap(key, null, labels[r.l], r.ids!)));
        for (var c = 0; c < components.Count; c++)
        {
            for (var l = 0; l < labels.Count; l++)
            {
                if (grid[c, l] is { } ids && rows[c] is null && columns[l] is null)
                {
                    overlaps.Add(new ArtOverlap(key, components[c], labels[l], ids));
                }
            }
        }

        return overlaps;
    }

    private sealed class ScopeFilterComparer : IEqualityComparer<ArtKeyScope>
    {
        public static readonly ScopeFilterComparer Instance = new();

        public bool Equals(ArtKeyScope? x, ArtKeyScope? y) =>
            x is not null && y is not null && x.HasSameFilters(y);

        public int GetHashCode(ArtKeyScope obj) =>
            HashCode.Combine(obj.Components.Count, obj.Labels.Count);
    }
}
