using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.DepartmentPlanning.Views;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.DepartmentPlanning.Views;

public class TreemapLayoutTests
{
    private const int CorePayments = 1;
    private const int DataPlatform = 2;
    private const int Argon = 1;
    private const int Boron = 2;
    private const int Zeta = 3;
    private const int GrowPayments = 1;
    private const int Resilience = 2;
    private const int InstantPayments = 10;
    private const int Tokenisation = 11;
    private const int DataLake = 12;
    private const int Fraud = 13;

    private static readonly TreemapRect Canvas = new(0d, 0d, TreemapLayout.Width, TreemapLayout.Height);
    private static readonly ArtLineRef CorePaymentsLine = new(CorePayments, "Core Payments");

    private static PlanningFeatureTeam Link(int teamId, int sp, bool included = true, bool belowArtLine = false, int position = 1) =>
        new(teamId, sp, included, belowArtLine ? [CorePaymentsLine] : [], position, null, 0m);

    private static PlanningFeature Feature(int id, int? outcomeId, int? epicId, string status, string? rag, params PlanningFeatureTeam[] teams) =>
        new(id, $"PAY-{id}", $"Feature {id}", status, rag, null, id, 3, "PI 26.09", outcomeId, epicId, null, new DateTime(2026, 11, 1).AddDays(id), null, teams);

