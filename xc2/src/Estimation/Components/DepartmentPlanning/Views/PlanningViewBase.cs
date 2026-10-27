using Estimation.Core.DepartmentPlanning.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Estimation.Components.DepartmentPlanning.Views;

public abstract class PlanningViewBase : ComponentBase
{
    private readonly PlanningHandlerCache<string, Func<Task>> _selectHandlers;
    private readonly PlanningHandlerCache<string, Func<Task>> _detailsHandlers;
    private readonly PlanningHandlerCache<string, Func<KeyboardEventArgs, Task>> _detailsKeyHandlers;
    private PlanningViewInputs _previousInputs;
    private bool _settingParameters;
    private bool _renderRequested;

    protected PlanningViewBase()
    {
        _selectHandlers = new(id => () => SelectAsync(id), StringComparer.Ordinal);
        _detailsHandlers = new(id => () => ShowDetailsAsync(id), StringComparer.Ordinal);
        _detailsKeyHandlers = new(id => e => e.Key is "Enter" or " " ? ShowDetailsAsync(id) : Task.CompletedTask, StringComparer.Ordinal);
    }

    [Parameter, EditorRequired] public PlanningTree Tree { get; set; } = PlanningTree.Empty;

    [Parameter, EditorRequired] public DepartmentPlanningData Data { get; set; } = null!;

    [Parameter] public PlanningOptions Options { get; set; } = new();

    [Parameter] public string? SelectedId { get; set; }

    [Parameter] public EventCallback<string> OnSelect { get; set; }

    [Parameter] public EventCallback<string> OnShowDetails { get; set; }

    [Parameter] public string? JiraTaskUrl { get; set; }

    [Parameter] public string? PiName { get; set; }

    private PlanningViewInputs Inputs => new(Tree, Data, Options, SelectedId, JiraTaskUrl, PiName);

    public override Task SetParametersAsync(ParameterView parameters)
    {
        _previousInputs = Inputs;
        _settingParameters = true;
        try
        {
            return base.SetParametersAsync(parameters);
        }
        finally
        {
            _settingParameters = false;
            _renderRequested = false;
            if (!ReferenceEquals(_previousInputs.Data, Data))
            {
                _selectHandlers.Clear();
                _detailsHandlers.Clear();
                _detailsKeyHandlers.Clear();
            }
        }
    }

    protected override bool ShouldRender() => !_settingParameters || _renderRequested || Inputs != _previousInputs;

    protected void RequestRender() => _renderRequested = true;

    protected Task SelectAsync(string rowId) => OnSelect.InvokeAsync(rowId);

    protected Task ShowDetailsAsync(string rowId) => OnShowDetails.InvokeAsync(rowId);

    protected Func<Task> SelectHandler(string rowId) => _selectHandlers.For(rowId);

    protected Func<Task> DetailsHandler(string rowId) => _detailsHandlers.For(rowId);

    protected EventCallback<KeyboardEventArgs> DetailsKeyHandler(string rowId) => NonRenderingEvents.Create(_detailsKeyHandlers.For(rowId));

    protected bool IsSelected(PlanningRow row) => row.Id == SelectedId;
}
