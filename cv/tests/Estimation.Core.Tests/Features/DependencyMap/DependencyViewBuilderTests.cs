using System.Diagnostics;
using Estimation.Core.Features.DependencyMap.Services;
using Estimation.Core.Features.Models;
using Xunit;
using static Estimation.Core.Tests.Features.DependencyMap.DependencyTestData;

namespace Estimation.Core.Tests.Features.DependencyMap;

public class DependencyViewBuilderTests
{
    private const string AxisLane = "t10";
    private const string RegalLane = "t11";
    private const string BravoLane = "t12";
    private const string ZetaLane = "t20";

    private static DependencyIndex Index(
        IReadOnlyList<DependencyFeature> features,
        IReadOnlyList<DependencyLinkRow> links,
        IReadOnlyList<DependencyStub>? stubs = null,
        IReadOnlyList<DependencyTeam>? teams = null,
        IReadOnlyList<DependencyArtSprint>? artSprints = null) =>
        DependencyClassifier.Build(Data(features, links, stubs, artSprints, teams));

    private static DependencyMapView View(
        DependencyIndex index,
        DependencyFilter? filter = null,
        FeatureCardFilter? featureFilter = null,
        string? myTeam = null,
        DependencySelection? selection = null,
        DependencyFocus? focus = null) =>
        DependencyViewBuilder.Build(
            index,
            new DependencyViewState(
                filter ?? new DependencyFilter(),
                featureFilter ?? new FeatureCardFilter(),
                myTeam,
                selection ?? DependencySelection.None,
                focus));

    private static DependencyFeature Pay(int id, params DependencyFeatureTeam[] teams) =>
        Feature(id, $"PAY-{id}", DependencyEndKind.InScope, "In Progress", null, teams);

    private static DependencyFeature PayWith(int id, string status, params DependencyFeatureTeam[] teams) =>
        Feature(id, $"PAY-{id}", DependencyEndKind.InScope, status, null, teams);

    private static DependencyFeature Crd(int id, params DependencyFeatureTeam[] teams) =>
        Feature(id, $"CRD-{id}", DependencyEndKind.OtherArt, "In Progress", null, teams);

    private static DependencyFeature EarlierPay(int id, params DependencyFeatureTeam[] teams) =>
        Feature(id, $"PAY-{id}", DependencyEndKind.OtherPi, "In Progress", null, teams);

    private static DependencyFilter Unlinked() => new() { ShowUnlinked = true };

    private static List<BoardCard> Cards(DependencyMapView view) =>
        view.Board.Lanes.SelectMany(l => l.Cards).ToList();

    private static BoardCard Card(DependencyMapView view, string nodeId) =>
        Assert.Single(Cards(view), c => c.NodeId == nodeId);

    private static List<string> CardIds(DependencyMapView view) =>
        Cards(view).Select(c => c.NodeId).Order(StringComparer.Ordinal).ToList();

    private static List<string> RowIds(DependencyMapView view) =>
        view.Rows.Select(r => r.Dependency.Id).Order(StringComparer.Ordinal).ToList();

    private static BoardLine Line(DependencyMapView view, string key) =>
        Assert.Single(view.Board.Lines, l => l.Key == key);

