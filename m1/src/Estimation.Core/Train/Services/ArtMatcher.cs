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
        new(artId, JiraProjectKeys.Normalize(jiraKey) ?? string.Empty, JiraListValues.Parse(components), JiraListValues.Parse(labels));

    public bool IsFiltered => Components.Count > 0 || Labels.Count > 0;

    public bool Accepts(IReadOnlySet<string> issueComponents, IReadOnlySet<string> issueLabels) =>
        (Components.Count == 0 || Components.Overlaps(issueComponents))
        && (Labels.Count == 0 || Labels.Overlaps(issueLabels));

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

    public ArtMatch Match(string? projectKey, string? jiraId, string? labels, string? components)
    {
        var key = JiraProjectKeys.Of(projectKey, jiraId);
        if (key is null)
        {
            return ArtMatch.NoKey;
        }

        if (!_scopesByKey.TryGetValue(key, out var scopes))
        {
            return new ArtMatch(ArtMatchKind.Unassigned, key, null, []);
        }

        var issueComponents = JiraListValues.Parse(components);
        var issueLabels = JiraListValues.Parse(labels);

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

    private sealed class ScopeFilterComparer : IEqualityComparer<ArtKeyScope>
    {
        public static readonly ScopeFilterComparer Instance = new();

        public bool Equals(ArtKeyScope? x, ArtKeyScope? y) =>
            x is not null && y is not null && x.HasSameFilters(y);

        public int GetHashCode(ArtKeyScope obj) =>
            HashCode.Combine(obj.Components.Count, obj.Labels.Count);
    }
}
