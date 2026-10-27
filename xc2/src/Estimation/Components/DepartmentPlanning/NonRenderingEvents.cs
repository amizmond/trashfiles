using Microsoft.AspNetCore.Components;

namespace Estimation.Components.DepartmentPlanning;

internal sealed class NonRenderingEvents : IHandleEvent
{
    private static readonly NonRenderingEvents Receiver = new();

    private NonRenderingEvents()
    {
    }

    public static EventCallback<T> Create<T>(Action<T> handler) => new(Receiver, handler);

    public static EventCallback<T> Create<T>(Func<T, Task> handler) => new(Receiver, handler);

    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? arg) => item.InvokeAsync(arg);
}
