using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.DepartmentPlanning;

public class PlanningPivotTests
{
    private const int CorePayments = 1;
    private const int DataPlatform = 2;
    private const int Argon = 1;
    private const int Zeta = 3;
    private const int GrowPayments = 1;
    private const int Resilience = 2;
    private const int InstantPayments = 10;
    private const int Tokenisation = 11;
    private const int Fraud = 13;

    private static PlanningFeatureTeam Link(int teamId, int sp, bool belowArtLine = false) =>
        new(teamId, sp, true, belowArtLine ? [new ArtLineRef(CorePayments, "Core Payments")] : [], 1, null, 0m);

    private static PlanningFeature Feature(int id, int outcomeId, int epicId, params PlanningFeatureTeam[] teams) =>
        new(id, $"PAY-{id}", $"Feature {id}", "In Progress", null, null, id, 3, "PI 26.09", outcomeId, epicId, null, null, null, teams);

    private static DepartmentPlanningData Data() => new(
        new DepartmentPlanningScope(1, [CorePayments, DataPlatform], 3),
        3,
        "PI 26.09",
        false,
        null,
        null,
        [
            new PlanningArt(CorePayments, "Core Payments", "CP", 1, "Retail", ArtPrioritization.PortfolioEpic, ArtOrderMode.General, [Argon],
            [
                new ArtOrderEntry(InstantPayments, 1, true, ArtOrderOrigin.Saved),
                new ArtOrderEntry(Fraud, 2, true, ArtOrderOrigin.Saved),
                new ArtOrderEntry(Tokenisation, 3, false, ArtOrderOrigin.Saved),
            ]),
            new PlanningArt(DataPlatform, "Data Platform", "DP", 1, "Retail", ArtPrioritization.BusinessOutcome, ArtOrderMode.Empty, [Zeta], []),
        ],
        [
            new PlanningTeam(Argon, "Argon", [CorePayments], 1m, 100d, 100m, 5, "Sprints"),
            new PlanningTeam(Zeta, "Zeta", [DataPlatform], 1m, 40d, 40m, 2, "PI"),
        ],
        [
            Feature(1, 100, InstantPayments, Link(Argon, 30)),
            Feature(2, 100, InstantPayments, Link(Argon, 50)),
            Feature(3, 102, Tokenisation, Link(Argon, 20, belowArtLine: true)),
            Feature(4, 104, Fraud, Link(Argon, 10)),
            Feature(5, 103, 12, Link(Zeta, 15)),
        ],
        [
            new PlanningObjective(GrowPayments, "STRAT-1", "Grow payments", null, null, [CorePayments]),
            new PlanningObjective(Resilience, "STRAT-2", "Resilience", null, null, [CorePayments]),
        ],
        [
            new PlanningEpic(InstantPayments, "EPIC-10", "Instant payments", null, null, null, null, null, [GrowPayments]),
            new PlanningEpic(Tokenisation, "EPIC-11", "Tokenisation", null, null, null, null, null, [GrowPayments, Resilience]),
            new PlanningEpic(Fraud, "EPIC-13", "Fraud", null, null, null, null, null, [Resilience]),
            new PlanningEpic(12, "EPIC-12", "Data lake", null, null, null, null, null, []),
        ],
        [
            new PlanningOutcome(100, "BO-100", "Rails", null, null, null, InstantPayments, null, null),
            new PlanningOutcome(102, "BO-102", "Vault", null, null, null, Tokenisation, null, null),
            new PlanningOutcome(103, "BO-103", "Ingestion", null, null, null, 12, null, null),
            new PlanningOutcome(104, "BO-104", "Scoring", null, null, null, Fraud, null, null),
        ]);

    private static PlanningTree Tree(PlanningLens lens = PlanningLens.Objectives, PlanningSort sortBy = PlanningSort.Priority, bool sortDescending = false) =>
        DepartmentPlanningTree.Build(Data(), new PlanningOptions { Lens = lens, SortBy = sortBy, SortDescending = sortDescending });

    [Fact]
    public void Level_zero_keeps_the_tree_as_it_is()
    {
        var tree = Tree();

        var pivot = PlanningPivot.Build(tree, 0);

        Assert.False(pivot.IsPivoted);
        Assert.Equal(tree.Roots.Select(r => r.Id), pivot.Roots.Select(r => r.Row.Id));
        Assert.All(pivot.Roots, r => Assert.Empty(r.Ancestors));
        Assert.Empty(pivot.AncestorLevels);
    }

