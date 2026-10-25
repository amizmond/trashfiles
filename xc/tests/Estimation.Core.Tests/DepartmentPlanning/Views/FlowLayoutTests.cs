using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.DepartmentPlanning.Views;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.DepartmentPlanning.Views;

public class FlowLayoutTests
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

    private static readonly string Grow = FlowLayout.RowKey($"so:{GrowPayments}");
    private static readonly string Resil = FlowLayout.RowKey($"so:{Resilience}");
    private static readonly string NoObjective = FlowLayout.RowKey($"so:{DepartmentPlanningTree.None}");
    private static readonly string CoreArt = FlowLayout.ArtKey(CorePayments);
    private static readonly string DataArt = FlowLayout.ArtKey(DataPlatform);

    private static readonly ArtLineRef CorePaymentsLine = new(CorePayments, "Core Payments");

    private static PlanningFeatureTeam Link(int teamId, int sp, bool included = true, bool belowArtLine = false, int position = 1) =>
        new(teamId, sp, included, belowArtLine ? [CorePaymentsLine] : [], position, null, 0m);

    private static PlanningFeature Feature(int id, int? outcomeId, int? epicId, string status, string? rag, params PlanningFeatureTeam[] teams) =>
        new(id, $"PAY-{id}", $"Feature {id}", status, rag, null, id, 3, "PI 26.09", outcomeId, epicId, null, new DateTime(2026, 11, 1).AddDays(id), null, teams);

    private static DepartmentPlanningData Data(decimal capacityFactor = 1m, bool boronInBothArts = false)
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
            new(DataPlatform, "Data Platform", "DP", 1, "Retail", ArtPrioritization.BusinessOutcome, ArtOrderMode.Empty, boronInBothArts ? [Zeta, Boron] : [Zeta], []),
        };
        var teams = new List<PlanningTeam>
        {
            new(Argon, "Argon", [CorePayments], 1m, 100d, 100m * capacityFactor, 5, "Sprints"),
            new(Boron, "Boron", boronInBothArts ? [CorePayments, DataPlatform] : [CorePayments], 1m, 50d, 50m * capacityFactor, 3, "Sprints"),
            new(Zeta, "Zeta", [DataPlatform], 1m, 40d, 40m * capacityFactor, 2, "PI"),
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

    private static PlanningTree Tree(DepartmentPlanningData? data = null, PlanningOptions? options = null) =>
        DepartmentPlanningTree.Build(data ?? Data(), options ?? new PlanningOptions());

    private static FlowLayout Layout(FlowOptions? options = null, DepartmentPlanningData? data = null, PlanningOptions? planning = null) =>
        FlowLayout.Build(Tree(data, planning), options ?? new FlowOptions());

    private static FlowNode Node(FlowLayout layout, string key) => layout.Nodes.Single(n => n.Key == key);

    private static decimal LinkSp(FlowLayout layout, string sourceKey, string targetKey) =>
        layout.Links.Where(l => l.SourceKey == sourceKey && l.TargetKey == targetKey).Sum(l => l.Sp);

    private static IEnumerable<FlowLink> LinksOf(FlowLayout layout, FlowNode node) =>
        layout.Links.Where(l => node.Kind is FlowNodeKind.Row or FlowNodeKind.Other ? l.SourceKey == node.Key : l.TargetKey == node.Key);

    private static void AssertConserved(FlowLayout layout)
    {
        foreach (var node in layout.Nodes)
        {
            var links = LinksOf(layout, node).ToList();
            Assert.Equal(node.Sp, links.Sum(l => l.Sp));
            Assert.Equal(node.Height, links.Sum(l => l.Thickness), 6);
            if (node.Sp > 0m)
            {
                Assert.Equal(1d, links.Sum(l => node.Kind is FlowNodeKind.Row or FlowNodeKind.Other ? l.ShareOfSource : l.ShareOfTarget), 6);
            }
        }

        Assert.Equal(layout.Sources.Sum(n => n.Sp), layout.Targets.Sum(n => n.Sp));
        Assert.Equal(layout.PlannedSp + layout.UnplannedSp, layout.Links.Sum(l => l.Sp));
    }

    [Fact]
    public void Objectives_send_planned_story_points_to_arts_and_unplanned_ones_to_the_outlet()
    {
        var layout = Layout();

        Assert.Equal(PlanningLevel.StrategicObjective, layout.SourceLevel);
        Assert.Equal([Grow, Resil, NoObjective], layout.Sources.Select(n => n.Key));
        Assert.Equal([CoreArt, DataArt, FlowLayout.OutletKey], layout.Targets.Select(n => n.Key));

        Assert.Equal(135m, LinkSp(layout, Grow, CoreArt));
        Assert.Equal(0m, LinkSp(layout, Grow, DataArt));
        Assert.Equal(50m, LinkSp(layout, Grow, FlowLayout.OutletKey));
        Assert.Equal(30m, LinkSp(layout, Resil, CoreArt));
        Assert.Equal(50m, LinkSp(layout, Resil, FlowLayout.OutletKey));
        Assert.Equal(30m, LinkSp(layout, NoObjective, DataArt));
        Assert.Equal(25m, LinkSp(layout, NoObjective, FlowLayout.OutletKey));

        var core = Node(layout, CoreArt);
        Assert.Equal(165m, core.Sp);
        Assert.Equal(150m, core.CapacitySp);
        Assert.True(core.OverCapacity);
        Assert.Equal(-15m, core.FreeSp);
        Assert.True(core.CapacityHeight < core.Height);

        var data = Node(layout, DataArt);
        Assert.Equal(30m, data.Sp);
        Assert.False(data.OverCapacity);
        Assert.Equal(10m, data.FreeSp);
        Assert.True(data.CapacityHeight > data.Height);

        var outlet = Node(layout, FlowLayout.OutletKey);
        Assert.Equal(FlowNodeKind.Outlet, outlet.Kind);
        Assert.Equal(125m, outlet.Sp);
        Assert.Equal(75m, outlet.NetSp);
        Assert.Equal(50m, outlet.SharedSp);
        Assert.Equal(outlet.Height * 75d / 125d, outlet.NetHeight, 6);
        Assert.False(outlet.IsSelectable);
        Assert.True(Node(layout, NoObjective).IsBucket);
    }

    [Fact]
    public void An_art_counts_a_planned_epic_under_several_objectives_once_against_its_capacity()
    {
        var data = Data();
        var features = data.Features.Select(f => f.Id == 3 ? f with { Teams = [Link(Argon, 50, position: 4)] } : f).ToList();
        var layout = Layout(data: data with { Features = features });

        Assert.Equal(185m, LinkSp(layout, Grow, CoreArt));
        Assert.Equal(80m, LinkSp(layout, Resil, CoreArt));
        var core = Node(layout, CoreArt);
        Assert.Equal(265m, core.Sp);
        Assert.Equal(215m, core.NetSp);
        Assert.True(core.HasShared);
        Assert.Equal(50m, core.SharedSp);
        Assert.Equal(-65m, core.FreeSp);
        Assert.True(core.OverCapacity);
        Assert.Equal(core.Height * 215d / 265d, core.NetHeight, 6);
        Assert.False(Node(layout, DataArt).HasShared);
        AssertConserved(layout);
    }

    [Fact]
    public void Capacity_is_left_out_when_the_caller_hides_it()
    {
        var layout = Layout(options: new FlowOptions { ShowCapacity = false });

        var core = Node(layout, CoreArt);
        Assert.Null(core.CapacitySp);
        Assert.Null(core.CapacityHeight);
        Assert.False(core.OverCapacity);
        Assert.Equal(165m, core.NetSp);
        Assert.Equal(0m, layout.CapacitySp);
        Assert.DoesNotContain(layout.Targets, n => n.Kind == FlowNodeKind.Art && n.Sp == 0m);
        AssertConserved(layout);
    }

    [Fact]
    public void Totals_follow_the_grid_except_that_a_shared_epic_counts_under_each_objective()
    {
        var tree = Tree();
        var layout = FlowLayout.Build(tree, new FlowOptions());

        Assert.Equal(tree.Total.PlannedSp, layout.PlannedSp);
        Assert.Equal(tree.TotalCapacitySp, layout.CapacitySp);
        Assert.Equal(tree.Roots.Sum(r => r.Metrics.UnplannedSp), layout.UnplannedSp);
        Assert.Equal(125m, layout.UnplannedSp);
        Assert.Equal(75m, tree.Total.UnplannedSp);
    }

    [Fact]
    public void Each_row_s_ribbons_reconcile_with_its_grid_metrics()
    {
        var layout = Layout();

        foreach (var node in layout.Sources)
        {
            var row = node.Row!;
            var planned = layout.Links.Where(l => l.SourceKey == node.Key && !l.ToOutlet).Sum(l => l.Sp);
            Assert.Equal(row.Metrics.PlannedSp, planned);
            Assert.Equal(row.Metrics.UnplannedSp, LinkSp(layout, node.Key, FlowLayout.OutletKey));
            Assert.Equal(row.Metrics.PlannedSp, node.PlannedSp);
            Assert.Equal(row.Metrics.UnplannedSp, node.UnplannedSp);
            Assert.Equal(row.Id, node.RowId);
        }

        AssertConserved(layout);
    }

    [Fact]
    public void Shares_relate_each_ribbon_to_its_row_and_its_art()
    {
        var layout = Layout();
        var link = layout.Links.Single(l => l.SourceKey == Grow && l.TargetKey == CoreArt);

        Assert.Equal(135d / 185d, link.ShareOfSource, 6);
        Assert.Equal(135d / 165d, link.ShareOfTarget, 6);
        Assert.Equal(0, link.ColorIndex);
        Assert.False(link.ToOutlet);
        Assert.True(layout.Links.Single(l => l.SourceKey == Grow && l.TargetKey == FlowLayout.OutletKey).ToOutlet);
    }

    [Fact]
    public void Nodes_stack_without_overlap_inside_the_canvas()
    {
        var options = new FlowOptions();
        var layout = Layout(options);

        Assert.Equal(options.Width, layout.Width);
        Assert.True(layout.Height >= options.MinHeight + 2 * options.Padding);
        foreach (var column in new[] { layout.Sources, layout.Targets })
        {
            for (var i = 1; i < column.Count; i++)
            {
                Assert.True(column[i].Y >= column[i - 1].Y + column[i - 1].SlotHeight + options.Gap - 1e-6);
            }

            Assert.All(column, n => Assert.True(n.Y >= options.Padding - 1e-6 && n.Y + n.SlotHeight <= layout.Height - options.Padding + 1e-6));
        }

        Assert.All(layout.Sources, n => Assert.Equal(options.LabelWidth, n.X));
        Assert.All(layout.Targets, n => Assert.Equal(options.Width - options.LabelWidth - options.NodeWidth, n.X));
        Assert.All(layout.Links, l =>
        {
            Assert.StartsWith("M268 ", l.Path);
            Assert.Contains(" 732 ", l.Path);
            Assert.True(l.Thickness > 0d);
        });
    }

    [Fact]
    public void Portfolio_epic_rows_take_the_colour_of_their_objective_and_a_shared_epic_appears_under_each()
    {
        var layout = Layout(new FlowOptions { Source = FlowSourceLevel.PortfolioEpics });

        Assert.Equal(PlanningLevel.PortfolioEpic, layout.SourceLevel);
        Assert.Equal(6, layout.Sources.Count);
        Assert.Equal(PlanningLevel.PortfolioEpic, layout.Sources[0].Row!.Level);

        var instant = Node(layout, FlowLayout.RowKey($"so:{GrowPayments}/pe:{InstantPayments}"));
        var tokenUnderGrow = Node(layout, FlowLayout.RowKey($"so:{GrowPayments}/pe:{Tokenisation}"));
        var tokenUnderResilience = Node(layout, FlowLayout.RowKey($"so:{Resilience}/pe:{Tokenisation}"));
        var fraud = Node(layout, FlowLayout.RowKey($"so:{Resilience}/pe:{Fraud}"));
        var dataLake = Node(layout, FlowLayout.RowKey($"so:{DepartmentPlanningTree.None}/pe:{DataLake}"));
        var noEpic = Node(layout, FlowLayout.RowKey($"so:{DepartmentPlanningTree.None}/pe:{DepartmentPlanningTree.None}"));

        Assert.Equal(0, instant.ColorIndex);
        Assert.Equal(0, tokenUnderGrow.ColorIndex);
        Assert.Equal(1, tokenUnderResilience.ColorIndex);
        Assert.Equal(1, fraud.ColorIndex);
        Assert.Equal(-1, dataLake.ColorIndex);
        Assert.Equal(-1, noEpic.ColorIndex);
        Assert.True(noEpic.IsBucket);
        Assert.False(dataLake.IsBucket);

        Assert.Equal(135m, LinkSp(layout, instant.Key, CoreArt));
        Assert.Equal(50m, LinkSp(layout, tokenUnderGrow.Key, FlowLayout.OutletKey));
        Assert.Equal(50m, LinkSp(layout, tokenUnderResilience.Key, FlowLayout.OutletKey));
        Assert.Equal(125m, Node(layout, FlowLayout.OutletKey).Sp);
        AssertConserved(layout);
    }

    [Fact]
    public void Colours_follow_the_objective_list_and_leftover_hues_go_to_the_rest()
    {
        var tree = Tree();

        var listed = FlowLayout.Build(tree, new FlowOptions(), [Resilience, 99, GrowPayments]);
        Assert.Equal(0, Node(listed, Resil).ColorIndex);
        Assert.Equal(2, Node(listed, Grow).ColorIndex);
        Assert.Equal(-1, Node(listed, NoObjective).ColorIndex);

        var unlisted = FlowLayout.Build(tree, new FlowOptions(), [50, 51, 52]);
        Assert.Equal(0, Node(unlisted, Grow).ColorIndex);
        Assert.Equal(1, Node(unlisted, Resil).ColorIndex);
    }

    [Fact]
    public void Small_rows_bundle_into_other_unless_every_row_is_asked_for()
    {
        var bundled = Layout(new FlowOptions { BundleShare = 0.3d });

        Assert.Equal([Grow, FlowLayout.OtherKey], bundled.Sources.Select(n => n.Key));
        var other = Node(bundled, FlowLayout.OtherKey);
        Assert.Equal(FlowNodeKind.Other, other.Kind);
        Assert.False(other.IsSelectable);
        Assert.Equal(2, other.BundledCount);
        Assert.Equal(["STRAT-2 Resilience", DepartmentPlanningTree.NoObjectiveTitle], other.BundledTitles);
        Assert.Equal(135m, other.Sp);
        Assert.Equal(60m, other.PlannedSp);
        Assert.Equal(75m, other.UnplannedSp);
        Assert.Equal(-1, other.ColorIndex);
        Assert.Equal(30m, LinkSp(bundled, FlowLayout.OtherKey, CoreArt));
        Assert.Equal(30m, LinkSp(bundled, FlowLayout.OtherKey, DataArt));
        Assert.Equal(75m, LinkSp(bundled, FlowLayout.OtherKey, FlowLayout.OutletKey));
        Assert.Equal(2, bundled.BundledRows);
        AssertConserved(bundled);

        var single = Layout(new FlowOptions { BundleShare = 0.2d });
        Assert.Equal(3, single.Sources.Count);
        Assert.Equal(0, single.BundledRows);

        var all = Layout(new FlowOptions { BundleShare = 0.3d, BundleSmall = false });
        Assert.Equal(3, all.Sources.Count);
        Assert.Equal(0, all.BundledRows);
    }

    [Fact]
    public void A_team_in_two_arts_counts_in_both_as_in_the_grid()
    {
        var layout = Layout(data: Data(boronInBothArts: true));

        var grow = Node(layout, Grow);
        Assert.Equal(135m, LinkSp(layout, Grow, CoreArt));
        Assert.Equal(20m, LinkSp(layout, Grow, DataArt));
        Assert.Equal(205m, grow.Sp);
        Assert.Equal(135m, grow.PlannedSp);
        Assert.Equal(80m, Node(layout, DataArt).Sp);
        Assert.Equal(90m, Node(layout, DataArt).CapacitySp);
        Assert.Equal(240m, layout.CapacitySp);
        Assert.Equal(245m, layout.PlannedSp);
        AssertConserved(layout);
    }

    [Fact]
    public void Rows_without_story_points_are_counted_but_not_drawn()
    {
        var layout = Layout(planning: new PlanningOptions { IncludeEmptyNodes = true });

        Assert.Equal(1, layout.HiddenRows);
        Assert.DoesNotContain(layout.Sources, n => n.Row?.EntityId == EmptyObjective);
        Assert.Equal(3, layout.Sources.Count);
    }

    [Fact]
    public void An_art_without_planned_work_still_shows_its_capacity_outline()
    {
        var data = Data();
        var layout = Layout(data: data with { Features = data.Features.Where(f => f.Id != 5).ToList() });

        var dataArt = Node(layout, DataArt);
        Assert.Equal(0m, dataArt.Sp);
        Assert.Equal(0d, dataArt.Height);
        Assert.True(dataArt.CapacityHeight > 0d);
        Assert.Equal(40m, dataArt.FreeSp);
        Assert.Equal(FlowLayout.OutletKey, layout.Targets[^1].Key);
        AssertConserved(layout);
    }

    [Fact]
    public void An_art_without_capacity_has_no_outline()
    {
        var layout = Layout(data: Data(capacityFactor: 0m));

        var core = Node(layout, CoreArt);
        Assert.Equal(0m, core.CapacitySp);
        Assert.Null(core.CapacityHeight);
        Assert.True(core.OverCapacity);
        Assert.Equal(0m, layout.CapacitySp);
        AssertConserved(layout);
    }

    [Fact]
    public void Nothing_to_draw_gives_an_empty_layout_without_failing()
    {
        var empty = FlowLayout.Build(PlanningTree.Empty, new FlowOptions());
        Assert.True(empty.IsEmpty);
        Assert.Empty(empty.Sources);
        Assert.Empty(empty.Targets);

        var filtered = Layout(planning: new PlanningOptions { Search = "nothing matches this" });
        Assert.True(filtered.IsEmpty);
        Assert.Equal(190m, filtered.CapacitySp);
        Assert.Equal(0, filtered.HiddenRows);
    }

    [Fact]
    public void Fit_shortens_long_text_with_an_ellipsis()
    {
        Assert.Equal("Grow payments", FlowLayout.Fit("Grow payments", 20));
        Assert.Equal("Grow paym…", FlowLayout.Fit("Grow payments", 10));
        Assert.Equal(10, FlowLayout.Fit("Grow payments", 10).Length);
        Assert.Equal("Grow…", FlowLayout.Fit("Grow payments", 6));
        Assert.Equal("Grow payments", FlowLayout.Fit("Grow payments", 1));
    }
}