    private static DependencyIndex Mixed() =>
        Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Regal, true, false, 1)),
                Pay(4, Team(Bravo, true, false)),
                Pay(5, Team(Axis, true, false, 1))
            ],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Blocks("502", "PAY-2", "PAY-3"),
                Blocks("503", "PAY-1", "PAY-4"),
                Blocks("504", "PAY-5", "PAY-1")
            ]);

    private static DependencyIndex ThreeTeams() =>
        Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Bravo, true, false, 3)),
                Pay(4, Team(Regal, true, false, 1)),
                Pay(5, Team(Axis, true, false, 3))
            ],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Blocks("502", "PAY-4", "PAY-5"),
                Blocks("503", "PAY-1", "PAY-3"),
                Blocks("504", "PAY-4", "PAY-3"),
                Blocks("505", "PAY-3", "PAY-2")
            ]);

    [Theory]
    [InlineData("PI 26.3", "PI 26.3")]
    [InlineData("  pi 26.3 ", "pi 26.3")]
    [InlineData("PI26.3", "PI26.3")]
    [InlineData("26.3", "PI 26.3")]
    [InlineData("Pilot", "PI Pilot")]
    public void Pi_label_adds_pi_only_when_the_name_lacks_it(string name, string expected)
    {
        Assert.Equal(expected, DependencyViewBuilder.PiLabel(name));
    }

    [Fact]
    public void Summary_counts_ignore_the_risk_filter()
    {
        var view = View(Mixed(), new DependencyFilter { Risks = [DependencyRisk.OutOfOrder] });

        Assert.Equal(new RiskSummary(4, 1, 1, 1, 1), view.Summary);
        var row = Assert.Single(view.Rows);
        Assert.Equal("f2>f3", row.Dependency.Id);
        Assert.Equal(1, view.Matrix.Total);
    }

    [Fact]
    public void Summary_counts_follow_my_team_and_the_selection_but_not_the_risk_filter()
    {
        var myTeam = View(ThreeTeams(), myTeam: AxisLane);
        var myTeamOutOfOrder = View(ThreeTeams(), new DependencyFilter { Risks = [DependencyRisk.OutOfOrder] }, myTeam: AxisLane);
        var selected = View(ThreeTeams(), myTeam: AxisLane, selection: new DependencySelection(RegalLane, AxisLane));

        Assert.Equal(5, View(ThreeTeams()).Summary.Total);
        Assert.Equal(new RiskSummary(3, 0, 0, 3, 0), myTeam.Summary);
        Assert.Equal(new RiskSummary(3, 0, 0, 3, 0), myTeamOutOfOrder.Summary);
        Assert.Empty(myTeamOutOfOrder.Rows);
        Assert.Equal(new RiskSummary(1, 0, 0, 1, 0), selected.Summary);
    }

    [Fact]
    public void All_four_risks_selected_show_every_dependency()
    {
        var all = new DependencyFilter
        {
            Risks = [DependencyRisk.OutOfOrder, DependencyRisk.AtRisk, DependencyRisk.InOrder, DependencyRisk.NotChecked]
        };

        Assert.Equal(RowIds(View(Mixed())), RowIds(View(Mixed(), all)));
    }

    [Fact]
    public void Matrix_counts_row_needs_column()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Axis, true, false, 3)),
                Pay(4, Team(Regal, true, false, 1))
            ],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-3", "PAY-4")]);

        var matrix = View(index).Matrix;

        var cell = matrix.Cells[(RegalLane, AxisLane)];
        Assert.Equal(2, cell.Count);
        Assert.Equal(1, cell.OutOfOrder);
        Assert.Equal(1, cell.InOrder);
        Assert.Equal(0, cell.AtRisk);
        Assert.Equal(0, cell.Related);
        Assert.Equal(DependencyRisk.OutOfOrder, cell.Worst);
        Assert.Equal("Regal needs Axis: 2 dependencies: 1 out of order, 0 at risk, 1 in order, 0 not checked", cell.Tooltip);
        Assert.False(matrix.Cells.ContainsKey((AxisLane, RegalLane)));
        Assert.Equal(2, matrix.RowTotals[RegalLane]);
        Assert.Equal(0, matrix.RowTotals[AxisLane]);
        Assert.Equal(2, matrix.ColumnTotals[AxisLane]);
        Assert.Equal(0, matrix.ColumnTotals[RegalLane]);
        Assert.Equal(2, matrix.Total);
        Assert.Equal(0, matrix.RelatedTotal);
    }

    [Fact]
    public void Matrix_diagonal_cells_count_dependencies_inside_the_team()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 1)), Pay(2, Team(Axis, true, false, 3))],
            [Blocks("501", "PAY-1", "PAY-2")]);

        var cell = View(index).Matrix.Cells[(AxisLane, AxisLane)];

        Assert.Equal(1, cell.Count);
        Assert.Equal(DependencyRisk.InOrder, cell.Worst);
        Assert.Equal("Axis: 1 dependency inside the team: 0 out of order, 0 at risk, 1 in order, 0 not checked", cell.Tooltip);
    }

    [Fact]
    public void Matrix_diagonal_cells_of_collapsed_lanes_say_between_teams_or_features_in_the_group()
    {
        var outsideTeams = Enumerable.Range(30, 10).ToList();
        var features = outsideTeams
            .Select(t => Pay(t, Team(t, true, false, t == 39 ? 3 : 1)))
            .Append(Pay(1, Team(Axis, true, false, 4)))
            .Append(Pay(40))
            .Append(Pay(41))
            .ToList();
        var links = outsideTeams
            .Where(t => t < 38)
            .Select(t => Blocks($"5{t}", $"PAY-{t}", "PAY-1"))
            .Append(Blocks("538", "PAY-38", "PAY-39"))
            .Append(Relates("540", "PAY-40", "PAY-41"))
            .ToList();
        var teams = outsideTeams.Select(t => TeamInfo(t, false)).Append(TeamInfo(Axis)).ToList();
        var index = Index(features, links, teams: teams);

        var on = View(index).Matrix;
        var off = View(index, new DependencyFilter { ShowWithinTeam = false }).Matrix;

        var other = DependencyClassifier.OtherTeamsLaneId;
        var none = DependencyClassifier.NoTeamLaneId;
        Assert.Equal(
            "(Other teams): 1 dependency between teams in this group: 0 out of order, 0 at risk, 1 in order, 0 not checked",
            on.Cells[(other, other)].Tooltip);
        Assert.Equal("(No team): 1 related link (no order) between features in this group", on.Cells[(none, none)].Tooltip);
        Assert.Equal(1, off.Cells[(other, other)].Count);
        Assert.Equal(1, off.Cells[(none, none)].Related);
    }

    [Fact]
    public void Multi_team_consumer_counts_in_each_row_but_once_in_the_total()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3), Team(Bravo, false, false, 3)),
                Pay(3, Team(Axis, true, false, 1), Team(Bravo, false, false, 1)),
                Pay(4, Team(Regal, true, false, 4))
            ],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-3", "PAY-4")]);

        var matrix = View(index).Matrix;

        Assert.Equal(2, matrix.Cells[(RegalLane, AxisLane)].Count);
        Assert.Equal(1, matrix.Cells[(BravoLane, AxisLane)].Count);
        Assert.Equal(1, matrix.Cells[(RegalLane, BravoLane)].Count);
        Assert.Equal(2, matrix.RowTotals[RegalLane]);
        Assert.Equal(1, matrix.RowTotals[BravoLane]);
        Assert.Equal(2, matrix.ColumnTotals[AxisLane]);
        Assert.Equal(1, matrix.ColumnTotals[BravoLane]);
        Assert.Equal(2, matrix.Total);
    }

    [Fact]
    public void Undirected_dependencies_count_as_related_and_not_in_the_total()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Axis, true, false, 1), Team(Regal, false, false, 1)),
                Pay(4, Team(Axis, true, false, 2))
            ],
            [Relates("501", "PAY-1", "PAY-2"), Relates("502", "PAY-4", "PAY-3")]);

        var matrix = View(index).Matrix;

        Assert.Equal(0, matrix.Total);
        Assert.Equal(2, matrix.RelatedTotal);
        Assert.All(matrix.RowTotals.Values, v => Assert.Equal(0, v));
        Assert.Equal(2, matrix.Cells[(AxisLane, RegalLane)].Related);
        Assert.Equal(2, matrix.Cells[(RegalLane, AxisLane)].Related);
        Assert.Equal(1, matrix.Cells[(AxisLane, AxisLane)].Related);
        Assert.False(matrix.Cells.ContainsKey((RegalLane, RegalLane)));

        var cell = matrix.Cells[(RegalLane, AxisLane)];
        Assert.Equal(0, cell.Count);
        Assert.Equal(DependencyRisk.NotChecked, cell.Worst);
        Assert.Equal("Regal and Axis: 2 related links (no order)", cell.Tooltip);
    }

    [Fact]
    public void Matrix_tooltip_adds_related_links_to_counted_dependencies()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Axis, true, false, 1)),
                Pay(4, Team(Regal, true, false, 1))
            ],
            [Blocks("501", "PAY-1", "PAY-2"), Relates("502", "PAY-3", "PAY-4")]);

        var cell = View(index).Matrix.Cells[(RegalLane, AxisLane)];

        Assert.Equal(1, cell.Count);
        Assert.Equal(1, cell.Related);
        Assert.Equal(
            "Regal needs Axis: 1 dependency: 0 out of order, 0 at risk, 1 in order, 0 not checked · 1 related link (no order)",
            cell.Tooltip);
    }

    [Fact]
    public void Matrix_ignores_selection_and_my_team()
    {
        var plain = View(ThreeTeams()).Matrix;
        var narrowed = View(ThreeTeams(), myTeam: BravoLane, selection: new DependencySelection(RegalLane, AxisLane)).Matrix;

        Assert.Equal(plain.Lanes.Select(l => l.LaneId), narrowed.Lanes.Select(l => l.LaneId));
        Assert.Equal(
            plain.Cells.OrderBy(c => c.Key.Row).ThenBy(c => c.Key.Column).Select(c => (c.Key, c.Value.Count)),
            narrowed.Cells.OrderBy(c => c.Key.Row).ThenBy(c => c.Key.Column).Select(c => (c.Key, c.Value.Count)));
        Assert.Equal(plain.Total, narrowed.Total);
        Assert.Equal(5, narrowed.Total);
    }

    [Fact]
    public void Art_team_lanes_show_when_empty_and_other_lanes_only_when_used()
    {
        var teams = new[]
        {
            TeamInfo(Axis),
            TeamInfo(Regal),
            TeamInfo(Bravo),
            TeamInfo(Zeta, false, false, OtherArtName),
            TeamInfo(21, false)
        };
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(21, true, false, 2)),
                Pay(4),
                Crd(9, Team(Zeta, true, false, 1))
            ],
            [Blocks("501", "CRD-9", "PAY-1")],
            teams: teams);

        var shown = View(index).Matrix;
        var hidden = View(index, new DependencyFilter { ShowOtherArt = false }).Matrix;

        Assert.Equal([AxisLane, BravoLane, RegalLane, ZetaLane], shown.Lanes.Select(l => l.LaneId));
        Assert.Equal([AxisLane, BravoLane, RegalLane], hidden.Lanes.Select(l => l.LaneId));
        Assert.Empty(hidden.Cells);
        Assert.Equal(1, shown.Cells[(AxisLane, ZetaLane)].Count);
    }

    [Fact]
    public void Not_in_tool_lane_appears_only_when_switched_on()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 1))],
            [Blocks("501", "PAY-1", "BO-7")],
            [Stub("BO-7", DependencyStubKind.BusinessOutcome, "Grow revenue", "Open")]);

        var off = View(index).Matrix;
        var on = View(index, new DependencyFilter { ShowNotInTool = true }).Matrix;

        Assert.Equal([AxisLane], off.Lanes.Select(l => l.LaneId));
        Assert.Equal(0, off.Total);
        Assert.Equal([AxisLane, DependencyClassifier.NotInToolLaneId], on.Lanes.Select(l => l.LaneId));
        var cell = on.Cells[(DependencyClassifier.NotInToolLaneId, AxisLane)];
        Assert.Equal(1, cell.Count);
        Assert.Equal(1, cell.NotChecked);
        Assert.Equal(DependencyRisk.NotChecked, cell.Worst);
    }

    [Fact]
    public void Pair_selection_keeps_that_consumer_and_provider_lane()
    {
        var view = View(ThreeTeams(), selection: new DependencySelection(RegalLane, AxisLane));

        Assert.Equal(["f1>f2"], RowIds(view));
        Assert.Equal(["f1", "f2"], CardIds(view));
    }

    [Fact]
    public void Needs_selection_keeps_what_the_team_needs()
    {
        var view = View(ThreeTeams(), selection: new DependencySelection(RegalLane, null));

        Assert.Equal(["f1>f2", "f3>f2"], RowIds(view));
    }

    [Fact]
    public void Needed_by_selection_keeps_who_needs_the_team()
    {
        var view = View(ThreeTeams(), selection: new DependencySelection(null, AxisLane));

        Assert.Equal(["f1>f2", "f1>f3"], RowIds(view));
    }

    [Fact]
    public void Undirected_dependency_matches_a_selection_in_either_orientation()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Axis, true, false, 1)),
                Pay(4, Team(Regal, true, false, 3))
            ],
            [Relates("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-3", "PAY-4")]);

        Assert.Equal(["f1~f2", "f3>f4"], RowIds(View(index, selection: new DependencySelection(RegalLane, AxisLane))));
        Assert.Equal(["f1~f2"], RowIds(View(index, selection: new DependencySelection(AxisLane, RegalLane))));
        Assert.Equal(["f1~f2"], RowIds(View(index, selection: new DependencySelection(AxisLane, null))));
        Assert.Equal(["f1~f2", "f3>f4"], RowIds(View(index, selection: new DependencySelection(null, AxisLane))));
        Assert.Empty(View(index, selection: new DependencySelection(BravoLane, null)).Rows);
    }

    [Fact]
    public void My_team_keeps_both_directions_until_a_selection_is_set()
    {
        Assert.Equal(["f1>f2", "f1>f3", "f4>f5"], RowIds(View(ThreeTeams(), myTeam: AxisLane)));
        Assert.Equal(
            ["f1>f2"],
            RowIds(View(ThreeTeams(), myTeam: AxisLane, selection: new DependencySelection(RegalLane, AxisLane))));
    }

    [Fact]
    public void Link_type_filter_keeps_dependencies_with_any_link_of_a_chosen_type()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3), Pay(4), Pay(5), Pay(6)],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                DependsOn("502", "PAY-2", "PAY-1"),
                Blocks("503", "PAY-3", "PAY-4"),
                Relates("504", "PAY-5", "PAY-6")
            ]);

        Assert.Equal(["f1>f2"], RowIds(View(index, new DependencyFilter { LinkTypes = ["dependency"] })));
        Assert.Equal(["f1>f2", "f3>f4"], RowIds(View(index, new DependencyFilter { LinkTypes = ["Blocks"] })));
        Assert.Equal(["f1>f2", "f3>f4", "f5~f6"], RowIds(View(index, new DependencyFilter { LinkTypes = [" Relates ", "Blocks"] })));
        Assert.Empty(View(index, new DependencyFilter { LinkTypes = ["Nope"] }).Rows);
    }

    [Fact]
    public void Hidden_link_types_drop_their_dependencies_and_cards()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3), Pay(4), Pay(5), Pay(6)],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Link("502", "Cloners", "clones", "is cloned by", "PAY-3", "PAY-4"),
                Relates("503", "PAY-5", "PAY-6")
            ]);
        var hideClones = new DependencyFilter { LinkTypes = ["cloners"], LinkTypesNot = true };

        var view = View(index, hideClones);

        Assert.Equal(["f1>f2", "f3~f4", "f5~f6"], RowIds(View(index)));
        Assert.Equal(["f1>f2", "f5~f6"], RowIds(view));
        Assert.Equal(["f1", "f2", "f5", "f6"], CardIds(view));
        Assert.Equal(2, view.Summary.Total);
        Assert.Equal(["f1>f2"], RowIds(View(index, new DependencyFilter { LinkTypes = ["Cloners", "Relates"], LinkTypesNot = true })));
    }

    [Fact]
    public void Hiding_one_link_type_of_a_dependency_keeps_its_other_links()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 3)), Pay(2, Team(Regal, true, false, 1))],
            [Blocks("501", "PAY-1", "PAY-2"), Link("502", "Cloners", "clones", "is cloned by", "PAY-1", "PAY-2")]);

        var view = View(index, new DependencyFilter { LinkTypes = ["Cloners"], LinkTypesNot = true });

        var row = Assert.Single(view.Rows);
        Assert.Equal("f1>f2", row.Dependency.Id);
        Assert.True(row.Dependency.Directed);
        Assert.Equal(["501"], row.Dependency.Links.Select(l => l.JiraLinkId));
        Assert.Equal("is blocked by", row.Relation);
        Assert.Equal(1, Line(view, "f1|f2").LinkCount);
    }

    [Fact]
    public void Other_link_counts_leave_out_hidden_link_types()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 1)), Pay(2, Team(Regal, true, false, 3))],
            [
                Blocks("501", "PAY-1", "BO-7"),
                Link("502", "Cloners", "clones", "is cloned by", "PAY-1", "ZZZ-9"),
                Blocks("503", "PAY-1", "PAY-2")
            ],
            [Stub("BO-7", DependencyStubKind.BusinessOutcome, "Grow revenue")]);

        Assert.Equal(2, Card(View(index), "f1").OtherLinks);
        Assert.Equal(1, Card(View(index, new DependencyFilter { LinkTypes = ["Cloners"], LinkTypesNot = true }), "f1").OtherLinks);
    }

    [Fact]
    public void Link_type_filter_from_another_scope_is_dropped_against_the_index_types()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3), Pay(4)],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-3", "PAY-4")]);
        var filter = new DependencyFilter { LinkTypes = ["Relates"] };

        Assert.Empty(View(index, filter).Rows);

        filter.KeepKnownLinkTypes(index.LinkTypes.Select(t => t.TypeName));

        Assert.Empty(filter.LinkTypes);
        Assert.Equal(["f1>f2", "f3>f4"], RowIds(View(index, filter)));
    }

    [Fact]
    public void Link_type_filter_keeps_the_index_spelling_of_a_known_type()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3), Pay(4)],
            [Blocks("501", "PAY-1", "PAY-2"), Relates("502", "PAY-3", "PAY-4")]);
        var filter = new DependencyFilter { LinkTypes = ["blocks"] };

        filter.KeepKnownLinkTypes(index.LinkTypes.Select(t => t.TypeName));

        Assert.Equal(["Blocks"], filter.LinkTypes);
        Assert.Equal(["f1>f2"], RowIds(View(index, filter)));
    }

    [Fact]
    public void Link_type_filter_keeps_only_the_chosen_links_of_a_dependency()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 3)), Pay(2, Team(Regal, true, false, 1))],
            [Blocks("501", "PAY-1", "PAY-2"), Relates("502", "PAY-1", "PAY-2")]);

        var all = View(index);
        var blocks = View(index, new DependencyFilter { LinkTypes = ["Blocks"] });

        Assert.Equal(2, Line(all, "f1|f2").LinkCount);
        Assert.Equal("is blocked by / relates to", Assert.Single(all.Rows).Relation);

        var row = Assert.Single(blocks.Rows);
        Assert.Equal("f1>f2", row.Dependency.Id);
        Assert.True(row.Dependency.Directed);
        Assert.Equal(DependencyRisk.OutOfOrder, row.Dependency.Verdict.Risk);
        Assert.Equal("is blocked by", row.Relation);
        Assert.Equal(["501"], row.Dependency.Links.Select(l => l.JiraLinkId));
        Assert.Equal(1, Line(blocks, "f1|f2").LinkCount);
        Assert.Equal(new RiskSummary(1, 1, 0, 0, 0), blocks.Summary);
        Assert.Equal(1, blocks.Matrix.Total);
    }

    [Fact]
    public void Link_type_filter_with_only_unordered_links_left_shows_an_undirected_dependency()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 3)), Pay(2, Team(Regal, true, false, 1))],
            [Blocks("501", "PAY-2", "PAY-1"), Relates("502", "PAY-1", "PAY-2")]);

        var view = View(
            index,
            new DependencyFilter { LinkTypes = ["Relates"] },
            focus: new DependencyFocus("f1", ChainMode.Direct, null));

        var row = Assert.Single(view.Rows);
        var dependency = row.Dependency;
        Assert.False(dependency.Directed);
        Assert.Equal(("f1", "f2"), (dependency.Provider.NodeId, dependency.Consumer.NodeId));
        Assert.Equal(DependencyRisk.NotChecked, dependency.Verdict.Risk);
        Assert.Equal(DependencyFlags.Unordered, dependency.Verdict.Flags);
        Assert.Equal("\"relates to\" links have no order, so they are not checked", dependency.Verdict.Text);
        Assert.Equal("relates to", row.Relation);
        Assert.Equal(new RiskSummary(1, 0, 0, 0, 1), view.Summary);
        Assert.Equal(0, view.Matrix.Total);
        Assert.Equal(1, view.Matrix.RelatedTotal);
        Assert.Equal(1, view.Matrix.Cells[(AxisLane, RegalLane)].Related);
        Assert.Equal(0, view.Matrix.Cells[(AxisLane, RegalLane)].Count);

        var line = Line(view, "f1|f2");
        Assert.False(line.Directed);
        Assert.Equal(DependencyRisk.NotChecked, line.Risk);
        Assert.Equal(1, line.LinkCount);

        Assert.Empty(view.Panel!.Needs);
        Assert.Empty(view.Panel.NeededBy);
        var related = Assert.Single(view.Panel.Related);
        Assert.Equal("relates to", related.Phrase);
        Assert.False(related.HiddenByFilters);
        Assert.Equal(DependencyRisk.NotChecked, related.Risk);
    }

    [Fact]
    public void Other_pi_and_other_art_ends_hide_when_switched_off()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 2)),
                Pay(2, Team(Axis, true, false, 2)),
                Pay(3, Team(Regal, true, false, 1)),
                Pay(4, Team(Regal, true, false, 3)),
                EarlierPay(50),
                Crd(9)
            ],
            [Blocks("501", "PAY-50", "PAY-1"), Blocks("502", "CRD-9", "PAY-2"), Blocks("503", "PAY-3", "PAY-4")]);

        Assert.Equal(["f3>f4", "f50>f1", "f9>f2"], RowIds(View(index)));
        Assert.Equal(["f3>f4", "f9>f2"], RowIds(View(index, new DependencyFilter { ShowOtherPi = false })));
        Assert.Equal(["f3>f4", "f50>f1"], RowIds(View(index, new DependencyFilter { ShowOtherArt = false })));
        Assert.DoesNotContain("f50", CardIds(View(index, new DependencyFilter { ShowOtherPi = false })));
    }

    [Fact]
    public void Links_inside_one_team_hide_when_switched_off()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Axis, true, false, 3)),
                Pay(3, Team(Regal, true, false, 3)),
                Pay(4),
                Pay(5)
            ],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-1", "PAY-3"), Blocks("503", "PAY-4", "PAY-5")]);

        var view = View(index, new DependencyFilter { ShowWithinTeam = false });

        Assert.Equal(["f1>f2", "f1>f3", "f4>f5"], RowIds(View(index)));
        Assert.Equal(["f1>f3", "f4>f5"], RowIds(view));
        Assert.False(view.Matrix.Cells.ContainsKey((AxisLane, AxisLane)));
    }

    [Fact]
    public void Links_between_multi_team_features_that_share_a_team_stay_when_links_inside_one_team_are_off()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 3), Team(Bravo, false, false, 3)),
                Pay(2, Team(Bravo, true, false, 1), Team(Regal, false, false, 1)),
                Pay(3, Team(Regal, true, false, 1)),
                Pay(4, Team(Regal, true, false, 3))
            ],
            [Blocks("501", "PAY-2", "PAY-1"), Blocks("502", "PAY-3", "PAY-4")],
            teams: [TeamInfo(Axis), TeamInfo(Bravo, shared: true), TeamInfo(Regal)]);

        var on = View(index);
        var off = View(index, new DependencyFilter { ShowWithinTeam = false });

        Assert.Equal(["f2>f1", "f3>f4"], RowIds(on));
        Assert.Equal(1, on.Matrix.Cells[(BravoLane, BravoLane)].Count);
        Assert.Equal(1, on.Matrix.Cells[(RegalLane, RegalLane)].Count);

        Assert.Equal(["f2>f1"], RowIds(off));
        Assert.Equal(["f1|f2"], off.Board.Lines.Select(l => l.Key));
        Assert.Equal(1, off.Summary.Total);
        Assert.Equal(1, off.Matrix.Total);
        Assert.Equal(1, off.Matrix.Cells[(AxisLane, BravoLane)].Count);
        Assert.Equal(1, off.Matrix.Cells[(AxisLane, RegalLane)].Count);
        Assert.Equal(1, off.Matrix.Cells[(BravoLane, RegalLane)].Count);
        Assert.False(off.Matrix.Cells.ContainsKey((BravoLane, BravoLane)));
        Assert.False(off.Matrix.Cells.ContainsKey((RegalLane, RegalLane)));
        Assert.Equal(1, off.Matrix.RowTotals[AxisLane]);
        Assert.Equal(1, off.Matrix.RowTotals[BravoLane]);
        Assert.Equal(0, off.Matrix.RowTotals[RegalLane]);
        Assert.Equal(1, off.Matrix.ColumnTotals[BravoLane]);
        Assert.Equal(1, off.Matrix.ColumnTotals[RegalLane]);
        Assert.Equal(0, off.Matrix.ColumnTotals[AxisLane]);
    }

    [Fact]
    public void Selection_matches_only_the_cells_the_matrix_counts_when_links_inside_one_team_are_off()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 3), Team(Regal, false, false, 3)),
                Pay(2, Team(Regal, true, false, 1)),
                Pay(3, Team(Regal, true, false, 1))
            ],
            [Blocks("501", "PAY-2", "PAY-1"), Relates("502", "PAY-1", "PAY-3")]);
        var off = new DependencyFilter { ShowWithinTeam = false };

        var diagonal = View(index, off, selection: new DependencySelection(RegalLane, RegalLane));
        var regalNeeds = View(index, off, selection: new DependencySelection(RegalLane, null));

        Assert.Empty(diagonal.Rows);
        Assert.Equal(0, diagonal.Summary.Total);
        Assert.Equal(["f1~f3"], RowIds(regalNeeds));
        Assert.Equal(0, regalNeeds.Matrix.RowTotals[RegalLane]);
        Assert.Equal(["f1~f3", "f2>f1"], RowIds(View(index, off, selection: new DependencySelection(AxisLane, RegalLane))));
        Assert.Equal(["f1~f3", "f2>f1"], RowIds(View(index, selection: new DependencySelection(RegalLane, RegalLane))));
        Assert.Equal(["f1~f3", "f2>f1"], RowIds(View(index, selection: new DependencySelection(RegalLane, null))));
    }

    [Fact]
    public void Feature_filter_keeps_dependencies_with_a_matching_in_scope_end_and_marks_the_other_as_context()
    {
        var index = Index(
            [
                PayWith(1, "Funnel", Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Regal, true, false, 4)),
                Crd(9, Team(Axis, true, false, 1))
            ],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-2", "PAY-3"), Blocks("503", "CRD-9", "PAY-1")]);

        var view = View(index, featureFilter: new FeatureCardFilter { Statuses = ["Funnel"] });

        Assert.Equal(["f1>f2", "f9>f1"], RowIds(view));
        Assert.Equal(2, view.Summary.Total);
        Assert.Equal(["f1", "f2", "f9"], CardIds(view));
        Assert.Equal(CardState.Normal, Card(view, "f1").State);
        Assert.Equal(CardState.Context, Card(view, "f2").State);
        Assert.Equal(CardState.Normal, Card(view, "f9").State);
        Assert.Equal(["f1|f2", "f1|f9"], view.Board.Lines.Select(l => l.Key));
    }

    [Fact]
    public void Unlinked_features_appear_only_when_asked_and_are_counted()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Axis, true, false, 2)),
                Feature(4, null, DependencyEndKind.InScope, "In Progress", null, Team(Regal, true, false, 2)),
                Crd(9, Team(Axis, true, false, 1))
            ],
            [Blocks("501", "PAY-1", "PAY-2")]);

        var hidden = View(index);
        var shown = View(index, Unlinked());
        var myTeam = View(index, Unlinked(), myTeam: AxisLane);
        var selected = View(index, Unlinked(), selection: new DependencySelection(RegalLane, null));

        Assert.Equal(2, hidden.HiddenUnlinked);
        Assert.Equal(["f1", "f2"], CardIds(hidden));
        Assert.Equal(2, shown.HiddenUnlinked);
        Assert.Equal(["f1", "f2", "f3", "f4"], CardIds(shown));
        Assert.Null(Card(shown, "f4").JiraId);
        Assert.Equal(1, myTeam.HiddenUnlinked);
        Assert.Equal(["f1", "f2", "f3"], CardIds(myTeam));
        Assert.Equal(["f1", "f2"], CardIds(selected));
        Assert.Equal(0, selected.HiddenUnlinked);
        Assert.Equal(0, View(index, selection: new DependencySelection(RegalLane, null)).HiddenUnlinked);
    }

    [Fact]
    public void Features_whose_links_are_all_filtered_out_count_as_unlinked()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 1)), Pay(2, Team(Regal, true, false, 3))],
            [Relates("501", "PAY-1", "PAY-2")]);

        var view = View(index, new DependencyFilter { LinkTypes = ["Blocks"] });

        Assert.Empty(view.Rows);
        Assert.Empty(Cards(view));
        Assert.Equal(2, view.HiddenUnlinked);
    }

    [Fact]
    public void Rows_are_sorted_riskiest_first()
    {
        var mixed = View(Mixed());
        var ties = View(Index(
            [
                Pay(1, Team(Bravo, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Axis, true, false, 3)),
                Pay(4, Team(Axis, true, false, 4))
            ],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-1", "PAY-4"), Blocks("503", "PAY-1", "PAY-3")]));

        Assert.Equal(["f2>f3", "f5>f1", "f1>f4", "f1>f2"], mixed.Rows.Select(r => r.Dependency.Id));
        Assert.Equal(
            [DependencyRisk.OutOfOrder, DependencyRisk.AtRisk, DependencyRisk.NotChecked, DependencyRisk.InOrder],
            mixed.Rows.Select(r => r.Dependency.Verdict.Risk));
        Assert.Equal(["f1>f3", "f1>f4", "f1>f2"], ties.Rows.Select(r => r.Dependency.Id));
    }

    [Fact]
    public void Rows_fill_teams_plans_relation_and_scope()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1), Team(Regal, false, false, 2)),
                Pay(2),
                Pay(3, Team(Bravo, true, false, 3)),
                Crd(9, Team(Zeta, true, false, 4))
            ],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                DependsOn("502", "PAY-2", "PAY-1"),
                Blocks("503", "BO-7", "PAY-3"),
                Blocks("504", "CRD-9", "PAY-3")
            ],
            [Stub("BO-7", DependencyStubKind.BusinessOutcome, "Grow revenue")]);

        var rows = View(index, new DependencyFilter { ShowNotInTool = true }).Rows;

        var merged = Assert.Single(rows, r => r.Dependency.Id == "f1>f2");
        Assert.Equal("is blocked by / depends on", merged.Relation);
        Assert.Equal("(No team)", merged.ConsumerTeams);
        Assert.Equal("Axis, Regal", merged.ProviderTeams);
        Assert.Equal("Not planned", merged.ConsumerPlan);
        Assert.Equal("S1–S2", merged.ProviderPlan);
        Assert.Equal(int.MaxValue, merged.ConsumerPlanSort);
        Assert.Equal(2, merged.ProviderPlanSort);
        Assert.Equal("This ART and PI", merged.ProviderScope);

        var stub = Assert.Single(rows, r => r.Dependency.Id == "k:BO-7>f3");
        Assert.Equal("—", stub.ProviderTeams);
        Assert.Equal("—", stub.ProviderPlan);
        Assert.Equal(int.MaxValue, stub.ProviderPlanSort);
        Assert.Equal("Business outcome", stub.ProviderScope);
        Assert.Equal("Bravo", stub.ConsumerTeams);
        Assert.Equal("S3", stub.ConsumerPlan);
        Assert.Equal(3, stub.ConsumerPlanSort);

        var outside = Assert.Single(rows, r => r.Dependency.Id == "f9>f3");
        Assert.Equal("ART Cards", outside.ProviderScope);
        Assert.Equal("Zeta", outside.ProviderTeams);
    }

    [Fact]
    public void Card_spans_its_first_to_last_planned_column()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 2), Team(Regal, false, false, 3, 4)),
                Pay(2, Team(Axis, true, false, 3)),
                Pay(3, Team(Axis, true, false, 5))
            ],
            []);

        var view = View(index, Unlinked());

        Assert.Equal(
            ["Sprint 1", "Sprint 2", "Sprint 3", "Sprint 4", "IP", "Not planned"],
            view.Board.Columns.Select(c => c.Name));
        var wide = Card(view, "f1");
        Assert.Equal(2, wide.ColumnStart);
        Assert.Equal(5, wide.ColumnEnd);
        Assert.Equal("S2–S4", wide.SprintText);
        Assert.False(wide.Approximate);
        var single = Card(view, "f2");
        Assert.Equal((3, 4), (single.ColumnStart, single.ColumnEnd));
        Assert.Equal("S3", single.SprintText);
        var ip = Card(view, "f3");
        Assert.Equal((5, 6), (ip.ColumnStart, ip.ColumnEnd));
        Assert.Equal("IP", ip.SprintText);
    }

    [Fact]
    public void Unplanned_card_is_in_the_not_planned_column_at_the_right()
    {
        var index = Index([Pay(1, Team(Axis, true, false)), Pay(2)], []);

        var view = View(index, Unlinked());

        var last = view.Board.Columns[^1];
        Assert.Equal(BoardColumnKind.NotPlanned, last.Kind);
        foreach (var card in Cards(view))
        {
            Assert.Equal(view.Board.Columns.Count, card.ColumnStart);
            Assert.Equal(view.Board.Columns.Count + 1, card.ColumnEnd);
            Assert.Equal("Not planned", card.SprintText);
        }
    }

    [Fact]
    public void Earlier_and_later_columns_appear_only_for_cards_that_need_them()
    {
        var inScope = View(Index([Pay(1, Team(Axis, true, false, 2)), Pay(2, Team(Axis, true, false, 4))], [Blocks("501", "PAY-1", "PAY-2")]));
        var earlier = View(Index([EarlierPay(50), Pay(1, Team(Axis, true, false, 2))], [Blocks("501", "PAY-50", "PAY-1")]));
        var later = View(Index(
            [Crd(9, Team(Zeta, true, false)).InLaterPi(), Pay(1, Team(Axis, true, false, 2))],
            [Blocks("501", "CRD-9", "PAY-1")]));

        Assert.DoesNotContain(inScope.Board.Columns, c => c.Kind is BoardColumnKind.Earlier or BoardColumnKind.Later);

        Assert.Equal(BoardColumnKind.Earlier, earlier.Board.Columns[0].Kind);
        Assert.DoesNotContain(earlier.Board.Columns, c => c.Kind == BoardColumnKind.Later);
        var earlierCard = Card(earlier, "f50");
        Assert.Equal((1, 2), (earlierCard.ColumnStart, earlierCard.ColumnEnd));
        Assert.Equal(EarlierPiName, earlierCard.SprintText);
        Assert.Equal(3, Card(earlier, "f1").ColumnStart);

        Assert.Equal(BoardColumnKind.Later, later.Board.Columns[^2].Kind);
        Assert.DoesNotContain(later.Board.Columns, c => c.Kind == BoardColumnKind.Earlier);
        var laterCard = Card(later, "f9");
        Assert.Equal((6, 7), (laterCard.ColumnStart, laterCard.ColumnEnd));
        Assert.Equal(LaterPiName, laterCard.SprintText);
        Assert.Equal($"In {LaterPiName}", laterCard.SprintTooltip);
    }

    [Fact]
    public void Card_in_a_pi_without_dates_stays_in_the_not_planned_column_with_its_pi_name()
    {
        var view = View(Index(
            [Crd(9, Team(Zeta, true, false)).InLaterPi() with { PiStart = null }, Pay(1, Team(Axis, true, false, 2))],
            [Blocks("501", "CRD-9", "PAY-1")]));

        var card = Card(view, "f9");
        Assert.Equal(BoardColumnKind.NotPlanned, view.Board.Columns[^1].Kind);
        Assert.DoesNotContain(view.Board.Columns, c => c.Kind == BoardColumnKind.Later);
        Assert.Equal((view.Board.Columns.Count, view.Board.Columns.Count + 1), (card.ColumnStart, card.ColumnEnd));
        Assert.Equal(LaterPiName, card.SprintText);
        Assert.Equal($"In {LaterPiName}", card.SprintTooltip);
        Assert.StartsWith($"CRD-9 blocks PAY-1 · provider ends {LaterPiName} · consumer starts S2 (Axis) · ", Line(view, "f1|f9").Title);
    }

    [Fact]
    public void Outside_feature_in_the_selected_pi_sits_in_its_sprint_column()
    {
        var index = Index(
            [Crd(9, Team(Zeta, true, false, 2, 3)), Pay(1, Team(Axis, true, false, 4))],
            [Blocks("501", "CRD-9", "PAY-1")]);

        var card = Card(View(index), "f9");

        Assert.Equal((2, 4), (card.ColumnStart, card.ColumnEnd));
        Assert.Equal("S2–S3", card.SprintText);
        Assert.Equal($"ART {OtherArtName}", card.ScopeText);
        Assert.Equal("Zeta ★: Sprint 2, Sprint 3 (19 Jan–15 Feb)", card.SprintTooltip);
    }

    [Fact]
    public void Empty_index_builds_an_empty_view()
    {
        var view = View(Index([], []), Unlinked(), focus: new DependencyFocus("f1", ChainMode.Direct, null));

        Assert.Equal(new RiskSummary(0, 0, 0, 0, 0), view.Summary);
        Assert.Empty(view.Rows);
        Assert.Empty(view.Board.Lanes);
        Assert.Empty(view.Board.Lines);
        Assert.Equal(BoardColumnKind.NotPlanned, view.Board.Columns[^1].Kind);
        Assert.Empty(view.Matrix.Lanes);
        Assert.Null(view.Panel);
        Assert.Equal(0, view.HiddenUnlinked);
        Assert.Matches("^[0-9a-f]{16}$", view.Board.LinesVersion);
    }

    [Fact]
    public void Without_art_sprints_cards_use_one_planned_column()
    {
        var index = Index([Pay(1, Team(Axis, true, false, 2)), Pay(2, Team(Axis, true, false))], [], artSprints: []);

        var view = View(index, Unlinked());

        Assert.Equal([BoardColumnKind.Planned, BoardColumnKind.NotPlanned], view.Board.Columns.Select(c => c.Kind));
        Assert.Equal((1, 2, "Planned"), (Card(view, "f1").ColumnStart, Card(view, "f1").ColumnEnd, Card(view, "f1").SprintText));
        Assert.Equal((2, 3, "Not planned"), (Card(view, "f2").ColumnStart, Card(view, "f2").ColumnEnd, Card(view, "f2").SprintText));
    }

    [Fact]
    public void Home_lane_prefers_selection_then_primary_then_art_team_then_name()
    {
        var teams = new[] { TeamInfo(Axis), TeamInfo(Regal), TeamInfo(Bravo), TeamInfo(Zeta, false, false, OtherArtName), TeamInfo(21, false) };
        var index = Index(
            [
                Pay(1, Team(Axis, false, false, 1), Team(Regal, true, false, 1)),
                Pay(2, Team(Zeta, false, false, 1), Team(Bravo, false, false, 1)),
                Pay(3, Team(21, false, false, 1), Team(Zeta, false, false, 1)),
                Pay(4),
                Pay(5, Team(Axis, false, false, 1), Team(Regal, true, false, 1)),
                Pay(6, Team(Bravo, false, false, 3))
            ],
            [Blocks("501", "PAY-5", "PAY-6")],
            teams: teams);

        var plain = View(index, Unlinked());

        Assert.Equal(RegalLane, Card(plain, "f1").LaneId);
        Assert.Equal(BravoLane, Card(plain, "f2").LaneId);
        Assert.Equal("t21", Card(plain, "f3").LaneId);
        Assert.Equal(DependencyClassifier.NoTeamLaneId, Card(plain, "f4").LaneId);
        Assert.Equal(RegalLane, Card(plain, "f5").LaneId);
        Assert.Equal(BravoLane, Card(plain, "f6").LaneId);

        var selected = View(index, selection: new DependencySelection(null, AxisLane), myTeam: RegalLane);
        Assert.Equal(AxisLane, Card(selected, "f5").LaneId);
        Assert.Equal(BravoLane, Card(selected, "f6").LaneId);

        Assert.Equal(AxisLane, Card(View(index, myTeam: AxisLane), "f5").LaneId);
    }

    [Fact]
    public void Multi_team_feature_has_one_card_and_counts_its_extra_teams()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 1), Team(Regal, false, false, 2), Team(Bravo, false, true))],
            []);

        var view = View(index, Unlinked());

        var card = Assert.Single(Cards(view));
        Assert.Equal(AxisLane, card.LaneId);
        Assert.Equal(2, card.ExtraTeams);
        Assert.Equal("Axis ★: S1\nRegal: S2\nBravo: not planned · below the line", card.TeamsText);
        Assert.True(card.BelowLine);
        Assert.Equal((1, 3), (card.ColumnStart, card.ColumnEnd));
        Assert.Equal([AxisLane], view.Board.Lanes.Select(l => l.Lane.LaneId));
    }

    [Fact]
    public void Cards_in_a_lane_get_rows_without_overlap()
    {
        var sprints = Sprints();
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1, 2)),
                Pay(2, Team(Axis, true, false, 2)),
                Pay(3, Team(Axis, true, false, 3)),
                Pay(4, Team(Axis, true, false, 1)),
                Pay(5, TeamWithSprints(Axis, true, false, 2, PlannedFrom(sprints[3], Axis))),
                Pay(6, TeamWithSprints(Axis, true, false, 1, PlannedFrom(sprints[3], Axis)))
            ],
            []);

        var lane = Assert.Single(View(index, Unlinked()).Board.Lanes);

        Assert.Equal(["f4", "f1", "f2", "f3", "f6", "f5"], lane.Cards.Select(c => c.NodeId));
        Assert.Equal([1, 2, 1, 1, 1, 2], lane.Cards.Select(c => c.Row));
        Assert.Equal(2, lane.RowCount);
    }

    [Fact]
    public void My_team_lane_comes_first_then_art_teams_then_others()
    {
        var teams = new[] { TeamInfo(Axis), TeamInfo(Regal), TeamInfo(Zeta, false, false, OtherArtName) };
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(4),
                Crd(9, Team(Zeta, true, false, 1))
            ],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "CRD-9", "PAY-2"), Blocks("503", "PAY-4", "PAY-2")],
            teams: teams);

        Assert.Equal(
            [AxisLane, RegalLane, ZetaLane, DependencyClassifier.NoTeamLaneId],
            View(index).Board.Lanes.Select(l => l.Lane.LaneId));
        Assert.Equal(
            [RegalLane, AxisLane, ZetaLane, DependencyClassifier.NoTeamLaneId],
            View(index, myTeam: RegalLane).Board.Lanes.Select(l => l.Lane.LaneId));
        Assert.Equal([ZetaLane, RegalLane], View(index, myTeam: ZetaLane).Board.Lanes.Select(l => l.Lane.LaneId));
    }

    [Fact]
    public void Lines_merge_dependencies_between_the_same_two_cards()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 1)), Pay(2, Team(Regal, true, false, 3))],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Blocks("502", "PAY-2", "PAY-1"),
                Relates("503", "PAY-1", "PAY-2"),
                DependsOn("504", "PAY-2", "PAY-1")
            ]);

        var line = Assert.Single(View(index).Board.Lines);

        Assert.Equal("f1|f2", line.Key);
        Assert.Equal(4, line.LinkCount);
        Assert.Equal(["f1>f2", "f2>f1"], line.DependencyIds);
        Assert.Equal(DependencyRisk.OutOfOrder, line.Risk);
        Assert.Equal(2, line.Title.Split('\n').Length);
    }

    [Fact]
    public void Two_way_dependencies_draw_one_line_with_both_arrows()
    {
        var index = Index(
            [Pay(2, Team(Axis, true, false, 1)), Pay(10, Team(Regal, true, false, 3))],
            [Blocks("501", "PAY-2", "PAY-10"), Blocks("502", "PAY-10", "PAY-2")]);

        var line = Assert.Single(View(index).Board.Lines);

        Assert.Equal("f10|f2", line.Key);
        Assert.True(line.BothWays);
        Assert.True(line.Directed);
        Assert.Equal(("f10", "f2"), (line.FromNodeId, line.ToNodeId));
    }

    [Fact]
    public void Undirected_line_runs_from_the_earlier_card()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 3)),
                Pay(2, Team(Regal, true, false, 1)),
                Pay(3, Team(Axis, true, false, 2)),
                Pay(4, Team(Regal, true, false, 2)),
                Pay(5, Team(Axis, true, false, 4)),
                Pay(6, Team(Regal, true, false, 1))
            ],
            [Relates("501", "PAY-1", "PAY-2"), Relates("502", "PAY-4", "PAY-3"), Blocks("503", "PAY-6", "PAY-5")]);

        var view = View(index);

        var undirected = Line(view, "f1|f2");
        Assert.Equal(("f2", "f1"), (undirected.FromNodeId, undirected.ToNodeId));
        Assert.False(undirected.Directed);
        Assert.False(undirected.BothWays);
        var tie = Line(view, "f3|f4");
        Assert.Equal(("f3", "f4"), (tie.FromNodeId, tie.ToNodeId));
        var directed = Line(view, "f5|f6");
        Assert.Equal(("f6", "f5"), (directed.FromNodeId, directed.ToNodeId));
        Assert.True(directed.Directed);
        Assert.False(directed.BothWays);
    }

    [Fact]
    public void Stub_dependencies_never_draw_lines_but_count_as_other_links()
    {
        var index = Index(
            [Pay(1, Team(Axis, true, false, 1)), Pay(2, Team(Regal, true, false, 3)), Pay(3, Team(Regal, true, false, 3))],
            [
                Blocks("501", "PAY-1", "BO-7"),
                Link("502", "Cloners", "clones", "is cloned by", "PAY-1", "ZZZ-9"),
                Blocks("503", "PAY-1", "PAY-2"),
                Blocks("504", "BO-7", "PAY-3")
            ],
            [Stub("BO-7", DependencyStubKind.BusinessOutcome, "Grow revenue")]);

        var hidden = View(index);
        var shown = View(index, new DependencyFilter { ShowNotInTool = true });

        Assert.Equal(["f1>f2"], RowIds(hidden));
        Assert.Equal(["f1|f2"], hidden.Board.Lines.Select(l => l.Key));
        var hiddenCard = Card(hidden, "f1");
        Assert.Equal(2, hiddenCard.OtherLinks);
        Assert.Equal(1, hiddenCard.NeededBy);
        Assert.Equal(0, hiddenCard.Related);
        Assert.Equal(1, View(index, Unlinked()).HiddenUnlinked);
        Assert.Equal(1, Card(View(index, Unlinked()), "f3").OtherLinks);

        Assert.Equal(4, shown.Rows.Count);
        Assert.Equal(["f1|f2"], shown.Board.Lines.Select(l => l.Key));
        var shownCard = Card(shown, "f1");
        Assert.Equal(2, shownCard.OtherLinks);
        Assert.Equal(1, shownCard.NeededBy);
        Assert.Equal(0, shownCard.Related);
        Assert.DoesNotContain(Cards(shown), c => c.NodeId.StartsWith("k:", StringComparison.Ordinal));

        var whole = View(
            index,
            new DependencyFilter { ShowNotInTool = true },
            focus: new DependencyFocus("f1", ChainMode.WholeChain, null));
        Assert.DoesNotContain("f3", whole.FocusNodeIds);
        Assert.Equal((0, 1), (whole.Panel!.ChainUpstream, whole.Panel.ChainDownstream));
    }

    [Fact]
    public void Line_title_names_both_ends_sprints_teams_and_the_verdict()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3), Team(Bravo, false, false, 4)),
                Pay(3, Team(Axis, true, false)),
                EarlierPay(50)
            ],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                DependsOn("502", "PAY-2", "PAY-3"),
                Relates("503", "PAY-1", "PAY-3"),
                Blocks("504", "PAY-50", "PAY-1")
            ]);

        var view = View(index);

        Assert.Equal(
            "PAY-1 blocks PAY-2 · provider ends S1 (Axis) · consumer starts S3 (Regal) · In order: PAY-1 ends in Sprint 1, before PAY-2 starts in Sprint 3",
            Line(view, "f1|f2").Title);
        Assert.Equal(
            "PAY-3 is depended on by PAY-2 · provider ends not planned · consumer starts S3 (Regal) · At risk: PAY-3 has no sprint in this PI, but PAY-2 starts in Sprint 3",
            Line(view, "f2|f3").Title);
        Assert.Equal("PAY-1 relates to PAY-3 · no order, not checked", Line(view, "f1|f3").Title);
        Assert.Equal(
            $"PAY-50 blocks PAY-1 · provider ends {EarlierPiName} · consumer starts S1 (Axis) · At risk: PAY-50 is from an earlier PI ({EarlierPiName}) and is still open",
            Line(view, "f1|f50").Title);
    }

    [Fact]
    public void Lines_version_changes_only_when_lines_change()
    {
        DependencyIndex Build(int providerSprint) =>
            Index(
                [Pay(1, Team(Axis, true, false, providerSprint)), Pay(2, Team(Regal, true, false, 3)), Pay(3, Team(Regal, true, false, 4))],
                [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-2", "PAY-3")]);

        var first = View(Build(1)).Board.LinesVersion;
        var again = View(Build(1)).Board.LinesVersion;
        var riskChanged = View(Build(4)).Board.LinesVersion;
        var focused = View(Build(1), focus: new DependencyFocus("f1", ChainMode.WholeChain, null)).Board.LinesVersion;
        var fewer = View(Build(1), selection: new DependencySelection(RegalLane, AxisLane)).Board.LinesVersion;

        Assert.Matches("^[0-9a-f]{16}$", first);
        Assert.Equal(first, again);
        Assert.NotEqual(first, riskChanged);
        Assert.Equal(first, focused);
        Assert.NotEqual(first, fewer);
    }

    [Fact]
    public void Focus_marks_the_feature_and_its_neighbours_and_dims_the_rest()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3), Pay(4), Pay(5)],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-2", "PAY-3"), Blocks("503", "PAY-4", "PAY-5")]);

        var view = View(index, focus: new DependencyFocus("f2", ChainMode.Direct, null));

        Assert.Equal(CardState.Focus, Card(view, "f2").State);
        Assert.Equal(CardState.Chain, Card(view, "f1").State);
        Assert.Equal(CardState.Chain, Card(view, "f3").State);
        Assert.Equal(CardState.Dimmed, Card(view, "f4").State);
        Assert.Equal(CardState.Dimmed, Card(view, "f5").State);
        Assert.Equal(["f1", "f2", "f3"], view.FocusNodeIds.Order(StringComparer.Ordinal));
        Assert.Equal("f2", view.Panel?.NodeId);
        Assert.All(Cards(view), c => Assert.False(c.AddedByFocus));
    }

    [Fact]
    public void Whole_chain_walks_upstream_and_downstream_but_undirected_only_one_hop()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3), Pay(4), Pay(6), Pay(7), Pay(8), Pay(9)],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Blocks("502", "PAY-2", "PAY-3"),
                Blocks("503", "PAY-3", "PAY-4"),
                Relates("504", "PAY-2", "PAY-6"),
                Relates("505", "PAY-6", "PAY-7"),
                Blocks("506", "PAY-6", "PAY-8"),
                Blocks("507", "PAY-9", "PAY-6")
            ]);

        var whole = View(index, focus: new DependencyFocus("f2", ChainMode.WholeChain, null));
        var direct = View(index, focus: new DependencyFocus("f2", ChainMode.Direct, null));

        Assert.Equal(["f1", "f2", "f3", "f4", "f6"], whole.FocusNodeIds.Order(StringComparer.Ordinal));
        Assert.Equal(["f1|f2", "f2|f3", "f2|f6", "f3|f4"], whole.FocusLineKeys.Order(StringComparer.Ordinal));
        Assert.Equal(1, whole.Panel!.ChainUpstream);
        Assert.Equal(2, whole.Panel.ChainDownstream);
        Assert.Equal(CardState.Chain, Card(whole, "f4").State);
        Assert.Equal(CardState.Dimmed, Card(whole, "f7").State);
        Assert.Equal(CardState.Dimmed, Card(whole, "f8").State);
        Assert.Equal(CardState.Dimmed, Card(whole, "f9").State);

        Assert.Equal(["f1", "f2", "f3", "f6"], direct.FocusNodeIds.Order(StringComparer.Ordinal));
        Assert.Equal(["f1|f2", "f2|f3", "f2|f6"], direct.FocusLineKeys.Order(StringComparer.Ordinal));
        Assert.Equal(1, direct.Panel!.ChainUpstream);
        Assert.Equal(2, direct.Panel.ChainDownstream);
        Assert.Equal(CardState.Dimmed, Card(direct, "f4").State);
    }

    [Fact]
    public void Whole_chain_terminates_on_cycles()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3)],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-2", "PAY-3"), Blocks("503", "PAY-3", "PAY-1")]);

        var view = View(index, focus: new DependencyFocus("f1", ChainMode.WholeChain, null));

        Assert.Equal(["f1", "f2", "f3"], view.FocusNodeIds.Order(StringComparer.Ordinal));
        Assert.Equal(2, view.Panel!.ChainUpstream);
        Assert.Equal(2, view.Panel.ChainDownstream);
        Assert.Equal(3, view.FocusLineKeys.Count);
    }

    [Fact]
    public void Whole_chain_follows_only_the_dependencies_the_link_filters_keep()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3)],
            [Blocks("501", "PAY-1", "PAY-2"), DependsOn("502", "PAY-3", "PAY-2")]);

        var view = View(
            index,
            new DependencyFilter { LinkTypes = ["Blocks"] },
            focus: new DependencyFocus("f1", ChainMode.WholeChain, null));

        Assert.Equal(["f1", "f2"], view.FocusNodeIds.Order(StringComparer.Ordinal));
        Assert.Equal(0, view.Panel!.ChainUpstream);
        Assert.Equal(1, view.Panel.ChainDownstream);
        Assert.DoesNotContain(Cards(view), c => c.NodeId == "f3");
    }

    [Fact]
    public void Focused_feature_hidden_by_filters_is_added_and_flagged()
    {
        var index = Index(
            [Pay(1), Pay(2), PayWith(3, "Funnel"), Pay(4), Pay(5)],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-3", "PAY-4")]);
        var funnel = new FeatureCardFilter { Statuses = ["Funnel"] };

        var plain = View(index, featureFilter: funnel);
        var hidden = View(index, featureFilter: funnel, focus: new DependencyFocus("f1", ChainMode.Direct, null));
        var visible = View(index, featureFilter: funnel, focus: new DependencyFocus("f3", ChainMode.Direct, null));
        var unlinked = View(index, focus: new DependencyFocus("f5", ChainMode.Direct, null));

        Assert.Equal(["f3", "f4"], CardIds(plain));
        Assert.Equal(["f1", "f2", "f3", "f4"], CardIds(hidden));
        Assert.True(hidden.Panel!.HiddenByFilters);
        Assert.True(Card(hidden, "f1").AddedByFocus);
        Assert.Equal(CardState.Focus, Card(hidden, "f1").State);
        Assert.True(Card(hidden, "f2").AddedByFocus);
        Assert.Equal(CardState.Chain, Card(hidden, "f2").State);
        Assert.False(Card(hidden, "f3").AddedByFocus);
        Assert.Equal(CardState.Dimmed, Card(hidden, "f3").State);
        Assert.True(Line(hidden, "f1|f2").InFocus);
        Assert.Equal(["f3>f4"], RowIds(hidden));

        Assert.False(visible.Panel!.HiddenByFilters);
        Assert.DoesNotContain(Cards(visible), c => c.AddedByFocus);

        Assert.True(unlinked.Panel!.HiddenByFilters);
        var lonely = Card(unlinked, "f5");
        Assert.True(lonely.AddedByFocus);
        Assert.Equal(CardState.Focus, lonely.State);
    }

    [Fact]
    public void Focus_keeps_the_table_rows_and_adds_its_chain_lines()
    {
        var view = View(
            ThreeTeams(),
            selection: new DependencySelection(RegalLane, AxisLane),
            focus: new DependencyFocus("f4", ChainMode.Direct, null));

        Assert.Equal(["f1>f2"], RowIds(view));
        Assert.Equal(["f1|f2", "f3|f4", "f4|f5"], view.Board.Lines.Select(l => l.Key));
        Assert.Equal(["f3|f4", "f4|f5"], view.FocusLineKeys.Order(StringComparer.Ordinal));
        Assert.True(Card(view, "f3").AddedByFocus);
        Assert.True(Card(view, "f5").AddedByFocus);
        Assert.Equal(CardState.Dimmed, Card(view, "f1").State);
    }

    [Fact]
    public void Focus_line_keys_are_the_lines_of_the_chain()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3), Pay(4)],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-3", "PAY-4")]);

        var focused = View(index, focus: new DependencyFocus("f1", ChainMode.Direct, null));
        var plain = View(index);

        Assert.Equal(["f1|f2"], focused.FocusLineKeys.Order(StringComparer.Ordinal));
        Assert.True(Line(focused, "f1|f2").InFocus);
        Assert.False(Line(focused, "f3|f4").InFocus);
        Assert.Empty(plain.FocusLineKeys);
        Assert.Empty(plain.FocusNodeIds);
        Assert.Null(plain.Panel);
        Assert.All(plain.Board.Lines, l => Assert.False(l.InFocus));
    }

    [Fact]
    public void Focus_on_a_stub_or_unknown_node_is_ignored()
    {
        var index = Index(
            [Pay(1), Pay(2)],
            [Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-1", "BO-7")],
            [Stub("BO-7", DependencyStubKind.BusinessOutcome)]);

        foreach (var nodeId in new[] { "k:BO-7", "f99", "PAY-1" })
        {
            var view = View(index, focus: new DependencyFocus(nodeId, ChainMode.WholeChain, null));

            Assert.Null(view.Panel);
            Assert.Empty(view.FocusNodeIds);
            Assert.Empty(view.FocusLineKeys);
            Assert.All(Cards(view), c => Assert.Equal(CardState.Normal, c.State));
        }
    }

    [Fact]
    public void Lane_counters_count_needs_at_risk_and_risk_for_others()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 3)),
                Pay(2, Team(Regal, true, false, 1)),
                Pay(3, Team(Axis, true, false, 2)),
                Pay(4, Team(Axis, true, false, 2)),
                Pay(5, Team(Axis, true, false, 1)),
                Pay(6, Team(Regal, true, false, 4)),
                Pay(7, Team(Regal, true, false, 1)),
                Pay(8, Team(Axis, true, false))
            ],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Blocks("502", "PAY-3", "PAY-4"),
                Blocks("503", "PAY-5", "PAY-6"),
                Blocks("504", "PAY-7", "PAY-8")
            ]);

        var lanes = View(index).Board.Lanes.ToDictionary(l => l.Lane.LaneId);

        Assert.Equal((1, 1), (lanes[AxisLane].NeedsAtRisk, lanes[AxisLane].AtRiskForOthers));
        Assert.Equal((1, 0), (lanes[RegalLane].NeedsAtRisk, lanes[RegalLane].AtRiskForOthers));
    }

    [Fact]
    public void Card_counters_count_shown_needs_needed_by_related_and_worst_risk()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Regal, true, false, 1)),
                Pay(4, Team(Bravo, true, false)),
                Pay(5, Team(Axis, true, false, 1)),
                Pay(6, Team(Bravo, true, false, 2))
            ],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Blocks("502", "PAY-2", "PAY-3"),
                Blocks("503", "PAY-1", "PAY-4"),
                Blocks("504", "PAY-5", "PAY-1"),
                Relates("505", "PAY-1", "PAY-6")
            ]);

        var view = View(index);
        var first = Card(view, "f1");
        var second = Card(view, "f2");
        var narrowed = Card(View(index, selection: new DependencySelection(RegalLane, null)), "f1");

        Assert.Equal((1, 2, 1), (first.Needs, first.NeededBy, first.Related));
        Assert.Equal(DependencyRisk.AtRisk, first.WorstRisk);
        Assert.Equal((1, 1, 0), (second.Needs, second.NeededBy, second.Related));
        Assert.Equal(DependencyRisk.OutOfOrder, second.WorstRisk);
        Assert.Equal((0, 1, 0), (narrowed.Needs, narrowed.NeededBy, narrowed.Related));
        Assert.Equal(DependencyRisk.InOrder, narrowed.WorstRisk);
    }

    [Fact]
    public void Card_shows_scope_status_problem_and_free_text()
    {
        var index = Index(
            [
                PayWith(1, "Cancelled", Team(Axis, true, false, 1)) with { FreeTextDependencies = "Needs the vendor API", ExternalDependencies = true },
                Pay(2, Team(Axis, true, false, 3)),
                EarlierPay(50),
                EarlierPay(51).WithoutPi(),
                Crd(9).InLaterPi(),
                Crd(10).WithoutArt(),
                Crd(11).WithoutArt().WithoutPi(),
                Crd(12)
            ],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Blocks("502", "PAY-50", "PAY-2"),
                Blocks("503", "PAY-51", "PAY-2"),
                Blocks("504", "CRD-9", "PAY-2"),
                Blocks("505", "CRD-10", "PAY-2"),
                Blocks("506", "CRD-11", "PAY-2"),
                Blocks("507", "CRD-12", "PAY-2")
            ]);

        var view = View(index);

        var rejected = Card(view, "f1");
        Assert.True(rejected.StatusProblem);
        Assert.Equal("Needs the vendor API", rejected.FreeText);
        Assert.True(rejected.ExternalDependencies);
        Assert.Null(rejected.ScopeText);
        Assert.False(Card(view, "f2").StatusProblem);
        Assert.Equal(EarlierPiName, Card(view, "f50").ScopeText);
        Assert.Equal("No PI", Card(view, "f51").ScopeText);
        Assert.DoesNotContain(view.Board.Lanes.SelectMany(l => l.Cards), c => c.ScopeText?.Contains("PI PI") == true);
        Assert.Equal($"ART {OtherArtName} · {LaterPiName}", Card(view, "f9").ScopeText);
        Assert.Equal("No ART", Card(view, "f10").ScopeText);
        Assert.Equal("No ART", Card(view, "f11").ScopeText);
        Assert.Equal($"ART {OtherArtName}", Card(view, "f12").ScopeText);
        Assert.Equal("No PI", Card(view, "f11").SprintTooltip);
        Assert.Equal(DependencyEndKind.OtherArt, Card(view, "f12").Kind);
    }

    [Fact]
    public void Sprint_tooltip_lists_every_team_with_dates_and_the_approximate_note()
    {
        var index = Index(
            [
                Pay(
                    1,
                    Team(Axis, true, false, 2, 3),
                    Team(Regal, false, true),
                    TeamWithSprints(Bravo, false, false, null, Sprint(9001, new DateTime(2026, 1, 26), new DateTime(2026, 2, 8), null, "Bravo 3")))
            ],
            []);

        var card = Card(View(index, Unlinked()), "f1");

        Assert.True(card.Approximate);
        Assert.Equal(
            "Axis ★: Sprint 2, Sprint 3 (19 Jan–15 Feb)\nRegal: not planned · below the line\nBravo: Bravo 3 (26 Jan–08 Feb)\n≈ the team's sprint dates do not line up with the ART sprints",
            card.SprintTooltip);
        Assert.Equal("Axis ★: S2–S3\nRegal: not planned · below the line\nBravo: S2–S3", card.TeamsText);
    }

    [Fact]
    public void Details_panel_splits_needs_needed_by_related_and_other_links()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 1)),
                Pay(2, Team(Regal, true, false, 3)),
                Pay(3, Team(Bravo, true, false, 4)),
                Pay(4),
                Pay(5),
                Pay(6)
            ],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                Blocks("502", "PAY-2", "PAY-3"),
                Relates("503", "PAY-2", "PAY-4"),
                Blocks("504", "PAY-2", "BO-7"),
                Blocks("505", "PAY-5", "PAY-6")
            ],
            [Stub("BO-7", DependencyStubKind.BusinessOutcome, "Grow revenue", "Open")]);

        var panel = View(index, focus: new DependencyFocus("f2", ChainMode.Direct, null)).Panel!;

        Assert.Equal("f2", panel.NodeId);
        Assert.Equal("PAY-2", panel.Feature.JiraId);
        Assert.Null(panel.ScopeText);

        var needs = Assert.Single(panel.Needs);
        Assert.Equal(("f1>f2", "f1", "PAY-1"), (needs.DependencyId, needs.OtherNodeId, needs.OtherKey));
        Assert.Equal("Feature 1", needs.OtherTitle);
        Assert.Equal("Axis", needs.OtherTeams);
        Assert.Equal("S1", needs.OtherPlan);
        Assert.Equal(DependencyRisk.InOrder, needs.Risk);
        Assert.True(needs.OtherIsCard);
        Assert.False(needs.HiddenByFilters);

        var neededBy = Assert.Single(panel.NeededBy);
        Assert.Equal("f2>f3", neededBy.DependencyId);
        Assert.Equal("Bravo", neededBy.OtherTeams);

        var related = Assert.Single(panel.Related);
        Assert.Equal("f2~f4", related.DependencyId);
        Assert.Equal("(No team)", related.OtherTeams);
        Assert.Equal("Not planned", related.OtherPlan);
        Assert.Equal(DependencyRisk.NotChecked, related.Risk);

        var other = Assert.Single(panel.OtherLinks);
        Assert.Equal(("f2>k:BO-7", "k:BO-7", "BO-7"), (other.DependencyId, other.OtherNodeId, other.OtherKey));
        Assert.Equal("Grow revenue", other.OtherTitle);
        Assert.Equal("Open", other.OtherStatus);
        Assert.Equal("Business outcome", other.OtherPlan);
        Assert.Equal("—", other.OtherTeams);
        Assert.False(other.OtherIsCard);
        Assert.True(other.HiddenByFilters);
        Assert.Equal("blocks", other.Phrase);
    }

    [Fact]
    public void Details_rows_read_the_phrase_from_the_focused_side_and_mark_hidden_rows()
    {
        var index = Index(
            [Pay(1), Pay(2), Pay(3), Pay(4)],
            [
                Blocks("501", "PAY-1", "PAY-2"),
                DependsOn("502", "PAY-3", "PAY-2"),
                Blocks("503", "PAY-2", "PAY-3"),
                Relates("504", "PAY-4", "PAY-2")
            ]);

        var panel = View(
            index,
            new DependencyFilter { LinkTypes = ["Dependency"] },
            focus: new DependencyFocus("f2", ChainMode.Direct, "f2>f3")).Panel!;

        var needs = Assert.Single(panel.Needs);
        Assert.Equal("is blocked by", needs.Phrase);
        Assert.True(needs.HiddenByFilters);
        Assert.False(needs.Selected);

        var neededBy = Assert.Single(panel.NeededBy);
        Assert.Equal("is depended on by", neededBy.Phrase);
        Assert.False(neededBy.HiddenByFilters);
        Assert.True(neededBy.Selected);

        var related = Assert.Single(panel.Related);
        Assert.Equal("relates to", related.Phrase);
        Assert.True(related.HiddenByFilters);
        Assert.False(related.Selected);
    }

    [Fact]
    public void Details_rows_are_sorted_riskiest_first_then_by_key()
    {
        var index = Index(
            [
                Pay(1, Team(Axis, true, false, 4)),
                Pay(2, Team(Axis, true, false, 1)),
                Pay(3, Team(Axis, true, false, 1)),
                Pay(9, Team(Regal, true, false, 3))
            ],
            [Blocks("501", "PAY-3", "PAY-9"), Blocks("502", "PAY-1", "PAY-9"), Blocks("503", "PAY-2", "PAY-9")]);

        var panel = View(index, focus: new DependencyFocus("f9", ChainMode.Direct, null)).Panel!;

        Assert.Equal(["PAY-1", "PAY-2", "PAY-3"], panel.Needs.Select(r => r.OtherKey));
        Assert.Equal([DependencyRisk.OutOfOrder, DependencyRisk.InOrder, DependencyRisk.InOrder], panel.Needs.Select(r => r.Risk));
    }

    [Fact]
    public void Details_plan_lists_every_team_with_sprints_below_line_and_approximate()
    {
        var index = Index(
            [
                Pay(
                    1,
                    Team(Axis, true, false, 2, 3),
                    Team(Regal, false, true),
                    TeamWithSprints(Bravo, false, false, null, Sprint(9001, new DateTime(2026, 1, 26), new DateTime(2026, 2, 8), null, "Bravo 3")))
            ],
            []);

        var panel = View(index, focus: new DependencyFocus("f1", ChainMode.Direct, null)).Panel!;

        Assert.Equal(
            [
                new DetailsTeamPlan("Axis", true, "Sprint 2, Sprint 3 (19 Jan–15 Feb)", false, false),
                new DetailsTeamPlan("Regal", false, "not planned", true, false),
                new DetailsTeamPlan("Bravo", false, "Bravo 3 (26 Jan–08 Feb)", false, true)
            ],
            panel.Plan);
        Assert.Empty(panel.Needs);
        Assert.Empty(panel.NeededBy);
        Assert.Empty(panel.Related);
        Assert.Empty(panel.OtherLinks);
    }

    [Fact]
    public void Build_is_deterministic_whatever_the_input_order()
    {
        var features = new List<DependencyFeature>
        {
            Pay(1, Team(Axis, true, false, 1)),
            Pay(2, Team(Regal, true, false, 3)),
            Pay(3, Team(Axis, true, false, 1)),
            Pay(4, Team(Bravo, true, false, 2)),
            Pay(5, Team(Regal, true, false, 1)),
            Pay(6)
        };
        var links = new List<DependencyLinkRow>
        {
            Blocks("501", "PAY-1", "PAY-2"),
            Blocks("502", "PAY-3", "PAY-2"),
            Relates("503", "PAY-4", "PAY-5"),
            DependsOn("504", "PAY-6", "PAY-5"),
            Blocks("505", "PAY-2", "PAY-4")
        };

        var forward = View(Index(features, links), Unlinked());
        var backward = View(Index(features.AsEnumerable().Reverse().ToList(), links.AsEnumerable().Reverse().ToList()), Unlinked());

        Assert.Equal(forward.Board.LinesVersion, backward.Board.LinesVersion);
        Assert.Equal(Describe(forward), Describe(backward));
        Assert.Equal(forward.Rows.Select(r => r.Dependency.Id), backward.Rows.Select(r => r.Dependency.Id));

        static List<string> Describe(DependencyMapView view) =>
            view.Board.Lanes
                .SelectMany(l => l.Cards.Select(c => $"{l.Lane.LaneId}:{c.NodeId}:{c.ColumnStart}-{c.ColumnEnd}:{c.Row}"))
                .Concat(view.Board.Lines.Select(l => $"{l.Key}:{l.FromNodeId}>{l.ToNodeId}:{l.Title}"))
                .ToList();
    }

    [Fact]
    public void Build_is_fast_for_300_links()
    {
        var teamIds = new[] { Axis, Regal, Bravo, 13, 14, 15 };
        var features = Enumerable.Range(1, 150)
            .Select(i => i % 4 == 0
                ? Pay(i, Team(teamIds[i % 6], true, false, (i % 5) + 1), Team(teamIds[(i + 1) % 6], false, false, ((i + 2) % 5) + 1))
                : Pay(i, Team(teamIds[i % 6], true, false, (i % 5) + 1)))
            .ToList();
        var links = Enumerable.Range(1, 150)
            .SelectMany(a => new[]
            {
                Blocks($"{1000 + a}", $"PAY-{a}", $"PAY-{((a * 7) % 150) + 1}"),
                a % 5 == 0
                    ? Relates($"{2000 + a}", $"PAY-{a}", $"PAY-{((a * 13) + 5) % 150 + 1}")
                    : Blocks($"{2000 + a}", $"PAY-{a}", $"PAY-{((a * 13) + 5) % 150 + 1}")
            })
            .ToList();
        var index = Index(features, links, teams: teamIds.Select(id => TeamInfo(id)).ToList());
        var focus = new DependencyFocus("f1", ChainMode.WholeChain, null);

        View(index, Unlinked(), focus: focus);
        var best = TimeSpan.MaxValue;

        for (var run = 0; run < 5; run++)
        {
            var watch = Stopwatch.StartNew();
            var view = View(index, Unlinked(), focus: focus);
            watch.Stop();
            Assert.NotEmpty(view.Board.Lines);
            best = watch.Elapsed < best ? watch.Elapsed : best;
        }

        Assert.True(index.Dependencies.Count > 250, $"{index.Dependencies.Count} dependencies");
        Assert.True(best < TimeSpan.FromMilliseconds(250), $"Build took {best.TotalMilliseconds} ms");
    }
}
