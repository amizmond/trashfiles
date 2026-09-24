using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;

namespace Estimation.Components.Shared;

public record ArtRef(int Id, string Name, string? Description);

public sealed class ArtLookup
{
    public static readonly ArtLookup Empty = new(ArtMatcher.Empty, new Dictionary<int, ArtRef>());

    private ArtLookup(ArtMatcher matcher, IReadOnlyDictionary<int, ArtRef> byId)
    {
        Matcher = matcher;
        ById = byId;
    }

    public ArtMatcher Matcher { get; }

    public IReadOnlyDictionary<int, ArtRef> ById { get; }

    public static ArtLookup Build(IEnumerable<Art> arts)
    {
        var list = arts.ToList();
        return new ArtLookup(
            ArtMatcher.FromArts(list),
            list.ToDictionary(a => a.Id, a => new ArtRef(a.Id, a.Name, a.Description)));
    }
}

public static class ArtHelper
{
    public static ArtLookup BuildLookup(IEnumerable<Art> arts) => ArtLookup.Build(arts);

    public const string UnassignedLabel = "(Unassigned)";

    public const string AmbiguousLabel = "(Ambiguous)";

    public static ArtRef? ResolveArt(JiraIssue issue, ArtLookup lookup) =>
        ToRef(lookup.Matcher.Match(issue), lookup);

    public static IReadOnlyList<ArtRef> ResolveParentArts(JiraIssue parent, IEnumerable<JiraIssue> childFeatures, ArtLookup lookup)
    {
        var children = childFeatures.ToList();
        if (children.Count == 0)
        {
            return ResolveArt(parent, lookup) is { } own ? [own] : [];
        }

        return children
            .Select(f => ResolveArt(f, lookup))
            .OfType<ArtRef>()
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? DisplayName(ArtMatch match, ArtLookup lookup) => match.Kind switch
    {
        ArtMatchKind.Matched => ToRef(match, lookup)?.Name,
        ArtMatchKind.Unmatched => UnassignedLabel,
        ArtMatchKind.Ambiguous => AmbiguousLabel,
        _ => null
    };

    public static string? Explain(ArtMatch match, ArtLookup lookup) => match.Kind switch
    {
        ArtMatchKind.Matched when match.Scope is not null => $"{ToRef(match, lookup)?.Name} via {match.Scope}",
        ArtMatchKind.Ambiguous => $"Claimed by {NamesOf(match.CandidateArtIds, lookup)}",
        ArtMatchKind.Unmatched => ExplainUnmatched(match, lookup),
        _ => null
    };

    public static string? DescribeMove(ArtMatch before, ArtMatch after, ArtLookup lookup)
    {
        var from = DisplayName(before, lookup) ?? "no ART";
        var to = DisplayName(after, lookup) ?? "no ART";
        if (before.ArtId == after.ArtId && from == to)
        {
            return null;
        }

        var reason = after.IsMatched
            ? $" via {after.Scope}"
            : Explain(after, lookup) is { } why ? $": {why}" : "";
        return $"Moves from {from} to {to}{reason}.";
    }

    private static string ExplainUnmatched(ArtMatch match, ArtLookup lookup)
    {
        var scopes = lookup.Matcher.ScopesForKey(match.ProjectKey);
        var needs = match.CandidateArtIds
            .Select(id => (Id: id, Scope: scopes.FirstOrDefault(s => s.ArtId == id)))
            .Where(x => x.Scope is { IsFiltered: true })
            .Select(x => $"{NameOf(x.Id, lookup)} needs {Requirement(x.Scope!)}")
            .ToList();

        var text = $"Key {match.ProjectKey} is used by {NamesOf(match.CandidateArtIds, lookup)}, but none of their filters accepts the issue";
        return needs.Count == 0 ? text : $"{text} ({string.Join("; ", needs)})";
    }

    private static string Requirement(ArtKeyScope scope)
    {
        var parts = new List<string>();
        if (scope.Components.Count > 0)
        {
            parts.Add(OneOf("component", "components", scope.Components));
        }
        if (scope.Labels.Count > 0)
        {
            parts.Add(OneOf("label", "labels", scope.Labels));
        }

        return string.Join(" and ", parts);
    }

    private static string OneOf(string singular, string plural, IReadOnlyCollection<string> values) =>
        values.Count == 1 ? $"{singular} {values.First()}" : $"one of the {plural} {string.Join(", ", values)}";

    private static string NameOf(int artId, ArtLookup lookup) =>
        lookup.ById.TryGetValue(artId, out var art) ? art.Name : $"#{artId}";

    private static string NamesOf(IEnumerable<int> artIds, ArtLookup lookup) =>
        string.Join(", ", artIds.Select(id => NameOf(id, lookup)));

    private static ArtRef? ToRef(ArtMatch match, ArtLookup lookup) =>
        match.ArtId is { } id && lookup.ById.TryGetValue(id, out var art) ? art : null;
}