    private static DepartmentPlanningData Data(decimal argonCapacity = 100m)
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
            new(Argon, "Argon", [CorePayments], 1m, (double)argonCapacity, argonCapacity, 5, "Sprints"),
            new(Boron, "Boron", [CorePayments], 1m, 50d, 50m, 3, "Sprints"),
            new(Zeta, "Zeta", [DataPlatform], 1m, 40d, 40m, 2, "PI"),
        };
        var features = new List<PlanningFeature>
        {
            Feature(1, 100, InstantPayments, "In Progress", "Green", Link(Argon, 30), Link(Boron, 20)),
            Feature(2, 101, InstantPayments, "Closed", null, Link(Argon, 40, position: 2)),
            Feature(3, 102, Tokenisation, "In Progress", "Red", Link(Argon, 50, belowArtLine: true, position: 4)),
            Feature(4, null, null, "Blocked", null, Link(Boron, 25, included: false, position: 3)),
            Feature(5, 103, DataLake, "To Do", "Amber", Link(Zeta, 30)),
            Feature(6, 100, InstantPayments, "To Do", null, Link(Argon, 45, position: 3)),
            Feature(7, 104, Fraud, "In Development", null, Link(Boron, 30, position: 2)),
        };
        var objectives = new List<PlanningObjective>
        {
            new(GrowPayments, "STRAT-1", "Grow payments", "In Progress", null, [CorePayments]),
            new(Resilience, "STRAT-2", "Resilience", "In Progress", null, [CorePayments]),
        };
        var epics = new List<PlanningEpic>
        {
            new(InstantPayments, "EPIC-10", "Instant payments", "In Progress", "Green", null, null, null, [GrowPayments]),
            new(Tokenisation, "EPIC-11", "Tokenisation", "In Progress", null, "Unfunded", null, null, [GrowPayments, Resilience]),
            new(DataLake, "EPIC-12", "Data lake", "Funnel", null, null, null, null, []),
            new(Fraud, "EPIC-13", "Fraud", "Analysis", null, null, null, null, [Resilience]),
        };
        var outcomes = new List<PlanningOutcome>
        {
            new(100, "BO-100", "Rails", "In Progress", "Amber", "Vendor late", InstantPayments, null, null),
            new(101, "BO-101", "Notifications", "Done", null, null, InstantPayments, null, null),
            new(102, "BO-102", "Token vault", "In Progress", "Red", null, Tokenisation, null, null),
            new(103, "BO-103", "Ingestion", "To Do", null, null, DataLake, null, null),
            new(104, "BO-104", "Scoring", "In Progress", null, null, Fraud, null, null),
        };

        return new DepartmentPlanningData(scope, 3, "PI 26.09", false, null, null, arts, teams, features, objectives, epics, outcomes);
    }

    private static PlanningTree Tree(decimal argonCapacity = 100m) => DepartmentPlanningTree.Build(Data(argonCapacity), new PlanningOptions());

    private static PlanningRow Row(string id, PlanningLevel level, decimal demand, decimal planned, params PlanningRow[] children) => new()
    {
        Id = id,
        Level = level,
        EntityId = 1,
        JiraId = id.ToUpperInvariant(),
        Title = $"Row {id}",
        Children = children,
        Metrics = PlanningMetrics.Empty with { Features = 1, DemandSp = demand, PlannedSp = planned, UnplannedSp = demand - planned },
    };

    private static PlanningTree TreeOf(decimal capacity, params PlanningRow[] roots)
    {
        var demand = roots.Sum(r => r.Metrics.DemandSp);
        var planned = roots.Sum(r => r.Metrics.PlannedSp);
        var total = PlanningMetrics.Empty with { Features = roots.Length, DemandSp = demand, PlannedSp = planned, UnplannedSp = demand - planned };
        return new PlanningTree(PlanningLens.Objectives, roots, total, capacity, (double)capacity, [], [], roots.Length, roots.Length);
    }

    private static IEnumerable<TreemapTile> Rows(TreemapLayoutResult layout) => layout.Tiles.Where(t => t.Kind == TreemapTileKind.Row);

    private static void AssertInside(TreemapRect rect, TreemapRect bounds)
    {
        Assert.True(rect.X >= bounds.X - 1e-6 && rect.Y >= bounds.Y - 1e-6, $"{rect} starts outside {bounds}");
        Assert.True(rect.Right <= bounds.Right + 1e-6 && rect.Bottom <= bounds.Bottom + 1e-6, $"{rect} ends outside {bounds}");
    }

    private static bool Overlap(TreemapRect a, TreemapRect b) =>
        a.X < b.Right - 1e-6 && b.X < a.Right - 1e-6 && a.Y < b.Bottom - 1e-6 && b.Y < a.Bottom - 1e-6;

    [Fact]
    public void Squarify_fills_the_bounds_with_areas_proportional_to_the_values()
    {
        double[] values = [6d, 6d, 4d, 3d, 2d, 2d, 1d];
        var bounds = new TreemapRect(10d, 20d, 600d, 400d);

        var rects = TreemapLayout.Squarify(values, bounds);

        Assert.Equal(values.Length, rects.Count);
        Assert.Equal(bounds.Area, rects.Sum(r => r.Area), 6);
        for (var i = 0; i < values.Length; i++)
        {
            AssertInside(rects[i], bounds);
            Assert.Equal(values[i] / values.Sum(), rects[i].Area / bounds.Area, 9);
        }

        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.False(Overlap(rects[i], rects[j]), $"{rects[i]} overlaps {rects[j]}");
            }
        }
    }

    [Fact]
    public void Squarify_keeps_the_input_order_and_gives_zero_values_no_area()
    {
        var rects = TreemapLayout.Squarify([1d, 0d, 3d, double.NaN], Canvas);

        Assert.Equal(0d, rects[1].Area);
        Assert.Equal(0d, rects[3].Area);
        Assert.Equal(0.25d, rects[0].Area / Canvas.Area, 9);
        Assert.Equal(0.75d, rects[2].Area / Canvas.Area, 9);
        Assert.Equal(Canvas, TreemapLayout.Squarify([5d], Canvas)[0]);
        Assert.Empty(TreemapLayout.Squarify([], Canvas));
        Assert.All(TreemapLayout.Squarify([1d, 2d], TreemapRect.Empty), r => Assert.True(r.IsEmpty));
    }

    [Fact]
    public void Squarify_prefers_square_tiles_over_slivers()
    {
        var values = Enumerable.Repeat(1d, 16).ToArray();

        var rects = TreemapLayout.Squarify(values, new TreemapRect(0d, 0d, 400d, 400d));

        Assert.All(rects, r => Assert.InRange(Math.Max(r.Width / r.Height, r.Height / r.Width), 1d, 1.5d));
    }

    [Fact]
    public void Demand_mode_draws_each_objective_and_hatches_the_not_planned_part_of_a_leaf()
    {
        var tree = Tree();

        var layout = TreemapLayout.Build(tree, TreemapArea.Demand, 1, null);

        Assert.Equal(tree.Roots.Select(r => r.Id).Order(), Rows(layout).Select(t => t.RowId).Order());
        Assert.DoesNotContain(layout.Tiles, t => t.Kind == TreemapTileKind.Unused);
        Assert.Equal(0, layout.HiddenRows);
        Assert.All(layout.Tiles, t => AssertInside(t.Rect, Canvas));

        var grow = layout.Tiles.Single(t => t.RowId == "so:1");
        var resilience = layout.Tiles.Single(t => t.RowId == "so:2");
        Assert.InRange(grow.Rect.Area / resilience.Rect.Area, 2.2d, 2.45d);
        Assert.Equal(grow.Height * 50d / 185d, grow.HatchedHeight, 6);
        Assert.Equal(185m, grow.Value);
        Assert.False(grow.HasChildrenDrawn);
    }

    [Fact]
    public void Planned_mode_sizes_by_planned_points_and_never_hatches()
    {
        var layout = TreemapLayout.Build(Tree(), TreemapArea.Planned, 3, null);

        Assert.All(layout.Tiles, t => Assert.Equal(0d, t.HatchedHeight));
        var grow = layout.Tiles.Single(t => t.RowId == "so:1");
        Assert.Equal(135m, grow.Value);
        Assert.Equal(3, layout.HiddenRows);
        Assert.DoesNotContain(layout.Tiles, t => t.RowId.StartsWith("so:1/pe:11", StringComparison.Ordinal));
    }

    [Fact]
    public void Depth_adds_one_nested_level_per_step_and_repeats_a_shared_epic_under_each_objective()
    {
        var tree = Tree();

        var one = TreemapLayout.Build(tree, TreemapArea.Demand, 1, null);
        var two = TreemapLayout.Build(tree, TreemapArea.Demand, 2, null);
        var three = TreemapLayout.Build(tree, TreemapArea.Demand, 3, null);

        Assert.Equal([PlanningLevel.StrategicObjective], Rows(one).Select(t => t.Level).Distinct());
        Assert.Equal([PlanningLevel.StrategicObjective, PlanningLevel.PortfolioEpic], Rows(two).Select(t => t.Level).Distinct().Order());
        Assert.Equal([PlanningLevel.StrategicObjective, PlanningLevel.PortfolioEpic, PlanningLevel.BusinessOutcome], Rows(three).Select(t => t.Level).Distinct().Order());

        Assert.Contains(two.Tiles, t => t.RowId == "so:1/pe:11");
        Assert.Contains(two.Tiles, t => t.RowId == "so:2/pe:11");
        Assert.All(Rows(two).Where(t => t.Level == PlanningLevel.StrategicObjective), t => Assert.True(t.HasChildrenDrawn));
        Assert.All(Rows(two).Where(t => t.Level == PlanningLevel.PortfolioEpic), t =>
        {
            var parent = two.Tiles.Single(p => p.RowId == t.ParentId);
            Assert.Equal(1, t.Depth);
            AssertInside(t.Rect, parent.Rect);
            Assert.True(t.Y >= parent.Y + parent.HeaderHeight - 1e-6);
        });

        var siblings = Rows(two).Where(t => t.ParentId == "so:1").ToList();
        Assert.Equal(2, siblings.Count);
        Assert.InRange(siblings.Single(t => t.RowId == "so:1/pe:10").Rect.Area / siblings.Single(t => t.RowId == "so:1/pe:11").Rect.Area, 2.5d, 2.9d);
        Assert.False(Overlap(siblings[0].Rect, siblings[1].Rect));
    }

    [Fact]
    public void Zooming_into_a_row_lays_out_its_children_on_the_whole_canvas()
    {
        var tree = Tree(1000m);

        var layout = TreemapLayout.Build(tree, TreemapArea.Demand, 2, "so:1/pe:11/bo:102");

        Assert.Equal("so:1/pe:11/bo:102", layout.Root!.Id);
        Assert.Equal(["so:1", "so:1/pe:11", "so:1/pe:11/bo:102"], layout.Path.Select(r => r.Id));
        Assert.DoesNotContain(layout.Tiles, t => t.Kind == TreemapTileKind.Unused);
        var feature = Assert.Single(layout.Tiles);
        Assert.Equal("so:1/pe:11/bo:102/f:3", feature.RowId);
        Assert.Equal(PlanningLevel.BusinessEpic, feature.Level);
        Assert.Equal(feature.Height, feature.HatchedHeight, 6);
        Assert.InRange(feature.Width, TreemapLayout.Width - 10d, TreemapLayout.Width);
        Assert.Equal(TreemapLabelMode.Full, feature.LabelMode);
    }

    [Fact]
    public void An_unknown_zoom_target_falls_back_to_the_root_level()
    {
        var layout = TreemapLayout.Build(Tree(), TreemapArea.Demand, 1, "so:999");

        Assert.Null(layout.Root);
        Assert.Empty(layout.Path);
        Assert.Equal(3, Rows(layout).Count());
    }

    [Fact]
    public void Unused_capacity_is_one_hollow_tile_sized_by_capacity_minus_planned()
    {
        var tree = Tree(1000m);

        var layout = TreemapLayout.Build(tree, TreemapArea.Planned, 1, null);

        var unused = Assert.Single(layout.Tiles, t => t.Kind == TreemapTileKind.Unused);
        Assert.Equal(1090m - 195m, unused.Value);
        Assert.Equal(895m, layout.UnusedCapacitySp);
        Assert.True(layout.HasUnusedTile);
        Assert.Equal("unused", unused.RowId);
        var objectives = Rows(layout).Sum(t => t.Rect.Area);
        Assert.InRange(unused.Rect.Area / Canvas.Area, 0.79d, 0.83d);
        Assert.InRange(objectives / Canvas.Area, 0.16d, 0.19d);
        Assert.All(Rows(layout), t => Assert.False(Overlap(t.Rect, unused.Rect)));
    }

    [Fact]
    public void Unused_capacity_too_thin_to_draw_gets_no_tile_and_the_layout_says_no_unused_tile_is_shown()
    {
        var layout = TreemapLayout.Build(TreeOf(1000m, Row("a", PlanningLevel.StrategicObjective, 1000m, 994m)), TreemapArea.Planned, 1, null);

        Assert.Equal(6m, layout.UnusedCapacitySp);
        Assert.DoesNotContain(layout.Tiles, t => t.Kind == TreemapTileKind.Unused);
        Assert.False(layout.HasUnusedTile);
        var objective = Assert.Single(layout.Tiles);
        Assert.InRange(objective.Width, TreemapLayout.Width - 10d, TreemapLayout.Width);
    }

    [Fact]
    public void Objectives_keep_a_visible_strip_next_to_a_huge_unused_capacity()
    {
        var tree = TreeOf(3000m, Row("a", PlanningLevel.StrategicObjective, 10m, 1m), Row("b", PlanningLevel.StrategicObjective, 10m, 1m));

        var layout = TreemapLayout.Build(tree, TreemapArea.Planned, 1, null);

        var unused = Assert.Single(layout.Tiles, t => t.Kind == TreemapTileKind.Unused);
        Assert.True(layout.HasUnusedTile);
        Assert.Equal(["a", "b"], Rows(layout).Select(t => t.RowId).Order());
        Assert.All(Rows(layout), t =>
        {
            Assert.True(Math.Min(t.Width, t.Height) >= 30d, $"{t.RowId} {t.Rect} is too thin to see");
            Assert.False(Overlap(t.Rect, unused.Rect));
        });
        Assert.Equal(0, layout.HiddenRows);
    }

    [Fact]
    public void The_caller_can_leave_out_unused_capacity_while_filters_hide_planned_work()
    {
        var layout = TreemapLayout.Build(Tree(1000m), TreemapArea.Planned, 1, null, showUnused: false);

        Assert.DoesNotContain(layout.Tiles, t => t.Kind == TreemapTileKind.Unused);
        Assert.False(layout.HasUnusedTile);
        Assert.Equal(0m, layout.UnusedCapacitySp);
        Assert.InRange(Rows(layout).Sum(t => t.Rect.Area) / Canvas.Area, 0.9d, 1d);
    }

    [Fact]
    public void Objective_tiles_and_unused_capacity_share_one_scale_when_an_epic_is_under_two_objectives()
    {
        var tree = Tree(1000m);

        var layout = TreemapLayout.Build(tree, TreemapArea.Demand, 1, null);

        var unused = Assert.Single(layout.Tiles, t => t.Kind == TreemapTileKind.Unused);
        var drawn = tree.Roots.Sum(r => r.Metrics.DemandSp);
        Assert.True(drawn > tree.Total.DemandSp);
        var objectivesArea = Rows(layout).Sum(t => t.Rect.Area);
        var perSpObjectives = objectivesArea / (double)drawn;
        var perSpUnused = unused.Rect.Area / (double)unused.Value;
        Assert.InRange(perSpObjectives / perSpUnused, 0.85d, 1.05d);
    }

    [Fact]
    public void No_unused_tile_without_capacity_or_when_planned_exceeds_capacity()
    {
        var withoutCapacity = TreemapLayout.Build(TreeOf(0m, Row("a", PlanningLevel.StrategicObjective, 10m, 0m)), TreemapArea.Planned, 1, null);
        var over = TreemapLayout.Build(Tree(), TreemapArea.Planned, 1, null);

        Assert.DoesNotContain(withoutCapacity.Tiles, t => t.Kind == TreemapTileKind.Unused);
        Assert.Empty(withoutCapacity.Tiles);
        Assert.Equal(1, withoutCapacity.HiddenRows);
        Assert.DoesNotContain(over.Tiles, t => t.Kind == TreemapTileKind.Unused);
        Assert.Equal(0m, over.UnusedCapacitySp);
    }

    [Fact]
    public void Tiny_siblings_collapse_into_one_more_tile_per_parent()
    {
        var small = Enumerable.Range(1, 30).Select(i => Row($"s{i}", PlanningLevel.PortfolioEpic, 0.05m, 0.025m)).ToArray();
        var parent = Row("p", PlanningLevel.StrategicObjective, 1001.5m, 1000.75m, [Row("big", PlanningLevel.PortfolioEpic, 1000m, 1000m), .. small]);

        var layout = TreemapLayout.Build(TreeOf(0m, parent), TreemapArea.Demand, 2, null);

        var more = Assert.Single(layout.Tiles, t => t.Kind == TreemapTileKind.More);
        Assert.Equal(30, more.MoreCount);
        Assert.Equal(1.5m, more.Value);
        Assert.Equal("p/more", more.RowId);
        Assert.Equal("p", more.ParentId);
        Assert.Equal(PlanningLevel.PortfolioEpic, more.Level);
        Assert.Equal(more.Height / 2d, more.HatchedHeight, 6);
        Assert.True(Math.Min(more.Width, more.Height) >= 30d, $"the more tile {more.Rect} is too thin to see");
        Assert.Equal(["big", "p"], Rows(layout).Select(t => t.RowId).Order());
        Assert.Equal(0, layout.HiddenRows);
        var big = layout.Tiles.Single(t => t.RowId == "big");
        Assert.False(Overlap(big.Rect, more.Rect));
    }

    [Fact]
    public void A_single_small_sibling_becomes_a_visible_plus_one_more_tile()
    {
        var parent = Row("p", PlanningLevel.StrategicObjective, 1000.05m, 1000.05m, Row("big", PlanningLevel.PortfolioEpic, 1000m, 1000m), Row("tiny", PlanningLevel.PortfolioEpic, 0.05m, 0.05m));

        var layout = TreemapLayout.Build(TreeOf(0m, parent), TreemapArea.Demand, 2, null);

        var more = Assert.Single(layout.Tiles, t => t.Kind == TreemapTileKind.More);
        Assert.Equal(1, more.MoreCount);
        Assert.Equal(0.05m, more.Value);
        Assert.True(Math.Min(more.Width, more.Height) >= 30d, $"the more tile {more.Rect} is too thin to see");
        Assert.DoesNotContain(layout.Tiles, t => t.RowId == "tiny");
        Assert.Equal(0, layout.HiddenRows);
    }

    [Fact]
    public void A_small_but_drawable_sibling_keeps_its_own_tile()
    {
        var parent = Row("p", PlanningLevel.StrategicObjective, 1020m, 1020m, Row("big", PlanningLevel.PortfolioEpic, 1000m, 1000m), Row("small", PlanningLevel.PortfolioEpic, 20m, 20m));

        var layout = TreemapLayout.Build(TreeOf(0m, parent), TreemapArea.Demand, 2, null);

        Assert.DoesNotContain(layout.Tiles, t => t.Kind == TreemapTileKind.More);
        var small = Assert.Single(layout.Tiles, t => t.RowId == "small");
        Assert.Equal(TreemapLabelMode.None, small.LabelMode);
        Assert.True(Math.Min(small.Width, small.Height) >= 20d, $"the small tile {small.Rect} is too thin to see");
    }

    [Fact]
    public void A_sibling_too_thin_to_draw_becomes_a_plus_one_more_tile_instead_of_a_sliver()
    {
        var parent = Row("p", PlanningLevel.StrategicObjective, 1003m, 1003m, Row("big", PlanningLevel.PortfolioEpic, 1000m, 1000m), Row("tiny", PlanningLevel.PortfolioEpic, 3m, 3m));

        var layout = TreemapLayout.Build(TreeOf(0m, parent), TreemapArea.Demand, 2, null);

        Assert.DoesNotContain(layout.Tiles, t => t.RowId == "tiny");
        var more = Assert.Single(layout.Tiles, t => t.Kind == TreemapTileKind.More);
        Assert.Equal(1, more.MoreCount);
        Assert.Equal(3m, more.Value);
        Assert.Equal("p", more.ParentId);
        Assert.True(Math.Min(more.Width, more.Height) >= 30d, $"the more tile {more.Rect} is too thin to see");
        Assert.False(Overlap(more.Rect, layout.Tiles.Single(t => t.RowId == "big").Rect));
        Assert.Equal(0, layout.HiddenRows);
    }

    [Fact]
    public void A_parent_whose_children_would_only_fit_as_one_more_tile_is_drawn_as_a_single_tile()
    {
        var roots = new[]
        {
            Row("a", PlanningLevel.StrategicObjective, 639.7m, 639.7m, Row("a1", PlanningLevel.PortfolioEpic, 639.7m, 639.7m)),
            Row("b", PlanningLevel.StrategicObjective, 18.8m, 18.8m, Row("b1", PlanningLevel.PortfolioEpic, 3.4m, 3.4m), Row("b2", PlanningLevel.PortfolioEpic, 15.4m, 15.4m)),
            Row("c", PlanningLevel.StrategicObjective, 627.4m, 627.4m, Row("c1", PlanningLevel.PortfolioEpic, 133.4m, 133.4m), Row("c2", PlanningLevel.PortfolioEpic, 494m, 494m)),
            Row("d", PlanningLevel.StrategicObjective, 3.9m, 3.9m, Row("d1", PlanningLevel.PortfolioEpic, 0.9m, 0.9m), Row("d2", PlanningLevel.PortfolioEpic, 3m, 3m)),
            Row("e", PlanningLevel.StrategicObjective, 0.3m, 0.3m, Row("e1", PlanningLevel.PortfolioEpic, 0.3m, 0.3m)),
        };

        var layout = TreemapLayout.Build(TreeOf(0m, roots), TreemapArea.Demand, 2, null);

        var d = layout.Tiles.Single(t => t.RowId == "d");
        Assert.False(d.HasChildrenDrawn);
        Assert.Equal(3.9m, d.Value);
        Assert.DoesNotContain(layout.Tiles, t => t.RowId is "d1" or "d2" || t.ParentId == "d");
        Assert.True(layout.Tiles.Single(t => t.RowId == "c").HasChildrenDrawn);
        Assert.All(layout.Tiles, t => Assert.True(Math.Min(t.Width, t.Height) >= 6d, $"{t.RowId} is a sliver: {t.Rect}"));
        Assert.Equal(0, layout.HiddenRows);
    }

    [Fact]
    public void Zooming_into_an_outcome_with_a_400_and_a_1_point_feature_shows_the_small_one_as_plus_one_more()
    {
        var outcome = Row("bo", PlanningLevel.BusinessOutcome, 401m, 401m, Row("f400", PlanningLevel.BusinessEpic, 400m, 400m), Row("f1", PlanningLevel.BusinessEpic, 1m, 1m));
        var objective = Row("so", PlanningLevel.StrategicObjective, 401m, 401m, Row("pe", PlanningLevel.PortfolioEpic, 401m, 401m, outcome));

        var layout = TreemapLayout.Build(TreeOf(0m, objective), TreemapArea.Demand, 1, "bo");

        Assert.Equal("bo", layout.Root!.Id);
        Assert.Equal(["f400"], Rows(layout).Select(t => t.RowId));
        var more = Assert.Single(layout.Tiles, t => t.Kind == TreemapTileKind.More);
        Assert.Equal(1, more.MoreCount);
        Assert.Equal(1m, more.Value);
        Assert.Equal("bo/more", more.RowId);
        Assert.True(Math.Min(more.Width, more.Height) >= 30d, $"the more tile {more.Rect} is too thin to see");
        Assert.Equal(0, layout.HiddenRows);
    }

    [Fact]
    public void Every_item_with_story_points_is_drawn_or_counted_in_a_more_tile_and_no_tile_is_a_sliver()
    {
        PlanningRow Parent(string id, params decimal[] children)
        {
            var rows = children.Select((sp, i) => Row($"{id}/c{i}", PlanningLevel.PortfolioEpic, sp, sp)).ToArray();
            var sum = children.Sum();
            return Row(id, PlanningLevel.StrategicObjective, sum, sum, rows);
        }

        var roots = new[]
        {
            Parent("a", 300m, 1m),
            Parent("b", 300m, 2m),
            Parent("c", 1000m, 3m, 2m, 1m, 0.5m),
            Parent("d", 120m, 60m, 1m, 1m),
            Parent("e", 40m, 0.4m),
            Parent("f", 9m, 0.2m, 0.1m),
            Row("g", PlanningLevel.StrategicObjective, 1m, 1m),
            Row("h", PlanningLevel.StrategicObjective, 0.3m, 0.3m),
        };

        foreach (var depth in new[] { 1, 2 })
        {
            var layout = TreemapLayout.Build(TreeOf(0m, roots), TreemapArea.Demand, depth, null);

            Assert.Equal(0, layout.HiddenRows);
            Assert.All(layout.Tiles, t => Assert.True(Math.Min(t.Width, t.Height) >= 6d, $"{t.RowId} is a sliver: {t.Rect}"));
            AssertAccounted(layout, null, roots);
            foreach (var parent in Rows(layout).Where(t => t.HasChildrenDrawn))
            {
                AssertAccounted(layout, parent.RowId, roots.Single(r => r.Id == parent.RowId).Children);
            }
        }

        static void AssertAccounted(TreemapLayoutResult layout, string? parentId, IReadOnlyList<PlanningRow> rows)
        {
            var drawn = layout.Tiles.Where(t => t.ParentId == parentId && t.Kind != TreemapTileKind.Unused).ToList();
            var counted = drawn.Count(t => t.Kind == TreemapTileKind.Row) + drawn.Where(t => t.Kind == TreemapTileKind.More).Sum(t => t.MoreCount);
            Assert.Equal(rows.Count, counted);
            Assert.Equal(rows.Sum(r => r.Metrics.DemandSp), drawn.Sum(t => t.Value));
        }
    }

    [Fact]
    public void A_more_tile_only_holds_items_no_bigger_than_every_drawn_sibling()
    {
        var random = new Random(20261026);
        for (var run = 0; run < 1500; run++)
        {
            var objectives = Enumerable.Range(0, random.Next(1, 13)).Select(o =>
            {
                var sizes = Enumerable.Range(0, random.Next(2, 30))
                    .Select(_ => Math.Round((decimal)((Math.Pow(random.NextDouble(), 4d) * 1000d) + 0.2d), 1))
                    .ToArray();
                var children = sizes.Select((sp, i) => Row($"o{o}/c{i}", PlanningLevel.PortfolioEpic, sp, sp)).ToArray();
                return Row($"o{o}", PlanningLevel.StrategicObjective, sizes.Sum(), sizes.Sum(), children);
            }).ToArray();

            var layout = TreemapLayout.Build(TreeOf(0m, objectives), TreemapArea.Demand, 2, null);

            foreach (var parent in Rows(layout).Where(t => t.HasChildrenDrawn))
            {
                var children = objectives.Single(r => r.Id == parent.RowId).Children;
                var drawn = Rows(layout).Where(t => t.ParentId == parent.RowId).Select(t => t.RowId).ToHashSet();
                if (drawn.Count == 0 || drawn.Count == children.Count)
                {
                    continue;
                }

                var smallestDrawn = children.Where(c => drawn.Contains(c.Id)).Min(c => c.Metrics.DemandSp);
                var biggestFolded = children.Where(c => !drawn.Contains(c.Id)).Max(c => c.Metrics.DemandSp);
                Assert.True(biggestFolded <= smallestDrawn, $"run {run}, {parent.RowId}: {biggestFolded} SP is in +N more while {smallestDrawn} SP has its own tile");
            }
        }
    }

    [Fact]
    public void Rows_without_area_are_counted_as_hidden_instead_of_drawn()
    {
        var roots = new[]
        {
            Row("a", PlanningLevel.StrategicObjective, 100m, 100m),
            Row("b", PlanningLevel.StrategicObjective, 40m, 0m),
            Row("c", PlanningLevel.StrategicObjective, 0m, 0m),
        };

        var demand = TreemapLayout.Build(TreeOf(0m, roots), TreemapArea.Demand, 1, null);
        var planned = TreemapLayout.Build(TreeOf(0m, roots), TreemapArea.Planned, 1, null);

        Assert.Equal(["a", "b"], Rows(demand).Select(t => t.RowId).Order());
        Assert.Equal(1, demand.HiddenRows);
        Assert.Equal(["a"], Rows(planned).Select(t => t.RowId));
        Assert.Equal(2, planned.HiddenRows);
    }

    [Fact]
    public void Empty_trees_and_line_markers_produce_no_tiles_and_no_exception()
    {
        var empty = TreemapLayout.Build(PlanningTree.Empty, TreemapArea.Demand, 3, null);
        var marker = new PlanningRow { Id = "art:1/line", Kind = PlanningRowKind.LineMarker, Level = PlanningLevel.Art, Title = "line" };
        var withMarker = TreemapLayout.Build(TreeOf(10m, Row("a", PlanningLevel.StrategicObjective, 5m, 5m), marker), TreemapArea.Demand, 2, null);

        Assert.Empty(empty.Tiles);
        Assert.Equal(0, empty.HiddenRows);
        Assert.Equal(["a"], Rows(withMarker).Select(t => t.RowId));
        Assert.Contains(withMarker.Tiles, t => t.Kind == TreemapTileKind.Unused && t.Value == 5m);
    }

    [Fact]
    public void Labels_drop_from_full_to_key_to_nothing_as_tiles_shrink()
    {
        var wide = Row("w", PlanningLevel.StrategicObjective, 1000m, 1000m);
        var slivers = Enumerable.Range(1, 12).Select(i => Row($"n{i}", PlanningLevel.StrategicObjective, 0.05m, 0.05m)).ToArray();

        var layout = TreemapLayout.Build(TreeOf(0m, [wide, .. slivers]), TreemapArea.Demand, 1, null);

        Assert.Equal(TreemapLabelMode.Full, layout.Tiles.Single(t => t.RowId == "w").LabelMode);
        Assert.True(layout.Tiles.Single(t => t.RowId == "w").LabelChars > 40);
        var more = Assert.Single(layout.Tiles, t => t.Kind == TreemapTileKind.More);
        Assert.Equal(12, more.MoreCount);
        Assert.Equal("root/more", more.RowId);

        var keyOnly = TreemapLayout.Build(TreeOf(0m, Row("x1", PlanningLevel.StrategicObjective, 145m, 145m), Row("y", PlanningLevel.StrategicObjective, 5000m, 5000m)), TreemapArea.Demand, 1, null);
        var narrow = keyOnly.Tiles.Single(t => t.RowId == "x1");
        Assert.Equal(TreemapLabelMode.Key, narrow.LabelMode);
        Assert.InRange(narrow.LabelChars, 2, 5);

        var none = TreemapLayout.Build(TreeOf(0m, Row("x", PlanningLevel.StrategicObjective, 40m, 40m), Row("y", PlanningLevel.StrategicObjective, 5000m, 5000m)), TreemapArea.Demand, 1, null);
        var sliver = none.Tiles.Single(t => t.RowId == "x");
        Assert.Equal(TreemapLabelMode.None, sliver.LabelMode);
        Assert.Equal(0, sliver.LabelChars);
    }

    [Fact]
    public void Fit_truncates_with_an_ellipsis_only_when_needed()
    {
        Assert.Equal("abc", TreemapLayout.Fit("abc", 4));
        Assert.Equal("abcd", TreemapLayout.Fit("abcd", 4));
        Assert.Equal("abc…", TreemapLayout.Fit("abcdef", 4));
        Assert.Equal("ab…", TreemapLayout.Fit("ab cdef", 4));
        Assert.Equal("…", TreemapLayout.Fit("abc", 1));
        Assert.Equal("", TreemapLayout.Fit("abc", 0));
        Assert.Equal("", TreemapLayout.Fit(null, 5));
    }

    [Fact]
    public void Hatch_path_covers_the_band_with_diagonal_segments_inside_it()
    {
        var band = new TreemapRect(10d, 20d, 50d, 30d);

        var path = TreemapLayout.HatchPath(band, 10d);

        Assert.StartsWith("M", path);
        var segments = path.Split('M', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(7, segments.Length);
        foreach (var segment in segments)
        {
            var points = segment.Split('L');
            var from = points[0].Split(' ').Select(double.Parse).ToArray();
            var to = points[1].Split(' ').Select(double.Parse).ToArray();
            Assert.InRange(from[0], band.X, band.Right);
            Assert.InRange(to[0], band.X, band.Right);
            Assert.InRange(from[1], band.Y, band.Bottom);
            Assert.InRange(to[1], band.Y, band.Bottom);
            Assert.True(to[0] > from[0]);
            Assert.True(to[1] < from[1]);
        }

        Assert.Equal("", TreemapLayout.HatchPath(new TreemapRect(0d, 0d, 50d, 0d)));
        Assert.Equal("", TreemapLayout.HatchPath(band, 0d));
    }
}
