using Estimation.Core.JiraIntegration.Models;
using MudBlazor;

namespace Estimation.Components.Features;

public static class RagStatusColors
{
    public static Color For(string? ragStatus) => RagStatuses.Normalize(ragStatus) switch
    {
        RagStatuses.Green => Color.Success,
        RagStatuses.Amber => Color.Warning,
        RagStatuses.Red => Color.Error,
        _ => Color.Default
    };
}
