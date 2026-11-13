using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Xunit;

namespace Estimation.HrReporting.Tests;

public sealed class HeadcountPersonDetailsTests
{
    [Fact]
    public void The_details_show_every_headcount_column_once()
    {
        var shown = HeadcountDetailSections.All.SelectMany(s => s.Columns).ToList();

        Assert.Equal(HeadcountColumns.All.Count, shown.Count);
        Assert.Equal(HeadcountColumns.All.ToHashSet(), shown.ToHashSet());
        Assert.Subset(shown.ToHashSet(), HeadcountDetailSections.Wide.ToHashSet());
    }

    [Theory]
    [InlineData(null, "G01000001", "/hr-reporting/headcount/person?brid=G01000001")]
    [InlineData(7, "G01000001", "/hr-reporting/headcount/person?brid=G01000001&upload=7")]
    [InlineData(null, "A&B C", "/hr-reporting/headcount/person?brid=A%26B%20C")]
    public void A_person_link_carries_the_brid_and_an_older_upload(int? uploadId, string brid, string expected)
    {
        Assert.Equal(expected, HrReportingRoutes.PersonUrl(uploadId, brid));
    }

    [Fact]
    public void Coming_back_from_the_details_restores_the_list_of_the_same_upload_once()
    {
        var filter = new HeadcountFilter { Search = "smith" };
        var state = new HeadcountListState
        {
            Saved = new HeadcountListSnapshot(4, filter, "Team", true, 2, 100),
            ReturnPending = true
        };

        var restored = state.TakeReturn(4);

        Assert.NotNull(restored);
        Assert.Same(filter, restored.Filter);
        Assert.Equal(("Team", true, 2, 100), (restored.SortKey, restored.SortDescending, restored.Page, restored.PageSize));
        Assert.Null(state.TakeReturn(4));
    }

    [Fact]
    public void The_list_starts_fresh_without_a_return_or_for_another_upload()
    {
        var state = new HeadcountListState { Saved = new HeadcountListSnapshot(4, new HeadcountFilter(), null, false, 1, 50) };

        Assert.Null(state.TakeReturn(4));

        state.ReturnPending = true;
        Assert.Null(state.TakeReturn(5));
        Assert.False(state.ReturnPending);
    }
}
