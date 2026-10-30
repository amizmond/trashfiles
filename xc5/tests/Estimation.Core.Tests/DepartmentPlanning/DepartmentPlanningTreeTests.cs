using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.DepartmentPlanning;

public class DepartmentPlanningTreeTests
{
    private const int CorePayments = 1;
    private const int DataPlatform = 2;
    private const int Argon = 1;
    private const int Boron = 2;
    private const int Zeta = 3;
    private const int GrowPayments = 1;
    private const int Resilience = 2;
    private const int EmptyObjective = 3;
    private const int InstantPayments = 10;
    private const int Tokenisation = 11;
    private const int DataLake = 12;
    private const int Fraud = 13;
    private const int EmptyEpic = 14;

    private static readonly ArtLineRef CorePaymentsLine = new(CorePayments, "Core Payments");

    private static PlanningFeatureTeam Link(int teamId, int sp, bool included = true, bool belowArtLine = false, int position = 1, decimal reserved = 0m) =>
        new(teamId, sp, included, belowArtLine ? [CorePaymentsLine] : [], position, null, reserved);

    private static PlanningFeature Feature(int id, int? outcomeId, int? epicId, string status, string? rag, params PlanningFeatureTeam[] teams) =>
        new(id, $"PAY-{id}", $"Feature {id}", status, rag, null, id, 3, "PI 26.09", outcomeId, epicId, null, new DateTime(2026, 11, 1).AddDays(id), null, teams);

    private static DepartmentPlanningData Data()
    {
        var scope = new DepartmentPlanningScope(1, [CorePayments, DataPlatform], 3);
        var arts = new List<PlanningArt>
        {
            new(CorePayments, "Core Payments", "CP", 1, "Retail", ArtPrioritization.PortfolioEpic, ArtOrderMode.General, [Argon, Boron],
            [
                new ArtOrderEntry(InstantPayments, 1, true, ArtOrderOrigin.Saved),
                new ArtOrderEntry(Fraud, 2, true, ArtOrderOrigin.Saved),
                new ArtOrderEntry(ArtBoardItems.None, 3, true, ArtOrderOrigin.New),
                new ArtOrderEntry(Tokenisation, 4, false, ArtOrderOrigin.Saved),
            ]),
            new(DataPlatform, "Data Platform", "DP", 1, "Retail", ArtPrioritization.BusinessOutcome, ArtOrderMode.Empty, [Zeta], []),
        };
        var teams = new List<PlanningTeam>
        {
            new(Argon, "Argon", [CorePayments], 1m, 100d, 100m, 5, "Sprints"),
            new(Boron, "Boron", [CorePayments], 1m, 50d, 50m, 3, "Sprints"),
            new(Zeta, "Zeta", [DataPlatform], 1m, 40d, 40m, 2, "PI"),
        };
        var features = new List<PlanningFeature>
        {
            Feature(1, 100, InstantPayments, "In Progress", "Green", Link(Argon, 30, position: 1), Link(Boron, 20, position: 1)) with { DependencyIds = ["f1>f8", "f9>f1"], RiskyDependencyIds = ["f1>f8"], HygieneFailures = 1 },
            Feature(2, 101, InstantPayments, "Closed", null, Link(Argon, 40, position: 2)),
            Feature(3, 102, Tokenisation, "In Progress", "Red", Link(Argon, 50, belowArtLine: true, position: 4)),
            Feature(4, null, null, "Blocked", null, Link(Boron, 25, included: false, position: 3)),
            Feature(5, 103, DataLake, "To Do", "Amber", Link(Zeta, 30, position: 1)),
            Feature(6, 100, InstantPayments, "To Do", null, Link(Argon, 45, position: 3)),
            Feature(7, 104, Fraud, "In Development", null, Link(Boron, 30, position: 2)),
        };
        var objectives = new List<PlanningObjective>
        {
            new(GrowPayments, "STRAT-1", "Grow payments", "In Progress", null, [CorePayments]),
            new(Resilience, "STRAT-2", "Resilience", "In Progress", null, [CorePayments]),
            new(EmptyObjective, "STRAT-3", "Empty objective", "To Do", null, [CorePayments]),
        };
        var epics = new List<PlanningEpic>
        {
            new(InstantPayments, "EPIC-10", "Instant payments", "In Progress", "Green", null, null, null, [GrowPayments]),
            new(Tokenisation, "EPIC-11", "Tokenisation", "In Progress", null, "Unfunded", null, null, [GrowPayments, Resilience]),
            new(DataLake, "EPIC-12", "Data lake", "Funnel", null, null, null, null, []),
            new(Fraud, "EPIC-13", "Fraud", "Analysis", null, null, null, null, [Resilience]),
            new(EmptyEpic, "EPIC-14", "Nothing planned", "Funnel", null, null, null, null, [EmptyObjective]),
        };
        var outcomes = new List<PlanningOutcome>
        {
            new(100, "BO-100", "Rails", "In Progress", "Amber", "Vendor late", InstantPayments, null, null),
            new(101, "BO-101", "Notifications", "Done", null, null, InstantPayments, null, null),
            new(102, "BO-102", "Token vault", "In Progress", "Red", null, Tokenisation, null, null),
            new(103, "BO-103", "Ingestion", "To Do", null, null, DataLake, null, null),
            new(104, "BO-104", "Scoring", "In Progress", null, null, Fraud, null, null),
            new(105, "BO-105", "Empty outcome", "Funnel", null, null, EmptyEpic, null, null),
        };

        return new DepartmentPlanningData(scope, 3, "PI 26.09", false, null, null, arts, teams, features, objectives, epics, outcomes);
    }