    [Fact]
    public void Portfolio_epics_become_the_top_rows_with_their_objective_as_a_column()
    {
        var pivot = PlanningPivot.Build(Tree(), 1);

        Assert.Equal(["so:1/pe:10", "so:1/pe:11", "so:2/pe:13", "so:2/pe:11", "so:0/pe:12"], pivot.Roots.Select(r => r.Row.Id));
        Assert.All(pivot.Roots, r => Assert.Equal(PlanningLevel.PortfolioEpic, r.Row.Level));
        Assert.Equal([PlanningLevel.StrategicObjective], pivot.AncestorLevels);
        Assert.Equal("STRAT-2", pivot.Roots[3].Ancestors[PlanningLevel.StrategicObjective].JiraId);
        Assert.True(pivot.Roots[4].Ancestors[PlanningLevel.StrategicObjective].IsBucket);
    }

    [Fact]
    public void Features_list_every_parent_level_as_columns()
    {
        var pivot = PlanningPivot.Build(Tree(), 3);

        Assert.All(pivot.Roots, r => Assert.Equal(PlanningLevel.BusinessEpic, r.Row.Level));
        Assert.Equal([PlanningLevel.StrategicObjective, PlanningLevel.PortfolioEpic, PlanningLevel.BusinessOutcome], pivot.AncestorLevels);
        var first = pivot.Roots[0];
        Assert.Equal("STRAT-1", first.Ancestors[PlanningLevel.StrategicObjective].JiraId);
        Assert.Equal("EPIC-10", first.Ancestors[PlanningLevel.PortfolioEpic].JiraId);
        Assert.Equal("BO-100", first.Ancestors[PlanningLevel.BusinessOutcome].JiraId);
        Assert.Equal(6, pivot.Roots.Count);
    }

    [Fact]
    public void Children_stay_under_their_pivot_row_and_indent_from_it()
    {
        var pivot = PlanningPivot.Build(Tree(), 1);

        var collapsed = pivot.Flatten(_ => false).ToList();
        var expanded = pivot.Flatten(_ => true).ToList();

        Assert.Equal(5, collapsed.Count);
        Assert.All(collapsed, line => Assert.Equal(0, line.Indent));
        var outcome = expanded.First(line => line.Row.Level == PlanningLevel.BusinessOutcome);
        Assert.Equal(1, outcome.Indent);
        var feature = expanded.First(line => line.Row.Level == PlanningLevel.BusinessEpic);
        Assert.Equal(2, feature.Indent);
        Assert.Equal("so:1/pe:10", feature.Root.Row.Id);
    }

    [Fact]
    public void A_column_sort_orders_the_pivot_rows_as_one_list()
    {
        var grouped = PlanningPivot.Build(Tree(), 3);
        var sorted = PlanningPivot.Build(Tree(), 3, PlanningSort.Demand, sortDescending: true);

        Assert.Equal(["PAY-1", "PAY-2", "PAY-3", "PAY-4", "PAY-3", "PAY-5"], grouped.Roots.Select(r => r.Row.JiraId));
        Assert.Equal(["PAY-2", "PAY-1", "PAY-3", "PAY-3", "PAY-5", "PAY-4"], sorted.Roots.Select(r => r.Row.JiraId));
    }

    [Fact]
    public void Drawdown_ranked_items_keep_the_art_line_and_name_the_art_in_a_column()
    {
        var pivot = PlanningPivot.Build(Tree(PlanningLens.Drawdown), 1);

        Assert.Equal(
            [PlanningRowKind.Node, PlanningRowKind.Node, PlanningRowKind.LineMarker, PlanningRowKind.Node, PlanningRowKind.Node],
            pivot.Roots.Select(r => r.Row.Kind));
        Assert.Equal([PlanningLevel.Art], pivot.AncestorLevels);
        Assert.Equal("Data Platform", pivot.Roots[^1].Ancestors[PlanningLevel.Art].Title);
        Assert.Equal(PlanningLevel.BusinessOutcome, pivot.Roots[^1].Row.Level);
    }

