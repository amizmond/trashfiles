using Estimation.Core.DepartmentPlanning.Services;
using Microsoft.AspNetCore.Components;

namespace Estimation.Components.DepartmentPlanning.Views;

public abstract class PlanningViewBase : ComponentBase
{
    [Parameter, EditorRequired] public PlanningTree Tree { get; set; } = PlanningTree.Empty;

    [Parameter, EditorRequired] public DepartmentPlanningData Data { get; set; } = null!;

    [Parameter] public PlanningOptions Options { get; set; } = new();

    [Parameter] public string? SelectedId { get; set; }

    [Parameter] public EventCallback<string> OnSelect { get; set; }

    [Parameter] public string? JiraTaskUrl { get; set; }

    [Parameter] public string? PiName { get; set; }

    protected Task SelectAsync(string rowId) => OnSelect.InvokeAsync(rowId);

    protected bool IsSelected(PlanningRow row) => row.Id == SelectedId;
}
