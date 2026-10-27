using Estimation.Components.DepartmentPlanning;
using Estimation.Core.DepartmentPlanning.Services;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningColumnsTests
{
    [Fact]
    public void Each_sortable_column_has_its_own_sort()
    {
        var sorts = PlanningColumns.All.Select(c => c.Sort).OfType<PlanningSort>().ToList();

        Assert.Equal(sorts.Distinct(), sorts);
    }

    [Fact]
    public void Share_of_capacity_sorts_by_the_share_largest_first_and_target_end_earliest_first()
    {
        var share = PlanningColumns.All.Single(c => c.Key == PlanningColumns.Share);
        var targetEnd = PlanningColumns.All.Single(c => c.Key == PlanningColumns.TargetEnd);

        Assert.Equal(PlanningSort.Share, share.Sort);
        Assert.True(share.DefaultDescending);
        Assert.Equal(PlanningSort.TargetEnd, targetEnd.Sort);
        Assert.False(targetEnd.DefaultDescending);
    }

    [Fact]
    public void The_hygiene_column_is_headed_hygiene_issues()
    {
        Assert.Equal("Hygiene issues", PlanningColumns.All.Single(c => c.Key == PlanningColumns.Hygiene).Header);
    }
}
