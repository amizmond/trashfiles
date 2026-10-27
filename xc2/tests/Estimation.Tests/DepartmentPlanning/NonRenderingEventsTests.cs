using Estimation.Components.DepartmentPlanning;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class NonRenderingEventsTests
{
    private sealed class FullScreenHost : ComponentBase
    {
        public List<string> Keys { get; } = [];

        public void OnKey(KeyboardEventArgs e) => Keys.Add(e.Key);
    }

    [Fact]
    public async Task The_handler_runs_without_asking_its_component_to_render()
    {
        var host = new FullScreenHost();
        var rendering = EventCallback.Factory.Create<KeyboardEventArgs>(host, host.OnKey);
        var silent = NonRenderingEvents.Create<KeyboardEventArgs>(host.OnKey);

        await Assert.ThrowsAsync<InvalidOperationException>(() => rendering.InvokeAsync(new KeyboardEventArgs { Key = "a" }));
        await silent.InvokeAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal(new[] { "a", "ArrowDown" }, host.Keys);
    }

    [Fact]
    public void Callbacks_made_from_the_same_method_on_each_render_are_equal()
    {
        var host = new FullScreenHost();

        Assert.Equal(NonRenderingEvents.Create<KeyboardEventArgs>(host.OnKey), NonRenderingEvents.Create<KeyboardEventArgs>(host.OnKey));
        Assert.NotEqual(NonRenderingEvents.Create<KeyboardEventArgs>(host.OnKey), NonRenderingEvents.Create<KeyboardEventArgs>(new FullScreenHost().OnKey));
    }
}