    [Fact]
    public void A_column_sort_keeps_drawdown_ranked_items_in_rank_order_with_the_art_line()
    {
        var tree = Tree(PlanningLens.Drawdown, PlanningSort.Demand, sortDescending: true);

        var pivot = PlanningPivot.Build(tree, 1, PlanningSort.Demand, sortDescending: true);

        Assert.Equal(
            ["EPIC-10", "EPIC-13", "Core Payments line", "EPIC-11", "BO-103"],
            pivot.Roots.Select(r => r.Row.Kind == PlanningRowKind.LineMarker ? r.Row.Title : r.Row.JiraId));
        Assert.Equal([80m, 90m, null, null, 15m], pivot.Roots.Select(r => r.Row.CumulativePlannedSp));
        Assert.Equal("Data Platform", pivot.Roots[^1].Ancestors[PlanningLevel.Art].Title);
        var outcome = Assert.Single(pivot.Roots[0].Row.Children);
        Assert.Equal(["PAY-2", "PAY-1"], outcome.Children.Select(f => f.JiraId));
    }

    [Fact]
    public void A_column_sort_orders_drawdown_features_as_one_list()
    {
        var tree = Tree(PlanningLens.Drawdown, PlanningSort.Demand, sortDescending: true);

        var grouped = PlanningPivot.Build(tree, 3);
        var sorted = PlanningPivot.Build(tree, 3, PlanningSort.Demand, sortDescending: true);

        Assert.Equal(["PAY-2", "PAY-1", "PAY-4", "PAY-3", "PAY-5"], grouped.Roots.Select(r => r.Row.JiraId));
        Assert.Equal(["PAY-2", "PAY-1", "PAY-3", "PAY-5", "PAY-4"], sorted.Roots.Select(r => r.Row.JiraId));
    }

    [Fact]
    public void A_share_sort_orders_drawdown_features_of_arts_with_different_capacities_by_the_share_shown()
    {
        var tree = Tree(PlanningLens.Drawdown, PlanningSort.Share, sortDescending: true);

        var byShare = PlanningPivot.Build(tree, 3, PlanningSort.Share, sortDescending: true);
        var byPlanned = PlanningPivot.Build(tree, 3, PlanningSort.Planned, sortDescending: true);
        var ascending = PlanningPivot.Build(tree, 3, PlanningSort.Share);

        Assert.Equal(["PAY-2", "PAY-5", "PAY-1", "PAY-4", "PAY-3"], byShare.Roots.Select(r => r.Row.JiraId));
        var shares = byShare.Roots.Select(r => r.Row.ShareOfCapacity!.Value).ToList();
        Assert.Equal(shares.OrderDescending(), shares);
        Assert.Equal(["PAY-2", "PAY-1", "PAY-5", "PAY-4", "PAY-3"], byPlanned.Roots.Select(r => r.Row.JiraId));
        Assert.Equal(["PAY-3", "PAY-4", "PAY-1", "PAY-5", "PAY-2"], ascending.Roots.Select(r => r.Row.JiraId));
    }

    [Fact]
    public void A_share_sort_puts_rows_without_capacity_last_both_ways()
    {
        var data = Data() with { Teams = Data().Teams.Select(t => t.Id == Zeta ? t with { CapacitySp = 0m } : t).ToList() };
        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown });

        var descending = PlanningPivot.Build(tree, 3, PlanningSort.Share, sortDescending: true);
        var ascending = PlanningPivot.Build(tree, 3, PlanningSort.Share);

        Assert.Null(descending.Roots.Single(r => r.Row.JiraId == "PAY-5").Row.ShareOfCapacity);
        Assert.Equal(["PAY-2", "PAY-1", "PAY-4", "PAY-3", "PAY-5"], descending.Roots.Select(r => r.Row.JiraId));
        Assert.Equal(["PAY-3", "PAY-4", "PAY-1", "PAY-2", "PAY-5"], ascending.Roots.Select(r => r.Row.JiraId));
    }

    [Fact]
    public void Drawdown_outcomes_include_outcome_ranked_arts_without_an_epic_column_value()
    {
        var pivot = PlanningPivot.Build(Tree(PlanningLens.Drawdown), 2);

        Assert.All(pivot.Roots, r => Assert.Equal(PlanningLevel.BusinessOutcome, r.Row.Level));
        Assert.Equal([PlanningLevel.Art, PlanningLevel.PortfolioEpic], pivot.AncestorLevels);
        var dataPlatform = pivot.Roots.Single(r => r.Row.EntityId == 103);
        Assert.False(dataPlatform.Ancestors.ContainsKey(PlanningLevel.PortfolioEpic));
        Assert.Equal("Data Platform", dataPlatform.Ancestors[PlanningLevel.Art].Title);
    }
}
