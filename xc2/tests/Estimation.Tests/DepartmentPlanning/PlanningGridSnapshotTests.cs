using Estimation.Components.DepartmentPlanning;
using Estimation.Core.DepartmentPlanning.Services;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningGridSnapshotTests
{
    private static readonly IReadOnlyList<PlanningColumn> DefaultColumns =
        PlanningColumns.For(PlanningLens.Objectives).Where(c => c.DefaultVisible).ToList();

    private static PlanningRow Row(string id, PlanningLevel level, int depth, params PlanningRow[] children) =>
        new() { Id = id, Level = level, Title = id, Depth = depth, Children = children };

    private static PlanningTree Tree() => new(
        PlanningLens.Objectives,
        [
            Row("so1", PlanningLevel.StrategicObjective, 0,
                Row("so1/pe1", PlanningLevel.PortfolioEpic, 1,
                    Row("so1/pe1/bo1", PlanningLevel.BusinessOutcome, 2,
                        Row("so1/pe1/bo1/f1", PlanningLevel.BusinessEpic, 3),
                        Row("so1/pe1/bo1/f2", PlanningLevel.BusinessEpic, 3))),
                Row("so1/pe2", PlanningLevel.PortfolioEpic, 1)),
            Row("so2", PlanningLevel.StrategicObjective, 0,
                Row("so2/pe3", PlanningLevel.PortfolioEpic, 1)),
        ],
        PlanningMetrics.Empty, 0m, 0d, [], [], 0, 0);

    private static Dictionary<string, PlanningGridRowSnapshot> Rows(
        PlanningTree tree,
        PlanningPivot pivot,
        IReadOnlySet<string> expanded,
        string? selectedId = null,
        IReadOnlyList<PlanningColumn>? columns = null,
        bool teamColumns = false) =>
        pivot.Flatten(r => expanded.Contains(r.Id)).ToDictionary(
            line => line.Row.Id,
            line => new PlanningGridRowSnapshot(
                line,
                line.Row.Id == selectedId,
                expanded.Contains(line.Row.Id),
                columns ?? DefaultColumns,
                pivot.AncestorLevels,
                teamColumns,
                tree));

    private static List<string> Redrawn(Dictionary<string, PlanningGridRowSnapshot> before, Dictionary<string, PlanningGridRowSnapshot> after) =>
        after.Where(row => !before.TryGetValue(row.Key, out var previous) || !row.Value.DrawsSameAs(previous))
            .Select(row => row.Key)
            .ToList();

    private static PlanningGridSnapshot Grid(PlanningTree tree, PlanningPivot pivot, IReadOnlySet<string> expanded, IReadOnlySet<string> columns) =>
        new(tree, pivot, expanded, columns, "so1", false, true, 100, PlanningSort.Priority, false, false, "Nothing to show.");

    [Fact]
    public void Selecting_another_row_redraws_only_the_old_and_the_new_selection()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 0);
        var expanded = new HashSet<string> { "so1", "so1/pe1" };

        var redrawn = Redrawn(Rows(tree, pivot, expanded, "so1/pe1"), Rows(tree, pivot, expanded, "so1/pe2"));

        Assert.Equal(new[] { "so1/pe1", "so1/pe2" }, redrawn.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Expanding_a_row_redraws_that_row_and_adds_its_children_only()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 0);
        var before = Rows(tree, pivot, new HashSet<string> { "so1" });
        var after = Rows(tree, pivot, new HashSet<string> { "so1", "so1/pe1" });

        Assert.Equal(new[] { "so1/pe1", "so1/pe1/bo1" }, Redrawn(before, after).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Collapsing_a_row_redraws_only_that_row()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 0);
        var before = Rows(tree, pivot, new HashSet<string> { "so1", "so1/pe1" });
        var after = Rows(tree, pivot, new HashSet<string> { "so1" });

        Assert.Equal(new[] { "so1/pe1" }, Redrawn(before, after));
        Assert.DoesNotContain("so1/pe1/bo1", after.Keys);
    }

    [Fact]
    public void Lines_flattened_again_with_an_equal_columns_list_redraw_nothing()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 0);
        var expanded = new HashSet<string> { "so1" };

        var redrawn = Redrawn(Rows(tree, pivot, expanded, "so1"), Rows(tree, pivot, expanded, "so1", DefaultColumns.ToList()));

        Assert.Empty(redrawn);
    }

    [Fact]
    public void Showing_a_column_or_the_team_columns_redraws_every_row()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 0);
        var expanded = new HashSet<string> { "so1" };
        var before = Rows(tree, pivot, expanded);
        var withFunding = DefaultColumns.Append(PlanningColumns.All.Single(c => c.Key == PlanningColumns.Funding)).ToList();

        Assert.Equal(before.Count, Redrawn(before, Rows(tree, pivot, expanded, columns: withFunding)).Count);
        Assert.Equal(before.Count, Redrawn(before, Rows(tree, pivot, expanded, teamColumns: true)).Count);
    }

    [Fact]
    public void A_rebuilt_tree_redraws_rows_even_with_the_same_ids()
    {
        var tree = Tree();
        var rebuilt = Tree();
        var expanded = new HashSet<string> { "so1" };

        var redrawn = Redrawn(Rows(tree, PlanningPivot.Build(tree, 0), expanded), Rows(rebuilt, PlanningPivot.Build(rebuilt, 0), expanded));

        Assert.Equal(new[] { "so1", "so1/pe1", "so1/pe2", "so2" }, redrawn.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_top_row_redraws_when_the_parent_it_shows_in_a_column_changes()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 1);
        var line = pivot.Flatten(_ => false).First(l => l.Row.Id == "so1/pe1");
        var otherParent = Row("so9", PlanningLevel.StrategicObjective, 0);
        var moved = line with { Root = line.Root with { Ancestors = new Dictionary<PlanningLevel, PlanningRow> { [PlanningLevel.StrategicObjective] = otherParent } } };
        var sameParent = line with { Root = line.Root with { Ancestors = new Dictionary<PlanningLevel, PlanningRow>(line.Root.Ancestors) } };

        var shown = Snapshot(line, pivot, tree);

        Assert.Equal(new[] { PlanningLevel.StrategicObjective }, pivot.AncestorLevels);
        Assert.False(Snapshot(moved, pivot, tree).DrawsSameAs(shown));
        Assert.True(Snapshot(sameParent, pivot, tree).DrawsSameAs(shown));
    }

    [Fact]
    public void An_indented_row_ignores_its_root_s_parents_because_it_leaves_those_cells_empty()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 1);
        var line = pivot.Flatten(_ => true).First(l => l.Row.Id == "so1/pe1/bo1");
        var otherParent = Row("so9", PlanningLevel.StrategicObjective, 0);
        var moved = line with { Root = line.Root with { Ancestors = new Dictionary<PlanningLevel, PlanningRow> { [PlanningLevel.StrategicObjective] = otherParent } } };

        Assert.Equal(1, line.Indent);
        Assert.True(Snapshot(moved, pivot, tree).DrawsSameAs(Snapshot(line, pivot, tree)));
    }

    [Fact]
    public void A_row_redraws_when_the_parent_columns_change()
    {
        var tree = Tree();
        var line = PlanningPivot.Build(tree, 0).Flatten(_ => false).First();
        var shown = new PlanningGridRowSnapshot(line, false, false, DefaultColumns, [], false, tree);

        Assert.False(shown.DrawsSameAs(shown with { AncestorLevels = [PlanningLevel.StrategicObjective] }));
        Assert.False(shown.DrawsSameAs(shown with { Line = line with { Indent = 1 } }));
        Assert.False(shown.DrawsSameAs(shown with { Tree = Tree() }));
        Assert.True(shown.DrawsSameAs(shown with { AncestorLevels = new List<PlanningLevel>() }));
    }

    [Fact]
    public void The_grid_skips_a_page_render_that_changes_none_of_its_parameters()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 0);
        var expanded = new HashSet<string> { "so1" };
        var columns = PlanningColumns.Defaults();

        Assert.True(Grid(tree, pivot, expanded, columns).DrawsSameAs(Grid(tree, pivot, expanded, columns)));
    }

    [Fact]
    public void The_grid_redraws_for_zoom_selection_full_screen_and_sort()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 0);
        var shown = Grid(tree, pivot, new HashSet<string>(), PlanningColumns.Defaults());

        Assert.False(shown.DrawsSameAs(shown with { Zoom = 110 }));
        Assert.False(shown.DrawsSameAs(shown with { SelectedId = "so2" }));
        Assert.False(shown.DrawsSameAs(shown with { FullHeight = true }));
        Assert.False(shown.DrawsSameAs(shown with { SortBy = PlanningSort.Demand }));
        Assert.False(shown.DrawsSameAs(shown with { SortDescending = true }));
        Assert.False(shown.DrawsSameAs(shown with { TeamColumns = true }));
        Assert.False(shown.DrawsSameAs(shown with { EmptyText = "No rows match the search and filters." }));
    }

    [Fact]
    public void The_grid_notices_new_expanded_and_column_sets_by_instance()
    {
        var tree = Tree();
        var pivot = PlanningPivot.Build(tree, 0);
        var expanded = new HashSet<string> { "so1" };
        var columns = PlanningColumns.Defaults();
        var shown = Grid(tree, pivot, expanded, columns);

        Assert.False(shown.DrawsSameAs(shown with { Expanded = new HashSet<string>(expanded) }));
        Assert.False(shown.DrawsSameAs(shown with { Columns = PlanningColumns.Defaults() }));
        Assert.False(shown.DrawsSameAs(shown with { Pivot = PlanningPivot.Build(tree, 0) }));
        Assert.False(shown.DrawsSameAs(shown with { Tree = Tree() }));
    }

    private static PlanningGridRowSnapshot Snapshot(PlanningPivotLine line, PlanningPivot pivot, PlanningTree tree) =>
        new(line, false, false, DefaultColumns, pivot.AncestorLevels, false, tree);
}
