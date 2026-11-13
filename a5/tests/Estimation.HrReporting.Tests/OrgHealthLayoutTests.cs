using Estimation.HrReporting.Services;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class OrgHealthLayoutTests
{
    [Fact]
    public void A_new_layout_shows_the_slide_kpis_and_every_column()
    {
        var layout = new OrgHealthLayout();

        Assert.True(layout.IsShown(OrgHealthKpis.Headcount));
        Assert.True(layout.IsShown(OrgHealthKpis.Country("Poland")));
        Assert.False(layout.IsShown(OrgHealthKpis.NonOperational));
        Assert.False(layout.IsShown(OrgHealthKpis.AverageTeamSize));
        Assert.True(layout.IsColumnShown(null));
        Assert.True(layout.IsColumnShown("RFT - Capital"));
        Assert.True(layout.IsDefaultKpiSet);
    }

    [Fact]
    public void Choices_survive_a_round_trip_and_new_kpis_keep_their_default()
    {
        var layout = new OrgHealthLayout();
        layout.SetShown(OrgHealthKpis.FixedPrice, false);
        layout.SetShown(OrgHealthKpis.NonOperational, true);
        layout.SetShown(OrgHealthKpis.Headcount, true);
        layout.SetColumnShown(null, false);
        layout.SetColumnShown(" RFT WR Execution & Delivery ", false);
        layout.SetColumnShown("", false);

        var restored = OrgHealthLayout.Parse(layout.Serialize());

        Assert.False(restored.IsShown(OrgHealthKpis.FixedPrice));
        Assert.True(restored.IsShown(OrgHealthKpis.NonOperational));
        Assert.True(restored.IsShown(OrgHealthKpis.Headcount));
        Assert.True(restored.IsShown(OrgHealthKpis.Country("Brazil")));
        Assert.False(restored.IsColumnShown(null));
        Assert.False(restored.IsColumnShown("rft wr execution & delivery"));
        Assert.False(restored.IsColumnShown(""));
        Assert.True(restored.IsColumnShown("RFT - Capital"));
        Assert.False(restored.IsDefaultKpiSet);
    }

    [Fact]
    public void Show_all_and_reset_cover_every_kpi()
    {
        var kpis = OrgHealthKpis.Catalog(["India"]);
        var layout = new OrgHealthLayout();
        layout.SetShown(OrgHealthKpis.Lpm, false);

        layout.ShowAll(kpis);
        Assert.All(kpis, k => Assert.True(layout.IsShown(k)));

        layout.ResetKpis();
        Assert.All(kpis, k => Assert.Equal(k.ShownByDefault, layout.IsShown(k)));
        Assert.True(layout.IsDefaultKpiSet);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"kpisOff\":[null,\"lpm\"],\"hiddenColumns\":null}")]
    public void Missing_or_broken_storage_falls_back_to_the_defaults(string? json)
    {
        var layout = OrgHealthLayout.Parse(json);

        Assert.True(layout.IsShown(OrgHealthKpis.Headcount));
        Assert.True(layout.IsColumnShown(null));
        Assert.Equal(json?.Contains("lpm") != true, layout.IsShown(OrgHealthKpis.Lpm));
    }
}