    private static PlanningTree Build(PlanningOptions? options = null) => DepartmentPlanningTree.Build(Data(), options ?? new PlanningOptions());

    private static PlanningRow Child(PlanningRow row, int? entityId) => row.Children.Single(c => c.Kind == PlanningRowKind.Node && c.EntityId == entityId);

    [Fact]
    public void Objectives_lens_groups_objectives_epics_outcomes_and_business_epics()
    {
        var tree = Build();

        Assert.Equal([GrowPayments, Resilience, null], tree.Roots.Select(r => r.EntityId));
        var grow = tree.Roots[0];
        Assert.Equal(PlanningLevel.StrategicObjective, grow.Level);
        Assert.Equal([InstantPayments, Tokenisation], grow.Children.Select(c => c.EntityId));
        var instant = Child(grow, InstantPayments);
        Assert.Equal([100, 101], instant.Children.Select(c => c.EntityId).Order());
        Assert.Equal([1, 6], Child(instant, 100).Children.Select(c => c.EntityId));
        Assert.All(Child(instant, 100).Children, c => Assert.Equal(PlanningLevel.BusinessEpic, c.Level));
    }

    [Fact]
    public void Metrics_add_up_the_business_epics_underneath_and_the_total_counts_each_once()
    {
        var tree = Build();
        var grow = tree.Roots[0];

        Assert.Equal(4, grow.Metrics.Features);
        Assert.Equal(1, grow.Metrics.Done);
        Assert.Equal(185m, grow.Metrics.DemandSp);
        Assert.Equal(135m, grow.Metrics.PlannedSp);
        Assert.Equal(50m, grow.Metrics.UnplannedSp);
        Assert.Equal(2, grow.Metrics.Dependencies);
        Assert.Equal(1, grow.Metrics.DependenciesAtRisk);
        Assert.Equal(1, grow.Metrics.HygieneFailingFeatures);
        Assert.Equal("Red", grow.Metrics.WorstRag);
        Assert.Equal(2, grow.Metrics.Teams);
        Assert.Equal(1, grow.Metrics.Arts);

        Assert.Equal(7, tree.Total.Features);
        Assert.Equal(270m, tree.Total.DemandSp);
        Assert.Equal(195m, tree.Total.PlannedSp);
        Assert.Equal(75m, tree.Total.UnplannedSp);
        Assert.Equal(190m, tree.TotalCapacitySp);
        Assert.Equal(7, tree.FeaturesInScope);
        Assert.Equal(7, tree.FeaturesShown);
    }

    [Fact]
    public void A_dependency_between_two_features_counts_once_in_their_parents_and_the_total()
    {
        var data = Data();
        var features = data.Features.Select(f => f.Id switch
        {
            1 => f with { DependencyIds = ["f1>f6", "f1>f8"], RiskyDependencyIds = ["f1>f6"] },
            6 => f with { DependencyIds = ["f1>f6"], RiskyDependencyIds = ["f1>f6"] },
            _ => f,
        }).ToList();
        var tree = DepartmentPlanningTree.Build(data with { Features = features }, new PlanningOptions());

        var instant = Child(tree.Roots[0], InstantPayments);
        Assert.Equal(2, instant.Metrics.Dependencies);
        Assert.Equal(1, instant.Metrics.DependenciesAtRisk);
        Assert.Equal(2, instant.Metrics.FeaturesAtRisk);
        var six = Child(Child(instant, 100), 6);
        Assert.Equal(1, six.Metrics.Dependencies);
        Assert.Equal(1, six.Metrics.DependenciesAtRisk);
        Assert.Equal(2, tree.Total.Dependencies);
        Assert.Equal(1, tree.Total.DependenciesAtRisk);
    }

    [Fact]
    public void A_shared_epic_appears_under_each_objective_and_names_the_other_one()
    {
        var tree = Build();

        var underGrow = Child(tree.Roots[0], Tokenisation);
        var underResilience = Child(tree.Roots[1], Tokenisation);

        Assert.Equal(["STRAT-2"], underGrow.AlsoUnder);
        Assert.Equal(["STRAT-1"], underResilience.AlsoUnder);
        Assert.Equal(underGrow.Metrics, underResilience.Metrics);
        Assert.NotEqual(underGrow.Id, underResilience.Id);
    }

    [Fact]
    public void Ranks_come_from_the_art_order_and_mark_items_below_the_line()
    {
        var tree = Build();

        var instant = Child(tree.Roots[0], InstantPayments);
        var tokenisation = Child(tree.Roots[0], Tokenisation);
        var fraud = Child(tree.Roots[1], Fraud);

        Assert.Equal([("CP", 1, false)], instant.Ranks.Select(r => (r.ArtAbbreviation, r.Rank, r.BelowLine)));
        Assert.Equal([("CP", 4, true)], tokenisation.Ranks.Select(r => (r.ArtAbbreviation, r.Rank, r.BelowLine)));
        Assert.True(tokenisation.IsBelowArtLine);
        Assert.False(instant.IsBelowArtLine);
        Assert.Equal(2, fraud.SortRank);
        Assert.Equal([InstantPayments, Tokenisation], tree.Roots[0].Children.Select(c => c.EntityId));
        Assert.Equal([Fraud, Tokenisation], tree.Roots[1].Children.Select(c => c.EntityId));
    }

