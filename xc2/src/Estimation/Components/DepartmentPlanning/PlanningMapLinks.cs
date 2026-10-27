using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.Features.DependencyMap.Services;
using Estimation.Core.Features.Models;

namespace Estimation.Components.DepartmentPlanning;

public sealed record PlanningMapLink(int ArtId, string ArtName, string Url, string Title);

public static class PlanningMapLinks
{
    public static IReadOnlyList<PlanningMapLink> For(PlanningRow row, string? piName, Func<int, bool> canOpenArt)
    {
        if (string.IsNullOrWhiteSpace(piName) || row.Kind != PlanningRowKind.Node)
        {
            return [];
        }

        var pi = piName.Trim();
        if (row.Level == PlanningLevel.BusinessEpic)
        {
            if (string.IsNullOrWhiteSpace(row.JiraId) || row.Feature?.DependencyMaps.FirstOrDefault(m => canOpenArt(m.ArtId)) is not { } map)
            {
                return [];
            }

            var key = row.JiraId.Trim();
            var title = map.InScope
                ? $"Open the Dependency Map of {map.ArtName} for {pi} on {key}"
                : $"Open the Dependency Map of {map.ArtName} for {pi}, which links to {key}";
            return [new PlanningMapLink(map.ArtId, map.ArtName, Url(map.ArtName, pi, new FeatureCardFilter(), new DependencyFilter(), key), title)];
        }

        if (CardFilter(row) is not { } cards)
        {
            return [];
        }

        var maps = FeaturesUnder(row)
            .DistinctBy(f => f.Id)
            .Where(f => !string.IsNullOrWhiteSpace(f.JiraId))
            .Select(f => f.DependencyMaps.FirstOrDefault(m => m.InScope))
            .OfType<PlanningMapArt>()
            .GroupBy(m => m.ArtId)
            .Select(g => (ArtId: g.Key, Name: g.First().ArtName, Features: g.Count()))
            .ToList();
        if (row.Level == PlanningLevel.Art && row.ArtId is { } ownArtId && maps.All(m => m.ArtId != ownArtId))
        {
            maps.Add((ownArtId, row.Title, 0));
        }

        var links = row.Level == PlanningLevel.Art ? new DependencyFilter() : new DependencyFilter { ShowUnlinked = true };
        var subject = Subject(row);
        return maps
            .Where(m => canOpenArt(m.ArtId))
            .OrderByDescending(m => m.ArtId == row.ArtId)
            .ThenByDescending(m => m.Features)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m => new PlanningMapLink(
                m.ArtId,
                m.Name,
                Url(m.Name, pi, cards, links, null),
                subject is null ? $"Open the Dependency Map of {m.Name} for {pi}" : $"Open the Dependency Map of {m.Name} for {pi}, showing {subject}"))
            .ToList();
    }

    public static string Url(string artName, string piName, FeatureCardFilter cards, DependencyFilter links, string? focus)
    {
        var state = new DependencyMapUrlState(artName, piName, null, null, null, false, focus, ChainMode.Direct, true, links, cards);
        var query = DependencyMapQuery.Write(state)
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!.ToString()!)}");
        return $"{RoutingPath.DependencyMap}?{string.Join('&', query)}";
    }

    private static FeatureCardFilter? CardFilter(PlanningRow row)
    {
        var key = row.JiraId?.Trim();
        var hasKey = !string.IsNullOrEmpty(key);
        return row.Level switch
        {
            PlanningLevel.Art => new FeatureCardFilter(),
            PlanningLevel.StrategicObjective when row.IsBucket => new FeatureCardFilter { StrategicObjectiveNot = true },
            PlanningLevel.StrategicObjective when hasKey => new FeatureCardFilter { StrategicObjectives = [key!] },
            PlanningLevel.PortfolioEpic when row.IsBucket => new FeatureCardFilter { PortfolioEpicNot = true },
            PlanningLevel.PortfolioEpic when hasKey => new FeatureCardFilter { PortfolioEpics = [key!] },
            PlanningLevel.BusinessOutcome when row.IsBucket => new FeatureCardFilter { BusinessOutcomeNot = true },
            PlanningLevel.BusinessOutcome when hasKey => new FeatureCardFilter { BusinessOutcomes = [key!] },
            _ => null,
        };
    }

    private static string? Subject(PlanningRow row) => row.Level switch
    {
        PlanningLevel.Art => null,
        PlanningLevel.StrategicObjective when row.IsBucket => "the features without a strategic objective",
        PlanningLevel.PortfolioEpic when row.IsBucket => "the features without a portfolio epic",
        PlanningLevel.BusinessOutcome when row.IsBucket => "the features without a business outcome",
        _ => $"the features of {row.JiraId!.Trim()}",
    };

    private static IEnumerable<PlanningFeature> FeaturesUnder(PlanningRow row)
    {
        if (row.Feature is { } feature)
        {
            yield return feature;
            yield break;
        }

        foreach (var child in row.Children)
        {
            foreach (var under in FeaturesUnder(child))
            {
                yield return under;
            }
        }
    }
}
