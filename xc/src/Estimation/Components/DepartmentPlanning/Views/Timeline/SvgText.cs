using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Estimation.Components.DepartmentPlanning.Views;

public sealed class SvgText : ComponentBase
{
    [Parameter] public string? Class { get; set; }

    [Parameter] public string? X { get; set; }

    [Parameter] public string? Y { get; set; }

    [Parameter] public string? Dx { get; set; }

    [Parameter] public string? TextAnchor { get; set; }

    [Parameter] public string? Text { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "class", Class);
        builder.AddAttribute(2, "x", X);
        builder.AddAttribute(3, "y", Y);
        builder.AddAttribute(4, "dx", Dx);
        builder.AddAttribute(5, "text-anchor", TextAnchor);
        builder.AddContent(6, Text);
        builder.CloseElement();
    }
}