    [Fact]
    public void Business_epics_without_hierarchy_land_in_buckets_at_the_end()
    {
        var tree = Build();
        var bucket = tree.Roots[2];

        Assert.True(bucket.IsBucket);
        Assert.Equal(DepartmentPlanningTree.NoObjectiveTitle, bucket.Title);
        Assert.Equal([DataLake, null], bucket.Children.Select(c => c.EntityId));
        var noEpic = Child(bucket, null);
        Assert.Equal(DepartmentPlanningTree.NoEpicTitle, noEpic.Title);
        var noOutcome = Child(noEpic, null);
        Assert.Equal(DepartmentPlanningTree.NoOutcomeTitle, noOutcome.Title);
        Assert.Equal([4], noOutcome.Children.Select(c => c.EntityId));
        Assert.Equal(25m, noOutcome.Metrics.UnplannedSp);
    }

    [Fact]
    public void Share_of_capacity_uses_the_capacity_of_the_teams_in_scope()
    {
        var tree = Build();

        Assert.Equal(135d / 190d, tree.Roots[0].ShareOfCapacity!.Value, 6);
    }

    [Fact]
    public void Drawdown_lens_lists_each_art_in_rank_order_with_cumulative_demand_and_the_line()
    {
        var tree = Build(new PlanningOptions { Lens = PlanningLens.Drawdown });

        Assert.Equal(["Core Payments", "Data Platform"], tree.Roots.Select(r => r.Title));
        var core = tree.Roots[0];
        Assert.Equal(150m, core.CapacitySp);
        Assert.Equal(165m, core.Metrics.PlannedSp);

        Assert.Equal(
            [(PlanningRowKind.Node, InstantPayments), (PlanningRowKind.Node, Fraud), (PlanningRowKind.Node, null), (PlanningRowKind.LineMarker, null), (PlanningRowKind.Node, Tokenisation)],
            core.Children.Select(c => (c.Kind, c.EntityId)));
        Assert.Equal(150m, core.Children[3].CapacitySp);
        Assert.Equal(150d, core.Children[3].CapacityDays);

        var instant = core.Children[0];
        Assert.Equal(135m, instant.CumulativePlannedSp);
        Assert.False(instant.OverCapacity);
        Assert.Equal(1, instant.Ranks.Single().Rank);

        var fraud = core.Children[1];
        Assert.Equal(165m, fraud.CumulativePlannedSp);
        Assert.True(fraud.OverCapacity);
        Assert.True(fraud.CapacityRunsOutHere);

        var bucket = core.Children[2];
        Assert.True(bucket.OverCapacity);
        Assert.False(bucket.CapacityRunsOutHere);
        Assert.Equal(165m, bucket.CumulativePlannedSp);

        var tokenisation = core.Children[4];
        Assert.True(tokenisation.IsBelowArtLine);
        Assert.Null(tokenisation.CumulativePlannedSp);
        Assert.Equal(0m, tokenisation.Metrics.PlannedSp);
        Assert.Equal(50m, tokenisation.Metrics.UnplannedSp);
    }

    [Fact]
    public void Filters_change_neither_an_art_s_share_of_capacity_nor_the_department_s()
    {
        var all = Build(new PlanningOptions { Lens = PlanningLens.Drawdown });
        var filtered = Build(new PlanningOptions { Lens = PlanningLens.Drawdown, HideDone = true });

        Assert.Equal(125m, filtered.Roots[0].Metrics.PlannedSp);
        Assert.Equal(165d / 150d, filtered.Roots[0].ShareOfCapacity!.Value, 6);
        Assert.Equal(all.Roots[0].ShareOfCapacity, filtered.Roots[0].ShareOfCapacity);
        Assert.False(all.HidesFeatures);
        Assert.True(filtered.HidesFeatures);
        Assert.Equal(195d / 190d, filtered.ScopeShareOfCapacity!.Value, 6);
        Assert.Equal(all.ScopeShareOfCapacity, filtered.ScopeShareOfCapacity);
    }

    [Fact]
    public void Drawdown_team_columns_follow_each_team_s_own_capacity()
    {
        var tree = Build(new PlanningOptions { Lens = PlanningLens.Drawdown });
        var instant = tree.Roots[0].Children[0];

        var argon = instant.TeamCells[Argon];
        Assert.Equal(115m, argon.PlannedSp);
        Assert.Equal(115m, argon.CumulativeSp);
        Assert.True(argon.OverCapacity);

        var boron = instant.TeamCells[Boron];
        Assert.Equal(20m, boron.CumulativeSp);
        Assert.False(boron.OverCapacity);

        var fraud = tree.Roots[0].Children[1];
        Assert.Equal(50m, fraud.TeamCells[Boron].CumulativeSp);
        Assert.False(fraud.TeamCells[Boron].OverCapacity);
    }

