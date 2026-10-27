using Estimation.Components.DepartmentPlanning;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.Features.DependencyMap.Services;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningMapLinksTests
{
    private const string Pi = "PI 26.09";
    private const int CorePayments = 1;
    private const int DataPlatform = 2;
    private const int Elsewhere = 3;

    private static readonly PlanningMapArt CorePaymentsMap = new(CorePayments, "Core Payments", true);
    private static readonly PlanningMapArt DataPlatformMap = new(DataPlatform, "Data Platform", true);
    private static readonly PlanningMapArt ElsewhereMap = new(Elsewhere, "Elsewhere", true);

    private static PlanningFeature Feature(int id, PlanningMapArt? map, string? jiraId = "default", params PlanningMapArt[] linking) =>
        new(id, jiraId == "default" ? $"PAY-{id}" : jiraId, $"Feature {id}", "To Do", null, null, id, 3, Pi, null, null, null, null, null, [])
        {
            DependencyMaps = map is null ? linking : [map, .. linking],
        };

    private static PlanningRow FeatureRow(PlanningFeature feature) => new()
    {
        Id = $"f:{feature.Id}",
        Level = PlanningLevel.BusinessEpic,
        EntityId = feature.Id,
        JiraId = feature.JiraId,
        Title = feature.Title,
        Feature = feature,
    };

    private static PlanningRow Item(PlanningLevel level, string? jiraId, int? entityId, params PlanningRow[] children) => new()
    {
        Id = $"{level}:{jiraId ?? "none"}",
        Level = level,
        EntityId = entityId,
        JiraId = jiraId,
        Title = jiraId ?? "None",
        Children = children,
    };

    private static DependencyMapUrlState Parse(string url)
    {
        Assert.StartsWith($"{RoutingPath.DependencyMap}?", url);
        var query = QueryHelpers.ParseQuery(new Uri(new Uri("http://localhost"), url).Query)
            .ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());
        return DependencyMapQuery.Parse(query);
    }

    [Fact]
    public void A_feature_opens_the_map_of_the_art_that_owns_it_focused_on_the_feature()
    {
        var links = PlanningMapLinks.For(FeatureRow(Feature(1, ElsewhereMap)), Pi, _ => true);

        var link = Assert.Single(links);
        Assert.Equal($"{RoutingPath.DependencyMap}?cp=Elsewhere&pi=PI%2026.09&focus=PAY-1", link.Url);
        Assert.Equal("Elsewhere", link.ArtName);
        var state = Parse(link.Url);
        Assert.Equal("Elsewhere", state.Art);
        Assert.Equal(Pi, state.Pi);
        Assert.Equal("PAY-1", state.Focus);
        Assert.False(state.FeatureFilter.IsActive);
        Assert.Equal(0, state.Filter.ActiveCount);
    }

    [Fact]
    public void A_feature_on_no_map_opens_a_map_that_links_to_it()
    {
        var link = Assert.Single(PlanningMapLinks.For(FeatureRow(Feature(1, CorePaymentsMap with { InScope = false })), Pi, _ => true));

        Assert.Equal("PAY-1", Parse(link.Url).Focus);
        Assert.Equal("Open the Dependency Map of Core Payments for PI 26.09, which links to PAY-1", link.Title);
    }

    [Fact]
    public void A_feature_whose_own_map_the_user_may_not_open_opens_a_map_that_links_to_it()
    {
        var feature = Feature(1, ElsewhereMap, "default", CorePaymentsMap with { InScope = false }, DataPlatformMap with { InScope = false });

        var link = Assert.Single(PlanningMapLinks.For(FeatureRow(feature), Pi, artId => artId != Elsewhere));

        Assert.Equal("Core Payments", link.ArtName);
        Assert.Equal("PAY-1", Parse(link.Url).Focus);
    }

    [Fact]
    public void A_feature_gets_no_link_without_a_key_a_map_a_pi_or_the_right_to_open_the_map()
    {
        Assert.Empty(PlanningMapLinks.For(FeatureRow(Feature(1, ElsewhereMap, jiraId: null)), Pi, _ => true));
        Assert.Empty(PlanningMapLinks.For(FeatureRow(Feature(1, null)), Pi, _ => true));
        Assert.Empty(PlanningMapLinks.For(FeatureRow(Feature(1, ElsewhereMap)), null, _ => true));
        Assert.Empty(PlanningMapLinks.For(FeatureRow(Feature(1, ElsewhereMap)), " ", _ => true));
        Assert.Empty(PlanningMapLinks.For(FeatureRow(Feature(1, ElsewhereMap)), Pi, artId => artId != Elsewhere));
    }

    [Fact]
    public void A_portfolio_epic_opens_one_filtered_map_per_art_that_holds_its_features()
    {
        var shared = Feature(2, CorePaymentsMap);
        var epic = Item(PlanningLevel.PortfolioEpic, "EPIC-10", 10,
            Item(PlanningLevel.BusinessOutcome, "BO-100", 100, FeatureRow(Feature(1, DataPlatformMap)), FeatureRow(shared)),
            Item(PlanningLevel.BusinessOutcome, "BO-101", 101, FeatureRow(shared), FeatureRow(Feature(3, CorePaymentsMap)), FeatureRow(Feature(4, ElsewhereMap with { InScope = false }))));

        var links = PlanningMapLinks.For(epic, Pi, _ => true);

        Assert.Equal(["Core Payments", "Data Platform"], links.Select(l => l.ArtName));
        Assert.All(links, link =>
        {
            var state = Parse(link.Url);
            Assert.Equal(["EPIC-10"], state.FeatureFilter.PortfolioEpics);
            Assert.True(state.Filter.ShowUnlinked);
            Assert.Null(state.Focus);
        });
        Assert.Equal("Open the Dependency Map of Core Payments for PI 26.09, showing the features of EPIC-10", links[0].Title);
    }

    [Fact]
    public void Outcomes_and_objectives_filter_the_map_by_their_own_key()
    {
        var feature = FeatureRow(Feature(1, CorePaymentsMap));

        var outcome = Assert.Single(PlanningMapLinks.For(Item(PlanningLevel.BusinessOutcome, "BO-100", 100, feature), Pi, _ => true));
        var objective = Assert.Single(PlanningMapLinks.For(Item(PlanningLevel.StrategicObjective, "STRAT-1", 1, feature), Pi, _ => true));

        Assert.Equal(["BO-100"], Parse(outcome.Url).FeatureFilter.BusinessOutcomes);
        Assert.Equal(["STRAT-1"], Parse(objective.Url).FeatureFilter.StrategicObjectives);
    }

    [Fact]
    public void The_without_buckets_filter_the_map_to_features_without_that_parent()
    {
        var feature = FeatureRow(Feature(1, CorePaymentsMap));

        var noObjective = Parse(Assert.Single(PlanningMapLinks.For(Item(PlanningLevel.StrategicObjective, null, null, feature), Pi, _ => true)).Url);
        var noEpic = Parse(Assert.Single(PlanningMapLinks.For(Item(PlanningLevel.PortfolioEpic, null, null, feature), Pi, _ => true)).Url);
        var noOutcome = Parse(Assert.Single(PlanningMapLinks.For(Item(PlanningLevel.BusinessOutcome, null, null, feature), Pi, _ => true)).Url);

        Assert.True(noObjective.FeatureFilter.StrategicObjectiveNot);
        Assert.Empty(noObjective.FeatureFilter.StrategicObjectives);
        Assert.True(noEpic.FeatureFilter.PortfolioEpicNot);
        Assert.Empty(noEpic.FeatureFilter.PortfolioEpics);
        Assert.True(noOutcome.FeatureFilter.BusinessOutcomeNot);
        Assert.Empty(noOutcome.FeatureFilter.BusinessOutcomes);
    }

    [Fact]
    public void An_art_row_opens_its_own_map_first_without_a_filter()
    {
        var art = Item(PlanningLevel.Art, null, CorePayments,
            FeatureRow(Feature(1, ElsewhereMap)),
            FeatureRow(Feature(2, ElsewhereMap)),
            FeatureRow(Feature(3, CorePaymentsMap))) with { ArtId = CorePayments };

        var links = PlanningMapLinks.For(art, Pi, _ => true);

        Assert.Equal(["Core Payments", "Elsewhere"], links.Select(l => l.ArtName));
        Assert.False(Parse(links[0].Url).FeatureFilter.IsActive);
        Assert.Equal(0, Parse(links[0].Url).Filter.ActiveCount);
        Assert.Equal("Open the Dependency Map of Core Payments for PI 26.09", links[0].Title);
    }

    [Fact]
    public void An_art_row_offers_its_own_map_even_when_none_of_its_shown_features_is_on_it()
    {
        var empty = Item(PlanningLevel.Art, "Core Payments", CorePayments) with { Title = "Core Payments", ArtId = CorePayments, JiraId = null };
        var others = Item(PlanningLevel.Art, null, CorePayments, FeatureRow(Feature(1, ElsewhereMap))) with { Title = "Core Payments", ArtId = CorePayments };

        Assert.Equal(["Core Payments"], PlanningMapLinks.For(empty, Pi, _ => true).Select(l => l.ArtName));
        Assert.Equal(["Core Payments", "Elsewhere"], PlanningMapLinks.For(others, Pi, _ => true).Select(l => l.ArtName));
        Assert.Empty(PlanningMapLinks.For(empty, Pi, artId => artId != CorePayments));
    }

    [Fact]
    public void Names_and_keys_with_commas_and_symbols_reach_the_map_unchanged()
    {
        var map = new PlanningMapArt(9, "R&D, Core #1", true);
        var epic = Item(PlanningLevel.PortfolioEpic, "EPIC,10", 10, FeatureRow(Feature(1, map)));

        var state = Parse(Assert.Single(PlanningMapLinks.For(epic, "PI 26.09 & more", _ => true)).Url);

        Assert.Equal("R&D, Core #1", state.Art);
        Assert.Equal("PI 26.09 & more", state.Pi);
        Assert.Equal(["EPIC,10"], state.FeatureFilter.PortfolioEpics);
    }

    [Fact]
    public void Maps_the_user_may_not_open_are_left_out()
    {
        var epic = Item(PlanningLevel.PortfolioEpic, "EPIC-10", 10, FeatureRow(Feature(1, CorePaymentsMap)), FeatureRow(Feature(2, DataPlatformMap)));

        var link = Assert.Single(PlanningMapLinks.For(epic, Pi, artId => artId == DataPlatform));

        Assert.Equal("Data Platform", link.ArtName);
    }

    [Fact]
    public void Items_without_a_key_features_without_a_key_and_line_rows_get_no_link()
    {
        var keyless = Item(PlanningLevel.PortfolioEpic, null, 10, FeatureRow(Feature(1, CorePaymentsMap)));
        var keylessFeatures = Item(PlanningLevel.PortfolioEpic, "EPIC-10", 10, FeatureRow(Feature(1, CorePaymentsMap, jiraId: null)));
        var line = new PlanningRow { Id = "line", Kind = PlanningRowKind.LineMarker, Level = PlanningLevel.Art, Title = "line" };

        Assert.Empty(PlanningMapLinks.For(keyless, Pi, _ => true));
        Assert.Empty(PlanningMapLinks.For(keylessFeatures, Pi, _ => true));
        Assert.Empty(PlanningMapLinks.For(line, Pi, _ => true));
    }
}
