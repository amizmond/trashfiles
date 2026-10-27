using Estimation.Components.DepartmentPlanning.Views;
using Estimation.Core.DepartmentPlanning.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public sealed class PlanningViewBaseTests : IAsyncDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly HtmlRenderer _renderer;

    public PlanningViewBaseTests() => _renderer = new HtmlRenderer(_services, NullLoggerFactory.Instance);

    public async ValueTask DisposeAsync()
    {
        await _renderer.DisposeAsync();
        await _services.DisposeAsync();
    }

    private sealed class ProbeView : PlanningViewBase
    {
        public int Renders { get; private set; }

        public bool AskToRender { get; set; }

        public Task RenderOwnStateAsync() => InvokeAsync(StateHasChanged);

        public Func<Task> Details(string rowId) => DetailsHandler(rowId);

        public Func<Task> Select(string rowId) => SelectHandler(rowId);

        public EventCallback<KeyboardEventArgs> DetailsKey(string rowId) => DetailsKeyHandler(rowId);

        protected override void OnParametersSet()
        {
            if (AskToRender)
            {
                AskToRender = false;
                RequestRender();
            }
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => Renders++;
    }

    private sealed class PageStandIn : ComponentBase
    {
        private DynamicComponent? _view;

        [Parameter] public Action<PageStandIn>? Created { get; set; }

        public PlanningTree Tree { get; set; } = PlanningTree.Empty with { };

        public DepartmentPlanningData Data { get; set; } = NewData();

        public PlanningOptions Options { get; set; } = new();

        public string? SelectedId { get; set; }

        public List<string> Details { get; } = [];

        public ProbeView View => (ProbeView)_view!.Instance!;

        public Task RenderAgainAsync() => InvokeAsync(StateHasChanged);

        protected override void OnInitialized() => Created?.Invoke(this);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<DynamicComponent>(0);
            builder.AddComponentParameter(1, nameof(DynamicComponent.Type), typeof(ProbeView));
            builder.AddComponentParameter(2, nameof(DynamicComponent.Parameters), new Dictionary<string, object?>
            {
                [nameof(PlanningViewBase.Tree)] = Tree,
                [nameof(PlanningViewBase.Data)] = Data,
                [nameof(PlanningViewBase.Options)] = Options,
                [nameof(PlanningViewBase.SelectedId)] = SelectedId,
                [nameof(PlanningViewBase.OnSelect)] = EventCallback.Factory.Create<string>(this, SelectAsync),
                [nameof(PlanningViewBase.OnShowDetails)] = EventCallback.Factory.Create<string>(this, ShowDetailsAsync),
                [nameof(PlanningViewBase.JiraTaskUrl)] = "https://jira/browse/",
                [nameof(PlanningViewBase.PiName)] = Data.PiName,
            });
            builder.AddComponentReferenceCapture(3, view => _view = (DynamicComponent)view);
            builder.CloseComponent();
        }

        private Task SelectAsync(string id)
        {
            SelectedId = id;
            return Task.CompletedTask;
        }

        private Task ShowDetailsAsync(string id)
        {
            SelectedId = id;
            Details.Add(id);
            return Task.CompletedTask;
        }
    }

    private static DepartmentPlanningData NewData() =>
        new(new DepartmentPlanningScope(1, [10], 3), 3, "PI 26.09", false, null, null, [], [], [], [], [], []);

    private async Task<PageStandIn> MountAsync()
    {
        PageStandIn? page = null;
        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(PageStandIn.Created)] = (Action<PageStandIn>)(created => page = created),
        });
        await _renderer.Dispatcher.InvokeAsync(() => _renderer.RenderComponentAsync<PageStandIn>(parameters));
        return page!;
    }

    private Task OnDispatcherAsync(Func<Task> work) => _renderer.Dispatcher.InvokeAsync(work);

    [Fact]
    public async Task A_page_render_with_the_same_inputs_does_not_render_the_view_again()
    {
        var page = await MountAsync();
        Assert.Equal(1, page.View.Renders);

        await page.RenderAgainAsync();
        await page.RenderAgainAsync();

        Assert.Equal(1, page.View.Renders);
    }

    [Fact]
    public async Task A_new_selection_tree_options_or_data_renders_the_view()
    {
        var page = await MountAsync();

        page.SelectedId = "so:1";
        await page.RenderAgainAsync();
        Assert.Equal(2, page.View.Renders);

        page.Tree = page.Tree with { };
        await page.RenderAgainAsync();
        Assert.Equal(3, page.View.Renders);

        page.Options = page.Options with { };
        await page.RenderAgainAsync();
        Assert.Equal(4, page.View.Renders);

        page.Data = NewData() with { PiName = "PI 26.10" };
        await page.RenderAgainAsync();
        Assert.Equal(5, page.View.Renders);

        await page.RenderAgainAsync();
        Assert.Equal(5, page.View.Renders);
    }

    [Fact]
    public async Task The_view_still_renders_for_its_own_changes()
    {
        var page = await MountAsync();

        await page.View.RenderOwnStateAsync();
        await page.View.RenderOwnStateAsync();

        Assert.Equal(3, page.View.Renders);
    }

    [Fact]
    public async Task A_view_can_ask_for_a_render_when_its_own_state_changed_on_unchanged_inputs()
    {
        var page = await MountAsync();

        page.View.AskToRender = true;
        await page.RenderAgainAsync();
        Assert.Equal(2, page.View.Renders);

        await page.RenderAgainAsync();
        Assert.Equal(2, page.View.Renders);
    }

    [Fact]
    public async Task Row_handlers_reach_the_page_and_render_the_view_with_the_new_selection()
    {
        var page = await MountAsync();

        await OnDispatcherAsync(page.View.Details("pe:7"));
        await OnDispatcherAsync(() => page.View.DetailsKey("pe:8").InvokeAsync(new KeyboardEventArgs { Key = "ArrowDown" }));
        await OnDispatcherAsync(() => page.View.DetailsKey("pe:9").InvokeAsync(new KeyboardEventArgs { Key = " " }));
        await OnDispatcherAsync(page.View.Select("so:2"));

        Assert.Equal(new[] { "pe:7", "pe:9" }, page.Details);
        Assert.Equal("so:2", page.SelectedId);
        Assert.Equal("so:2", page.View.SelectedId);
        Assert.Equal(4, page.View.Renders);
    }

    [Fact]
    public async Task A_key_that_does_nothing_does_not_redraw_the_view()
    {
        var page = await MountAsync();
        var renders = page.View.Renders;

        await OnDispatcherAsync(() => page.View.DetailsKey("pe:8").InvokeAsync(new KeyboardEventArgs { Key = "ArrowDown" }));

        Assert.Equal(renders, page.View.Renders);
        Assert.Empty(page.Details);
        var ordinary = EventCallback.Factory.Create<KeyboardEventArgs>(page.View, (KeyboardEventArgs _) => Task.CompletedTask);
        await OnDispatcherAsync(() => ordinary.InvokeAsync(new KeyboardEventArgs { Key = "ArrowDown" }));
        Assert.Equal(renders + 1, page.View.Renders);
    }

    [Fact]
    public async Task Row_handlers_stay_the_same_instance_across_renders_until_new_data_arrives()
    {
        var page = await MountAsync();
        var details = page.View.Details("pe:7");
        var select = page.View.Select("pe:7");
        var key = page.View.DetailsKey("pe:7");

        page.Tree = page.Tree with { };
        page.SelectedId = "pe:7";
        await page.RenderAgainAsync();

        Assert.Same(details, page.View.Details("pe:7"));
        Assert.Same(select, page.View.Select("pe:7"));
        Assert.Equal(key, page.View.DetailsKey("pe:7"));
        Assert.NotSame(details, page.View.Details("pe:8"));

        page.Data = NewData();
        await page.RenderAgainAsync();

        Assert.NotSame(details, page.View.Details("pe:7"));
        Assert.NotSame(select, page.View.Select("pe:7"));
        Assert.NotEqual(key, page.View.DetailsKey("pe:7"));
    }
}
