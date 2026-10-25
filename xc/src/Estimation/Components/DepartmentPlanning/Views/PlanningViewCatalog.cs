using Estimation.Core.DepartmentPlanning.Services;
using MudBlazor;

namespace Estimation.Components.DepartmentPlanning.Views;

public sealed record PlanningViewOption(
    string Key,
    string Title,
    string Icon,
    string Description,
    Type? ComponentType,
    PlanningLens? Lens,
    bool CanZoom = false)
{
    public bool IsGrid => ComponentType is null;
}

public static class PlanningViewCatalog
{
    public const string Grid = "grid";

    public static readonly IReadOnlyList<PlanningViewOption> All =
    [
        new(Grid, "Grid", Icons.Material.Filled.GridOn, "Excel-like tree grid: every level with its numbers, sortable columns, team columns.", null, null, CanZoom: true),
        new("treemap", "Treemap", Icons.Material.Filled.ViewQuilt, "Nested tiles sized by story points: how much of the PI each objective, epic and outcome takes, and how much of the ask does not fit.", typeof(TreemapView), PlanningLens.Objectives, CanZoom: true),
        new("waterline", "Waterline", Icons.Material.Filled.BarChart, "One column per ART filled in ranking order: what pokes out above the capacity line does not fit.", typeof(WaterlineView), PlanningLens.Drawdown, CanZoom: true),
        new("risk-quadrant", "Risk quadrant", Icons.Material.Filled.BubbleChart, "Every portfolio epic as a bubble by size and risk: the top-right quadrant is the short list for a decision.", typeof(RiskQuadrantView), PlanningLens.Objectives, CanZoom: true),
        new("timeline", "Timeline", Icons.Material.Filled.ViewTimeline, "Epics and outcomes on a calendar from target start to target end against the PI window and today.", typeof(TimelineView), PlanningLens.Objectives),
        new("flow", "Flow", Icons.Material.Filled.Mediation, "Ribbons carry planned story points from objectives into the ARTs that deliver them, with what is not planned flowing to a separate outlet.", typeof(FlowView), PlanningLens.Objectives, CanZoom: true),
    ];

    public static PlanningViewOption Find(string? key) =>
        All.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