    [Fact]
    public void Drawdown_counts_every_feature_on_the_boards_while_a_filter_hides_some()
    {
        var tree = Build(new PlanningOptions { Lens = PlanningLens.Drawdown, HideDone = true });
        var core = tree.Roots[0];

        var instant = Child(core, InstantPayments);
        Assert.Equal(95m, instant.Metrics.PlannedSp);
        Assert.Equal(135m, instant.CumulativePlannedSp);
        Assert.Equal(115m, instant.TeamCells[Argon].CumulativeSp);
        Assert.True(instant.TeamCells[Argon].OverCapacity);

        var fraud = Child(core, Fraud);
        Assert.Equal(165m, fraud.CumulativePlannedSp);
        Assert.True(fraud.CapacityRunsOutHere);

        Assert.Equal(165m, core.CumulativePlannedSp);
        Assert.True(core.OverCapacity);
        Assert.Equal(115m, core.TeamCells[Argon].CumulativeSp);
        Assert.True(core.TeamCells[Argon].OverCapacity);
        Assert.Equal(50m, core.TeamCells[Boron].CumulativeSp);
        Assert.False(core.TeamCells[Boron].OverCapacity);
    }

    [Fact]
    public void Drawdown_search_keeps_the_real_place_of_the_found_item()
    {
        var tree = Build(new PlanningOptions { Lens = PlanningLens.Drawdown, Search = "Fraud" });
        var core = tree.Roots[0];

        var fraud = core.Children.Single(c => c.Kind == PlanningRowKind.Node);
        Assert.Equal(Fraud, fraud.EntityId);
        Assert.Equal(30m, fraud.Metrics.PlannedSp);
        Assert.Equal(165m, fraud.CumulativePlannedSp);
        Assert.True(fraud.CapacityRunsOutHere);
    }

    [Fact]
    public void Drawdown_marks_no_visible_row_when_capacity_runs_out_on_a_hidden_item()
    {
        var tree = Build(new PlanningOptions { Lens = PlanningLens.Drawdown, OnlyAtRisk = true });
        var core = tree.Roots[0];

        Assert.Equal([InstantPayments, null, Tokenisation], core.Children.Where(c => c.Kind == PlanningRowKind.Node).Select(c => c.EntityId));
        var bucket = Child(core, null);
        Assert.Equal(165m, bucket.CumulativePlannedSp);
        Assert.True(bucket.OverCapacity);
        Assert.DoesNotContain(core.Children, c => c.CapacityRunsOutHere);
    }

