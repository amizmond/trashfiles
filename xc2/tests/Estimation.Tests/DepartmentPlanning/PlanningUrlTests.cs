using Estimation.Components.DepartmentPlanning;
using Estimation.Core.DepartmentPlanning.Services;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningUrlTests
{
    [Fact]
    public void A_link_spells_out_the_default_settings_so_the_reader_s_own_settings_cannot_fill_them_in()
    {
        var navigation = new TestNavigation("http://localhost/department-planning");

        var uri = navigation.GetUriWithQueryParameters(PlanningUrl.Parameters(null, 3, PlanningLens.Objectives, "grid", 0, false, []));

        Assert.Equal("http://localhost/department-planning?dept=all&pi=3&lens=objectives&view=grid&level=0&ip=0", uri);
    }

    [Fact]
    public void A_link_carries_every_chosen_setting_and_the_arts_in_order()
    {
        var navigation = new TestNavigation("http://localhost/department-planning?dept=all&pi=3&ip=1&gf=0");

        var uri = navigation.GetUriWithQueryParameters(PlanningUrl.Parameters(7, 4, PlanningLens.Drawdown, "treemap", 2, true, new HashSet<int> { 5, 2 }));

        Assert.Equal("http://localhost/department-planning?dept=7&pi=4&ip=1&gf=0&lens=drawdown&view=treemap&level=2&arts=2%2C5", uri);
    }

    [Fact]
    public void Clearing_the_art_filter_removes_it_from_the_link()
    {
        var navigation = new TestNavigation("http://localhost/department-planning?dept=all&pi=3&arts=2,5");

        var uri = navigation.GetUriWithQueryParameters(PlanningUrl.Parameters(null, 3, PlanningLens.Objectives, "grid", 0, false, []));

        Assert.DoesNotContain("arts", uri);
        Assert.Contains("ip=0", uri);
    }

    [Fact]
    public void A_menu_link_without_settings_has_no_scope()
    {
        var url = PlanningUrl.Read("http://localhost/department-planning");

        Assert.False(url.HasScope);
        Assert.False(url.HasDepartment);
        Assert.Null(url.Lens);
        Assert.Null(url.IncludeIp);
        Assert.Empty(url.ArtIds);
    }

    [Fact]
    public void A_written_link_reads_back_the_same_settings()
    {
        var navigation = new TestNavigation("http://localhost/department-planning");
        var uri = navigation.GetUriWithQueryParameters(PlanningUrl.Parameters(7, 4, PlanningLens.Drawdown, "treemap", 2, true, new HashSet<int> { 5, 2 }));

        var url = PlanningUrl.Read(uri + "#top");

        Assert.Equal(new PlanningUrlState(true, true, 7, 4, PlanningLens.Drawdown, "treemap", 2, true, url.ArtIds), url);
        Assert.Equal([2, 5], url.ArtIds.Order());
    }

    [Fact]
    public void All_departments_unknown_values_and_out_of_range_levels_are_read_safely()
    {
        var url = PlanningUrl.Read("http://localhost/department-planning?dept=all&pi=x&lens=sideways&level=9&ip=yes&arts=3,,x,4");

        Assert.True(url.HasScope);
        Assert.True(url.HasDepartment);
        Assert.Null(url.DepartmentId);
        Assert.Null(url.PiId);
        Assert.Null(url.Lens);
        Assert.Equal(PlanningPivot.MaxStartLevel, url.Level);
        Assert.False(url.IncludeIp);
        Assert.Equal([3, 4], url.ArtIds.Order());
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation(string uri) => Initialize("http://localhost/", uri);

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }
}
