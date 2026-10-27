using Estimation.Components.DepartmentPlanning;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningGridKeysTests
{
    private static readonly string[] Visible = ["so:1", "so:1/pe:10", "so:1/pe:11", "so:2"];

    [Fact]
    public void A_visible_selected_row_is_found_where_it_is()
    {
        Assert.Equal(2, PlanningGridKeys.PositionOf(Visible, "so:1/pe:11"));
    }

    [Fact]
    public void A_selected_row_hidden_under_a_collapsed_parent_continues_from_its_nearest_visible_parent()
    {
        Assert.Equal(1, PlanningGridKeys.PositionOf(Visible, "so:1/pe:10/bo:100/f:7"));
        Assert.Equal(3, PlanningGridKeys.PositionOf(Visible, "so:2/pe:12"));
    }

    [Fact]
    public void Without_a_selection_or_a_visible_parent_keys_start_from_the_top()
    {
        Assert.Equal(-1, PlanningGridKeys.PositionOf(Visible, null));
        Assert.Equal(-1, PlanningGridKeys.PositionOf(Visible, "so:9/pe:1"));
        Assert.Equal(-1, PlanningGridKeys.PositionOf([], "so:1"));
    }

    [Theory]
    [InlineData("ArrowDown", false, false, false, false, true)]
    [InlineData("Enter", false, false, false, false, true)]
    [InlineData("+", true, false, false, false, true)]
    [InlineData("Enter", true, false, false, false, true)]
    [InlineData("ArrowLeft", false, true, false, false, false)]
    [InlineData("ArrowRight", false, false, true, false, false)]
    [InlineData("Enter", false, false, false, true, false)]
    [InlineData("ArrowRight", true, false, false, false, false)]
    [InlineData("Home", true, false, false, false, false)]
    public void Keys_with_Alt_Ctrl_or_Meta_go_to_the_browser_and_Shift_arrows_scroll_instead_of_moving(string key, bool shift, bool alt, bool ctrl, bool meta, bool handled)
    {
        var e = new KeyboardEventArgs { Key = key, ShiftKey = shift, AltKey = alt, CtrlKey = ctrl, MetaKey = meta };

        Assert.Equal(handled, PlanningGridKeys.Handles(e));
    }
}