    [Fact]
    public void Drawdown_outcomes_under_an_epic_ranked_art_carry_no_rank_of_another_art()
    {
        var data = Data();
        var platform = data.Arts.Single(a => a.Id == DataPlatform);
        var ranked = new PlanningArt(platform.Id, platform.Name, platform.Abbreviation, platform.DepartmentId, platform.DepartmentName, platform.Level,
            ArtOrderMode.PiOrder, platform.TeamIds, [new ArtOrderEntry(100, 7, false, ArtOrderOrigin.Saved)]);
        data = data with { Arts = data.Arts.Select(a => a.Id == DataPlatform ? ranked : a).ToList() };

        var drawdown = DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown });
        var rails = Child(Child(drawdown.Roots[0], InstantPayments), 100);
        Assert.Empty(rails.Ranks);
        Assert.False(rails.IsBelowArtLine);
        Assert.Equal(1, rails.SortRank);
        Assert.False(rails.HasArtRank);

        var objectives = DepartmentPlanningTree.Build(data, new PlanningOptions());
        var railsUnderObjective = Child(Child(objectives.Roots[0], InstantPayments), 100);
        Assert.Equal([("DP", 7, true)], railsUnderObjective.Ranks.Select(r => (r.ArtAbbreviation, r.Rank, r.BelowLine)));
        Assert.False(railsUnderObjective.IsBelowArtLine);
        Assert.True(railsUnderObjective.HasArtRank);
    }

    [Fact]
    public void An_item_is_below_the_line_only_while_art_lines_cut_all_of_its_work()
    {
        var tree = Build();
        var tokenisation = Child(tree.Roots.Single(r => r.EntityId == GrowPayments), Tokenisation);
        Assert.True(tokenisation.IsBelowArtLine);
        Assert.True(Child(tokenisation, 102).IsBelowArtLine);

        var data = Data();
        data = data with { Features = [.. data.Features, Feature(8, 102, Tokenisation, "To Do", null, Link(Zeta, 10))] };
        var shared = Child(DepartmentPlanningTree.Build(data, new PlanningOptions()).Roots.Single(r => r.EntityId == GrowPayments), Tokenisation);
        Assert.Equal([("CP", 4, true)], shared.Ranks.Select(r => (r.ArtAbbreviation, r.Rank, r.BelowLine)));
        Assert.False(shared.IsBelowArtLine);
        Assert.False(Child(shared, 102).IsBelowArtLine);

        var filtered = DepartmentPlanningTree.Build(data, new PlanningOptions { OnlyUnplanned = true });
        Assert.False(Child(filtered.Roots.Single(r => r.EntityId == GrowPayments), Tokenisation).IsBelowArtLine);
    }

    [Fact]
    public void An_item_without_work_in_scope_is_below_the_line_by_its_ranks()
    {
        var data = Data();
        var core = data.Arts.Single(a => a.Id == CorePayments);
        var ranked = new PlanningArt(core.Id, core.Name, core.Abbreviation, core.DepartmentId, core.DepartmentName, core.Level, core.OrderMode, core.TeamIds,
            [.. core.Order, new ArtOrderEntry(EmptyEpic, 5, false, ArtOrderOrigin.Saved)]);
        data = data with { Arts = data.Arts.Select(a => a.Id == CorePayments ? ranked : a).ToList() };

        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions { IncludeEmptyNodes = true });

        Assert.True(Child(tree.Roots.Single(r => r.EntityId == EmptyObjective), EmptyEpic).IsBelowArtLine);
    }

    [Fact]
    public void An_art_ranking_business_outcomes_without_a_saved_order_has_no_line()
    {
        var tree = Build(new PlanningOptions { Lens = PlanningLens.Drawdown });
        var data = tree.Roots[1];

        Assert.Equal(40m, data.CapacitySp);
        Assert.Equal([103], data.Children.Select(c => c.EntityId));
        Assert.DoesNotContain(data.Children, c => c.Kind == PlanningRowKind.LineMarker);
        var ingestion = data.Children[0];
        Assert.Equal(PlanningLevel.BusinessOutcome, ingestion.Level);
        Assert.Empty(ingestion.Ranks);
        Assert.Equal(30m, ingestion.CumulativePlannedSp);
        Assert.Equal([5], ingestion.Children.Select(c => c.EntityId));
    }

    [Fact]
    public void Drawdown_counts_only_the_art_s_own_teams()
    {
        var data = Data();
        var shared = data.Features[4] with { Teams = [Link(Zeta, 30), Link(Boron, 10)] };
        var features = data.Features.Select(f => f.Id == shared.Id ? shared : f).ToList();
        var tree = DepartmentPlanningTree.Build(data with { Features = features }, new PlanningOptions { Lens = PlanningLens.Drawdown });

        var dataPlatform = tree.Roots[1];
        Assert.Equal(30m, dataPlatform.Metrics.DemandSp);
        Assert.Equal(250m, tree.Roots[0].Metrics.DemandSp);
        Assert.Equal(7, tree.Total.Features);
        Assert.Equal(280m, tree.Total.DemandSp);
    }

    [Fact]
    public void Planned_story_points_per_team_count_a_feature_on_teams_of_two_arts_in_both_lenses()
    {
        var data = Data();
        var shared = data.Features[4] with { Teams = [Link(Zeta, 30), Link(Boron, 10)] };
        data = data with { Features = data.Features.Select(f => f.Id == shared.Id ? shared : f).ToList() };

        var drawdown = DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown });
        var objectives = DepartmentPlanningTree.Build(data, new PlanningOptions());

        var expected = new Dictionary<int, decimal> { [Argon] = 115m, [Boron] = 60m, [Zeta] = 30m };
        Assert.Equal(expected, drawdown.PlannedSpByTeam);
        Assert.Equal(expected, objectives.PlannedSpByTeam);
        Assert.Equal(30m, DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown, HideDone = true }).PlannedSpByTeam[Zeta]);
    }

    [Fact]
    public void Hide_done_removes_finished_business_epics_from_every_total()
    {
        var tree = Build(new PlanningOptions { HideDone = true });

        Assert.Equal(6, tree.Total.Features);
        Assert.Equal(3, tree.Roots[0].Metrics.Features);
        Assert.Equal([100], Child(tree.Roots[0], InstantPayments).Children.Select(c => c.EntityId));
        Assert.Equal(7, tree.FeaturesInScope);
        Assert.Equal(6, tree.FeaturesShown);
    }

    [Fact]
    public void Only_unplanned_keeps_business_epics_below_a_team_or_art_line()
    {
        var tree = Build(new PlanningOptions { OnlyUnplanned = true });

        var features = tree.FlattenAll().Where(r => r.Feature is not null).Select(r => r.Feature!.Id).Distinct().Order().ToList();
        Assert.Equal([3, 4], features);
    }

    [Fact]
    public void Only_at_risk_keeps_red_blocked_and_risky_business_epics()
    {
        var tree = Build(new PlanningOptions { OnlyAtRisk = true });

        var features = tree.FlattenAll().Where(r => r.Feature is not null).Select(r => r.Feature!.Id).Distinct().Order().ToList();
        Assert.Equal([1, 3, 4], features);
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("Closed")]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    public void Done_and_rejected_features_count_as_done_and_never_as_at_risk(string status)
    {
        var feature = Feature(9, 100, InstantPayments, status, "Red", Link(Argon, 10)) with { DependencyIds = ["f9>f1", "f9>f2"], RiskyDependencyIds = ["f9>f1", "f9>f2"] };

        Assert.True(feature.IsDone);
        Assert.False(DepartmentPlanningTree.IsAtRisk(feature));
        Assert.True(DepartmentPlanningTree.IsAtRisk(feature with { Status = "In Progress" }));
    }

    [Fact]
    public void Rag_filter_matches_normalised_colours_and_none()
    {
        var amber = Build(new PlanningOptions { RagStatuses = new HashSet<string> { "Amber" } });
        var none = Build(new PlanningOptions { RagStatuses = new HashSet<string> { PlanningOptions.NoRag } });

        Assert.Equal([5], amber.FlattenAll().Where(r => r.Feature is not null).Select(r => r.Feature!.Id));
        Assert.Equal([2, 4, 6, 7], none.FlattenAll().Where(r => r.Feature is not null).Select(r => r.Feature!.Id).Distinct().Order());
    }

    [Fact]
    public void Rag_choices_offer_every_value_in_the_data_between_the_colours_and_none()
    {
        var data = Data();
        var features = data.Features.Select(f => f.Id switch
        {
            2 => f with { RagStatus = "Blue – Delivered" },
            4 => f with { RagStatus = "blue – delivered " },
            6 => f with { RagStatus = "none" },
            _ => f,
        }).ToList();

        Assert.Equal(["Red", "Amber", "Green", "Blue – Delivered", PlanningOptions.NoRag], DepartmentPlanningTree.RagChoices(features));
        Assert.Equal(["Red", "Amber", "Green", PlanningOptions.NoRag], DepartmentPlanningTree.RagChoices([]));
    }

    [Fact]
    public void Rag_filter_keeps_features_with_a_value_that_is_not_a_colour()
    {
        var data = Data();
        var features = data.Features.Select(f => f.Id == 2 ? f with { RagStatus = "Blue – Delivered" } : f).ToList();
        var rags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Red", "Amber", "Green", "blue – delivered", PlanningOptions.NoRag };

        var tree = DepartmentPlanningTree.Build(data with { Features = features }, new PlanningOptions { RagStatuses = rags });

        Assert.Equal(7, tree.Total.Features);
    }

    [Fact]
    public void Search_keeps_matching_items_with_their_parents_and_children()
    {
        var tree = Build(new PlanningOptions { Search = "token" });

        Assert.Equal([GrowPayments, Resilience], tree.Roots.Select(r => r.EntityId));
        Assert.All(tree.Roots, r => Assert.Equal([Tokenisation], r.Children.Select(c => c.EntityId)));
        Assert.Equal([3], Child(Child(tree.Roots[0], Tokenisation), 102).Children.Select(c => c.EntityId));
        Assert.Equal(1, tree.Total.Features);
    }

    [Fact]
    public void Search_by_business_epic_key_keeps_only_that_branch()
    {
        var tree = Build(new PlanningOptions { Search = "PAY-7" });

        Assert.Equal([Resilience], tree.Roots.Select(r => r.EntityId));
        Assert.Equal([Fraud], tree.Roots[0].Children.Select(c => c.EntityId));
    }

    [Fact]
    public void Art_filter_narrows_the_scope_to_the_selected_arts()
    {
        var tree = Build(new PlanningOptions { ArtIds = new HashSet<int> { DataPlatform } });

        Assert.Equal(1, tree.Total.Features);
        Assert.Equal(40m, tree.TotalCapacitySp);
        Assert.Equal([null], tree.Roots.Select(r => r.EntityId));
        Assert.Equal(1, tree.FeaturesInScope);
    }

    [Fact]
    public void Empty_hierarchy_items_are_hidden_unless_asked_for()
    {
        var hidden = Build();
        var shown = Build(new PlanningOptions { IncludeEmptyNodes = true });

        Assert.DoesNotContain(hidden.Roots, r => r.EntityId == EmptyObjective);
        var empty = shown.Roots.Single(r => r.EntityId == EmptyObjective);
        Assert.Equal(0, empty.Metrics.Features);
        Assert.Equal([EmptyEpic], empty.Children.Select(c => c.EntityId));
        Assert.Equal([105], empty.Children[0].Children.Select(c => c.EntityId));
        Assert.Equal(shown.Total, hidden.Total);
    }

    [Fact]
    public void A_feature_without_story_points_below_a_line_counts_as_not_planned()
    {
        var data = Data();
        data = data with { Features = [.. data.Features, Feature(8, 100, InstantPayments, "To Do", null, Link(Argon, 0, included: false, position: 5))] };

        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions { OnlyUnplanned = true });

        var cut = tree.FlattenAll().Single(r => r.Feature?.Id == 8);
        Assert.False(cut.Feature!.IsFullyPlanned);
        Assert.Equal((0m, 1, 1), (cut.Metrics.UnplannedSp, cut.Metrics.TeamLinks, cut.Metrics.UnplannedTeamLinks));
        Assert.DoesNotContain(tree.FlattenAll(), r => r.Feature?.Id == 6);
    }

    [Fact]
    public void Drawdown_not_planned_only_lists_a_feature_under_the_arts_whose_teams_leave_it_unplanned()
    {
        var data = Data();
        data = data with { Features = [.. data.Features, Feature(8, 103, DataLake, "To Do", null, Link(Argon, 5, position: 6), Link(Zeta, 8, included: false, position: 2))] };

        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown, OnlyUnplanned = true });

        var rows = tree.FlattenAll().Where(r => r.Feature?.Id == 8).ToList();
        Assert.Equal(["art:2"], rows.Select(r => r.Id.Split('/')[0]));
        Assert.Equal(8m, rows.Single().Metrics.UnplannedSp);
    }

    [Fact]
    public void Show_empty_with_a_filter_adds_only_items_without_work_not_the_ones_the_filter_hides()
    {
        var tree = Build(new PlanningOptions { IncludeEmptyNodes = true, HideDone = true });

        Assert.Equal([100], Child(tree.Roots.Single(r => r.EntityId == GrowPayments), InstantPayments).Children.Select(c => c.EntityId));
        Assert.Equal([105], Child(tree.Roots.Single(r => r.EntityId == EmptyObjective), EmptyEpic).Children.Select(c => c.EntityId));

        var data = Data();
        data = data with { Features = data.Features.Select(f => f.Id == 7 ? f with { Status = "Done" } : f).ToList() };
        var drawdown = DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown, IncludeEmptyNodes = true, HideDone = true });
        Assert.DoesNotContain(drawdown.Roots[0].Children, c => c.EntityId == Fraud);
        Assert.Contains(drawdown.Roots[0].Children, c => c.EntityId == InstantPayments);
    }

    [Fact]
    public void Sorting_by_demand_descending_reorders_siblings_only()
    {
        var tree = Build(new PlanningOptions { SortBy = PlanningSort.Demand, SortDescending = true });

        Assert.Equal([GrowPayments, Resilience, null], tree.Roots.Select(r => r.EntityId));
        Assert.Equal([InstantPayments, Tokenisation], tree.Roots[0].Children.Select(c => c.EntityId));
        Assert.Equal([1, 6], Child(Child(tree.Roots[0], InstantPayments), 100).Children.Select(c => c.EntityId));
        var ascending = DepartmentPlanningTree.Build(Data(), new PlanningOptions { SortBy = PlanningSort.Demand });
        Assert.Equal([null, Resilience, GrowPayments], ascending.Roots.Select(r => r.EntityId));
        Assert.Equal([6, 1], Child(Child(ascending.Roots[2], InstantPayments), 100).Children.Select(c => c.EntityId));
    }

    private static DepartmentPlanningData WithEpicEnds(DepartmentPlanningData data, params (int EpicId, DateTime End)[] ends) => data with
    {
        Epics = data.Epics.Select(e => ends.Any(x => x.EpicId == e.Id) ? e with { TargetEnd = ends.Single(x => x.EpicId == e.Id).End } : e).ToList(),
    };

    private static DepartmentPlanningData WithoutFeatureEnd(DepartmentPlanningData data, int featureId) => data with
    {
        Features = data.Features.Select(f => f.Id == featureId ? f with { TargetEnd = null } : f).ToList(),
    };

    [Fact]
    public void The_shown_target_end_is_the_latest_feature_end_and_falls_back_to_the_item_s_own()
    {
        var data = WithoutFeatureEnd(WithEpicEnds(Data(), (InstantPayments, new DateTime(2026, 10, 1)), (Fraud, new DateTime(2026, 12, 1))), 7);

        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions());

        Assert.Equal(new DateTime(2026, 11, 7), tree.Find("so:1/pe:10")!.ShownTargetEnd);
        Assert.Equal(new DateTime(2026, 12, 1), tree.Find("so:2/pe:13")!.ShownTargetEnd);
        Assert.Equal(new DateTime(2026, 11, 2), tree.Find("so:1/pe:10/bo:100/f:1")!.ShownTargetEnd);
        Assert.Equal(new DateTime(2026, 11, 7), tree.Find("so:1")!.ShownTargetEnd);
    }

    [Fact]
    public void Sorting_by_target_end_follows_the_date_the_column_shows()
    {
        var data = WithEpicEnds(Data(), (InstantPayments, new DateTime(2026, 10, 1)), (Tokenisation, new DateTime(2026, 12, 31)));

        var ascending = DepartmentPlanningTree.Build(data, new PlanningOptions { SortBy = PlanningSort.TargetEnd });
        var descending = DepartmentPlanningTree.Build(data, new PlanningOptions { SortBy = PlanningSort.TargetEnd, SortDescending = true });

        Assert.Equal([Tokenisation, InstantPayments], ascending.Roots.Single(r => r.EntityId == GrowPayments).Children.Select(c => c.EntityId));
        Assert.Equal([InstantPayments, Tokenisation], descending.Roots.Single(r => r.EntityId == GrowPayments).Children.Select(c => c.EntityId));
    }

    [Fact]
    public void Rows_without_a_target_end_sort_last_both_ways()
    {
        var data = WithoutFeatureEnd(Data(), 2);

        foreach (var descending in new[] { false, true })
        {
            var tree = DepartmentPlanningTree.Build(data, new PlanningOptions { SortBy = PlanningSort.TargetEnd, SortDescending = descending });
            var epic = Child(tree.Roots.Single(r => r.EntityId == GrowPayments), InstantPayments);

            Assert.Equal([100, 101], epic.Children.Select(c => c.EntityId));
        }
    }

    [Fact]
    public void Sorting_rows_by_share_or_target_end_puts_blanks_last_and_follows_the_direction()
    {
        static PlanningRow Row(string id, DateTime? featuresEnd = null, DateTime? ownEnd = null, double? share = null, decimal planned = 0m) => new()
        {
            Id = id,
            JiraId = id,
            Title = id,
            Level = PlanningLevel.PortfolioEpic,
            TargetEnd = ownEnd,
            ShareOfCapacity = share,
            Metrics = PlanningMetrics.Empty with { TargetEnd = featuresEnd, PlannedSp = planned },
        };
        var dated = new[] { Row("A", new DateTime(2026, 11, 9), new DateTime(2026, 10, 1)), Row("B", ownEnd: new DateTime(2026, 11, 5)), Row("C") };
        var shared = new[] { Row("X", share: 0.2, planned: 50m), Row("Y", share: 0.6, planned: 20m), Row("Z", planned: 90m) };

        Assert.Equal(["B", "A", "C"], DepartmentPlanningTree.SortRows(dated, PlanningSort.TargetEnd, false).Select(r => r.Id));
        Assert.Equal(["A", "B", "C"], DepartmentPlanningTree.SortRows(dated, PlanningSort.TargetEnd, true).Select(r => r.Id));
        Assert.Equal(["Y", "X", "Z"], DepartmentPlanningTree.SortRows(shared, PlanningSort.Share, true).Select(r => r.Id));
        Assert.Equal(["X", "Y", "Z"], DepartmentPlanningTree.SortRows(shared, PlanningSort.Share, false).Select(r => r.Id));
        Assert.Equal(["Z", "X", "Y"], DepartmentPlanningTree.SortRows(shared, PlanningSort.Planned, true).Select(r => r.Id));
    }

    [Fact]
    public void Sorting_rows_by_rank_puts_art_ranked_rows_first_and_buckets_last_both_ways()
    {
        static PlanningRow Row(string id, int? rank, bool artRank = true, bool bucket = false) => new()
        {
            Id = id,
            JiraId = id,
            Title = id,
            Level = PlanningLevel.PortfolioEpic,
            EntityId = bucket ? null : 1,
            SortRank = rank,
            HasArtRank = artRank && rank is not null,
        };
        var rows = new[] { Row("B", 2), Row("Z", 1, bucket: true), Row("N", null), Row("P", 1, artRank: false), Row("A", 1) };

        Assert.Equal(["A", "B", "P", "N", "Z"], DepartmentPlanningTree.SortRows(rows, PlanningSort.Priority, false).Select(r => r.Id));
        Assert.Equal(["B", "A", "P", "N", "Z"], DepartmentPlanningTree.SortRows(rows, PlanningSort.Priority, true).Select(r => r.Id));
    }

    [Fact]
    public void Objectives_and_epics_with_an_art_rank_come_before_unranked_ones()
    {
        var data = Data();
        PlanningTree LinkDataLakeTo(int objectiveId) => DepartmentPlanningTree.Build(
            data with { Epics = data.Epics.Select(e => e.Id == DataLake ? e with { StrategicObjectiveIds = [objectiveId] } : e).ToList() },
            new PlanningOptions());

        var underEmpty = LinkDataLakeTo(EmptyObjective);
        var underResilience = LinkDataLakeTo(Resilience);

        Assert.Equal([GrowPayments, Resilience, EmptyObjective, null], underEmpty.Roots.Select(r => r.EntityId));
        var empty = underEmpty.Roots.Single(r => r.EntityId == EmptyObjective);
        Assert.False(empty.HasArtRank);
        Assert.Equal(1, empty.SortRank);
        var resilience = underResilience.Roots.Single(r => r.EntityId == Resilience);
        Assert.Equal([Fraud, Tokenisation, DataLake], resilience.Children.Select(c => c.EntityId));
        Assert.True(resilience.HasArtRank);
        Assert.Equal(2, resilience.SortRank);
    }

    [Fact]
    public void Sorting_by_title_puts_buckets_last_only_for_priority()
    {
        var byTitle = Build(new PlanningOptions { SortBy = PlanningSort.Title });

        Assert.Equal(["Grow payments", DepartmentPlanningTree.NoObjectiveTitle, "Resilience"], byTitle.Roots.Select(r => r.Title));
    }

    [Fact]
    public void Flatten_follows_the_expanded_rows_and_find_returns_rows_by_id()
    {
        var tree = Build();
        var expanded = new HashSet<string> { tree.Roots[0].Id };

        var visible = tree.Flatten(r => expanded.Contains(r.Id)).Select(r => r.Id).ToList();

        Assert.Equal([tree.Roots[0].Id, "so:1/pe:10", "so:1/pe:11", tree.Roots[1].Id, tree.Roots[2].Id], visible);
        Assert.Equal("Feature 3", tree.Find("so:2/pe:11/bo:102/f:3")!.Title);
        Assert.Equal(["so:2", "so:2/pe:11", "so:2/pe:11/bo:102", "so:2/pe:11/bo:102/f:3"], tree.PathTo("so:2/pe:11/bo:102/f:3").Select(r => r.Id));
    }

    [Fact]
    public void Status_classes_follow_the_planning_statuses()
    {
        Assert.Equal(PlanningStatusClass.Done, PlanningStatus.Classify("Closed"));
        Assert.Equal(PlanningStatusClass.Done, PlanningStatus.Classify("Accepted for Release"));
        Assert.Equal(PlanningStatusClass.Done, PlanningStatus.Classify("Rejected"));
        Assert.Equal(PlanningStatusClass.Blocked, PlanningStatus.Classify("blocked"));
        Assert.Equal(PlanningStatusClass.NotStarted, PlanningStatus.Classify("Funnel"));
        Assert.Equal(PlanningStatusClass.NotStarted, PlanningStatus.Classify(null));
        Assert.Equal(PlanningStatusClass.InProgress, PlanningStatus.Classify("In Development"));
        Assert.Equal(PlanningStatusClass.InProgress, PlanningStatus.Classify("In UAT"));
    }

    [Fact]
    public void Art_abbreviations_use_initials_and_stay_unique()
    {
        var abbreviations = ArtAbbreviations.For(
        [
            (1, "Core Payments"),
            (2, "Risk Analytics"),
            (3, "Risk Legacy Migration"),
            (4, "Retail Apps"),
            (5, "Treasury"),
        ]);

        Assert.Equal("CP", abbreviations[1]);
        Assert.Equal("RA", abbreviations[4]);
        Assert.Equal("RA2", abbreviations[2]);
        Assert.Equal("RLM", abbreviations[3]);
        Assert.Equal("TRE", abbreviations[5]);
    }
}
