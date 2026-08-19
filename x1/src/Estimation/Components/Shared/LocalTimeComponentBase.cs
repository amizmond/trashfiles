using Estimation.Services.Shared;
using Microsoft.AspNetCore.Components;

namespace Estimation.Components.Shared;

public abstract class LocalTimeComponentBase : ComponentBase
{
    [Inject]
    protected IUserTimeZoneService UserTime { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !UserTime.IsResolved)
        {
            await UserTime.EnsureResolvedAsync();
            StateHasChanged();
        }
    }
}
