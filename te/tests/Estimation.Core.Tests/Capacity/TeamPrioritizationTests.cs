using Estimation.Core.Capacity.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class TeamPrioritizationTests
{
    private const ArtPrioritization Pe = ArtPrioritization.PortfolioEpic;
    private const ArtPrioritization Bo = ArtPrioritization.BusinessOutcome;

    private static TeamArtPrioritization Art(string name, ArtPrioritization? prioritization) => new(name, prioritization);

    private static TeamCapacityFeatureRow Row(int featureId, int? featureRanking, int? prioritizationRank) =>
        new(featureId, null, $"Feature {featureId}", null, null, 0, true, 0)
        {
            FeatureRanking = featureRanking,
            PrioritizationRank = prioritizationRank,
        };

    private static ArtOrderEntry Entry(int? itemId, int rank, bool isIncluded = true) =>
        new(itemId ?? ArtBoardItems.None, rank, isIncluded, ArtOrderOrigin.Saved);

    private static TeamArtPrioritization Board(
        string name, int artId, ArtPrioritization level, ArtOrderMode kind, params ArtOrderEntry[] entries) =>
        new(name, level)
        {
            ArtId = artId,
            Placements = ArtBoardItems.Placements(new ArtOrderResolution(kind, entries)),
            Kind = kind,
        };

    private static TeamArtPrioritization PiOrder(string name, int artId, ArtPrioritization level, params ArtOrderEntry[] entries) =>
        Board(name, artId, level, ArtOrderMode.PiOrder, entries);

    private static TeamArtPrioritization General(string name, int artId, ArtPrioritization level, params ArtOrderEntry[] entries) =>
        Board(name, artId, level, ArtOrderMode.FollowsGeneral, entries);

    private static Feature Planned(int? outcomeId, int? epicId, int? ranking = null) => new()
    {
        Summary = "Feature",
        Ranking = ranking,
        BusinessOutcomeId = outcomeId,
        BusinessOutcome = outcomeId is null
            ? null
            : new BusinessOutcome
            {
                Id = outcomeId.Value,
                Summary = "Outcome",
                PortfolioEpicId = epicId,
                PortfolioEpic = epicId is null ? null : new PortfolioEpic { Id = epicId.Value, Summary = "Epic" },
            },
    };

    [Fact]
    public void A_team_without_an_art_has_no_prioritization()
    {
        var prioritization = TeamPrioritization.Resolve([]);

        Assert.Null(prioritization.Mode);
        Assert.False(prioritization.IsConflict);
        Assert.Empty(prioritization.SourceArtNames);
    }

    [Fact]
    public void A_team_follows_the_prioritization_of_its_art()
    {
        var prioritization = TeamPrioritization.Resolve([Art("Core Payments", Pe)]);

        Assert.Equal(Pe, prioritization.Mode);
        Assert.Equal(["Core Payments"], prioritization.SourceArtNames);
    }

    [Fact]
    public void An_art_without_a_prioritization_is_ignored()
    {
        var prioritization = TeamPrioritization.Resolve([Art("Risk Analytics", null), Art("Data Platform", Bo)]);

        Assert.Equal(Bo, prioritization.Mode);
        Assert.False(prioritization.IsConflict);
        Assert.Equal(["Data Platform"], prioritization.SourceArtNames);
    }

    [Fact]
    public void Arts_that_agree_are_all_named_as_the_source()
    {
        var prioritization = TeamPrioritization.Resolve([Art("Mobile Banking", Bo), Art("Data Platform", Bo)]);

        Assert.Equal(Bo, prioritization.Mode);
        Assert.Equal(["Data Platform", "Mobile Banking"], prioritization.SourceArtNames);
    }

    [Fact]
    public void Arts_that_disagree_fall_back_to_feature_ranking()
    {
        var prioritization = TeamPrioritization.Resolve([Art("Risk Analytics", Pe), Art("Data Platform", Bo)]);

        Assert.Null(prioritization.Mode);
        Assert.True(prioritization.IsConflict);
        Assert.Empty(prioritization.SourceArtNames);
    }

    [Fact]
    public void Without_an_art_order_there_is_no_rank_and_the_sort_key_is_the_feature_ranking()
    {
        var prioritization = TeamPrioritization.Resolve([Art("Core Payments", Pe)]);

        Assert.Null(prioritization.OrderArt);
        Assert.Null(prioritization.RankOf(Planned(10, 1, ranking: 9)));
        Assert.Equal(9, prioritization.SortKeyOf(Planned(10, 1, ranking: 9)));
    }

    [Fact]
    public void Without_a_prioritization_the_sort_key_is_the_feature_ranking()
    {
        Assert.Null(TeamPrioritization.None.RankOf(Planned(10, 1, ranking: 9)));
        Assert.Equal(9, TeamPrioritization.None.SortKeyOf(Planned(10, 1, ranking: 9)));
    }

    [Fact]
    public void The_default_order_uses_the_rank_then_feature_ranking_and_puts_missing_ranks_last()
    {
        var prioritization = new TeamPrioritization(Pe, []);

        var ordered = prioritization.DefaultOrder(
        [
            Row(1, featureRanking: 1, prioritizationRank: null),
            Row(2, featureRanking: 7, prioritizationRank: 2),
            Row(3, featureRanking: 4, prioritizationRank: 2),
            Row(4, featureRanking: null, prioritizationRank: 1),
            Row(5, featureRanking: null, prioritizationRank: 2),
        ]);

        Assert.Equal([4, 3, 2, 5, 1], ordered.Select(r => r.FeatureId));
    }

    [Fact]
    public void Without_a_prioritization_the_default_order_is_the_feature_ranking()
    {
        var ordered = TeamPrioritization.None.DefaultOrder(
        [
            Row(1, featureRanking: null, prioritizationRank: 1),
            Row(2, featureRanking: 5, prioritizationRank: null),
            Row(3, featureRanking: 2, prioritizationRank: null),
        ]);

        Assert.Equal([3, 2, 1], ordered.Select(r => r.FeatureId));
    }

    [Fact]
    public void Placements_take_the_resolver_ranks_and_number_only_cards_above_the_line()
    {
        var placements = ArtBoardItems.Placements(new ArtOrderResolution(ArtOrderMode.General,
        [
            Entry(5, 1),
            Entry(7, 2),
            Entry(null, 3),
            Entry(9, 4, isIncluded: false),
        ]));

        Assert.Equal(new ArtBoardPlacement(1, true, 1), placements[5]);
        Assert.Equal(new ArtBoardPlacement(2, true, 2), placements[7]);
        Assert.Equal(new ArtBoardPlacement(3, true, 3), placements[ArtBoardItems.None]);
        Assert.Equal(new ArtBoardPlacement(4, false, null), placements[9]);
    }

    [Fact]
    public void An_empty_resolution_places_nothing()
    {
        var placements = ArtBoardItems.Placements(new ArtOrderResolution(ArtOrderMode.Empty,
            [new ArtOrderEntry(5, 1, true, ArtOrderOrigin.New)]));

        Assert.Empty(placements);
    }

    [Fact]
    public void An_art_order_ranks_its_items_and_leaves_unranked_features_without_a_rank()
    {
        var prioritization = TeamPrioritization.Resolve([PiOrder("Core Payments", 1, Pe, Entry(2, 1), Entry(1, 2))]);

        Assert.Equal("Core Payments", prioritization.OrderArt?.ArtName);
        Assert.Equal(1, prioritization.RankOf(Planned(20, 2, ranking: 9)));
        Assert.Equal(1, prioritization.SortKeyOf(Planned(20, 2, ranking: 9)));
        Assert.Equal(1, prioritization.ArtPositionOf(Planned(20, 2, ranking: 9)));
        Assert.Equal(2, prioritization.RankOf(Planned(10, 1, ranking: 1)));
        Assert.Null(prioritization.RankOf(Planned(30, 3, ranking: 7)));
        Assert.Null(prioritization.SortKeyOf(Planned(30, 3, ranking: 7)));
        Assert.Null(prioritization.ArtPositionOf(Planned(30, 3, ranking: 7)));
    }

    [Fact]
    public void Features_without_an_epic_take_the_place_of_the_no_epic_card()
    {
        var prioritization = TeamPrioritization.Resolve([General("Core Payments", 1, Pe, Entry(1, 1), Entry(null, 2))]);

        Assert.Equal(2, prioritization.RankOf(Planned(outcomeId: 10, epicId: null)));
        Assert.Equal(2, prioritization.RankOf(Planned(outcomeId: null, epicId: null)));
        Assert.Equal(2, prioritization.ArtPositionOf(Planned(outcomeId: null, epicId: null)));
    }

    [Fact]
    public void A_card_below_the_art_line_names_its_art_and_has_no_position()
    {
        var prioritization = TeamPrioritization.Resolve(
            [PiOrder("Core Payments", 1, Pe, Entry(2, 1), Entry(1, 2, isIncluded: false))]);

        Assert.Equal([new ArtLineRef(1, "Core Payments")], prioritization.BelowArtLineOf(Planned(10, 1)));
        Assert.Null(prioritization.ArtPositionOf(Planned(10, 1)));
        Assert.Equal(2, prioritization.RankOf(Planned(10, 1)));
        Assert.Equal(1, prioritization.ArtPositionOf(Planned(20, 2)));
        Assert.Empty(prioritization.BelowArtLineOf(Planned(20, 2)));
        Assert.Empty(prioritization.BelowArtLineOf(Planned(30, 3)));
    }

    [Fact]
    public void A_shared_team_is_below_the_line_of_any_of_its_arts_even_when_they_disagree_on_the_level()
    {
        var prioritization = TeamPrioritization.Resolve(
        [
            General("Risk Analytics", 3, Pe, Entry(1, 1, isIncluded: false)),
            PiOrder("Data Platform", 5, Bo, Entry(10, 1)),
        ]);

        Assert.Null(prioritization.Mode);
        Assert.Null(prioritization.OrderArt);
        Assert.Equal([new ArtLineRef(3, "Risk Analytics")], prioritization.BelowArtLineOf(Planned(10, 1)));
    }

    [Fact]
    public void A_pi_order_beats_a_general_order_on_a_shared_team()
    {
        var prioritization = TeamPrioritization.Resolve(
        [
            General("Mobile Banking", 2, Bo, Entry(10, 1), Entry(20, 2)),
            PiOrder("Data Platform", 5, Bo, Entry(20, 1), Entry(10, 4)),
        ]);

        Assert.Equal("Data Platform", prioritization.OrderArt?.ArtName);
        Assert.Empty(prioritization.CompetingBoardArtNames);
        Assert.Equal(4, prioritization.RankOf(Planned(10, null, ranking: 1)));
    }

    [Fact]
    public void The_only_general_order_orders_a_shared_team_when_no_art_has_a_pi_order()
    {
        var prioritization = TeamPrioritization.Resolve(
        [
            new TeamArtPrioritization("Mobile Banking", Bo) { ArtId = 2 },
            General("Data Platform", 5, Bo, Entry(10, 3)),
        ]);

        Assert.Equal("Data Platform", prioritization.OrderArt?.ArtName);
        Assert.Equal(ArtOrderMode.FollowsGeneral, prioritization.OrderArt?.Kind);
        Assert.Equal(3, prioritization.RankOf(Planned(10, null, ranking: 4)));
    }

    [Fact]
    public void Two_pi_orders_of_the_same_level_leave_the_order_to_the_feature_ranking()
    {
        var prioritization = TeamPrioritization.Resolve(
        [
            PiOrder("Mobile Banking", 2, Bo, Entry(10, 1)),
            PiOrder("Data Platform", 5, Bo, Entry(10, 3)),
            General("Risk Analytics", 3, Bo, Entry(10, 2)),
        ]);

        Assert.Null(prioritization.OrderArt);
        Assert.Equal(["Data Platform", "Mobile Banking"], prioritization.CompetingBoardArtNames);
        Assert.Null(prioritization.RankOf(Planned(10, null, ranking: 4)));
        Assert.Equal(4, prioritization.SortKeyOf(Planned(10, null, ranking: 4)));
        Assert.Null(prioritization.ArtPositionOf(Planned(10, null, ranking: 4)));
    }

    [Fact]
    public void Two_general_orders_of_the_same_level_compete_too()
    {
        var prioritization = TeamPrioritization.Resolve(
        [
            General("Mobile Banking", 2, Bo, Entry(10, 1)),
            General("Data Platform", 5, Bo, Entry(10, 3)),
        ]);

        Assert.Null(prioritization.OrderArt);
        Assert.Equal(["Data Platform", "Mobile Banking"], prioritization.CompetingBoardArtNames);
    }

    [Fact]
    public void An_art_without_an_order_never_orders_the_team()
    {
        var prioritization = TeamPrioritization.Resolve([Board("Mobile Banking", 2, Bo, ArtOrderMode.Empty, Entry(10, 1))]);

        Assert.Equal(Bo, prioritization.Mode);
        Assert.Null(prioritization.OrderArt);
        Assert.Empty(prioritization.CompetingBoardArtNames);
        Assert.Equal(4, prioritization.SortKeyOf(Planned(10, null, ranking: 4)));
    }
}
