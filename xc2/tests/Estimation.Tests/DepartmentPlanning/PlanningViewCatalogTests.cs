using Estimation.Components.DepartmentPlanning.Views;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningViewCatalogTests
{
    [Fact]
    public void The_timeline_comes_right_after_the_objectives_grid()
    {
        Assert.Equal(
            ["Objectives", "Timeline", "Treemap", "Waterline", "Risk quadrant", "Flow"],
            PlanningViewCatalog.All.Select(o => o.Title));
    }

    [Fact]
    public void Only_the_objectives_grid_and_the_timeline_offer_full_screen()
    {
        Assert.Equal(["Objectives", "Timeline"], PlanningViewCatalog.All.Where(o => o.CanFullScreen).Select(o => o.Title));
    }

    [Fact]
    public void An_unknown_view_falls_back_to_the_objectives_grid()
    {
        Assert.True(PlanningViewCatalog.Find("nope").IsGrid);
        Assert.True(PlanningViewCatalog.Find(null).IsGrid);
    }
}
