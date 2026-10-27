using Estimation.Core.DepartmentPlanning.Services;

namespace Estimation.Components.DepartmentPlanning;

public readonly record struct PlanningGridSnapshot(
    PlanningTree Tree,
    PlanningPivot Pivot,
    IReadOnlySet<string> Expanded,
    IReadOnlySet<string> Columns,
    string? SelectedId,
    bool TeamColumns,
    bool Dense,
    int Zoom,
    PlanningSort SortBy,
    bool SortDescending,
    bool FullHeight,
    string EmptyText)
{
    public bool DrawsSameAs(PlanningGridSnapshot other) =>
        ReferenceEquals(Tree, other.Tree)
        && ReferenceEquals(Pivot, other.Pivot)
        && ReferenceEquals(Expanded, other.Expanded)
        && ReferenceEquals(Columns, other.Columns)
        && SelectedId == other.SelectedId
        && TeamColumns == other.TeamColumns
        && Dense == other.Dense
        && Zoom == other.Zoom
        && SortBy == other.SortBy
        && SortDescending == other.SortDescending
        && FullHeight == other.FullHeight
        && EmptyText == other.EmptyText;
}

public readonly record struct PlanningGridRowSnapshot(
    PlanningPivotLine Line,
    bool Selected,
    bool Expanded,
    IReadOnlyList<PlanningColumn> Columns,
    IReadOnlyList<PlanningLevel> AncestorLevels,
    bool TeamColumns,
    PlanningTree Tree)
{
    public bool DrawsSameAs(PlanningGridRowSnapshot other) =>
        ReferenceEquals(Line.Row, other.Line.Row)
        && Line.Indent == other.Line.Indent
        && Selected == other.Selected
        && Expanded == other.Expanded
        && TeamColumns == other.TeamColumns
        && ReferenceEquals(Tree, other.Tree)
        && Columns.SequenceEqual(other.Columns)
        && AncestorLevels.SequenceEqual(other.AncestorLevels)
        && SameAncestors(other);

    private bool SameAncestors(PlanningGridRowSnapshot other)
    {
        if (Line.Indent != 0 || ReferenceEquals(Line.Root, other.Line.Root))
        {
            return true;
        }

        foreach (var level in AncestorLevels)
        {
            if (!ReferenceEquals(Line.Root.Ancestors.GetValueOrDefault(level), other.Line.Root.Ancestors.GetValueOrDefault(level)))
            {
                return false;
            }
        }

        return true;
    }
}
