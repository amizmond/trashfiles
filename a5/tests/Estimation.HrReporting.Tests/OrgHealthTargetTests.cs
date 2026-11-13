using System.Globalization;
using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Estimation.HrReporting.Tests.Infrastructure;
using Xunit;

namespace Estimation.HrReporting.Tests;

public sealed class OrgHealthTargetTests : IDisposable
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly HrReportingDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private OrgHealthTargetService Service() => new(_db, _db.Clock);

    [Theory]
    [InlineData(OrgHealthComparison.Above, 50, 50, false)]
    [InlineData(OrgHealthComparison.Above, 50, 51, true)]
    [InlineData(OrgHealthComparison.AtLeast, 75, 75, true)]
    [InlineData(OrgHealthComparison.Below, 10, 10, false)]
    [InlineData(OrgHealthComparison.Below, 10, 9, true)]
    [InlineData(OrgHealthComparison.AtMost, 10, 10, true)]
    public void Rules_compare_the_shown_value(OrgHealthComparison comparison, int value, int shown, bool met) =>
        Assert.Equal(met, OrgHealthTargetRule.Create(null, comparison, value).IsMet(shown));

    [Fact]
    public void The_target_column_shows_the_text_else_the_rule_else_na()
    {
        Assert.Equal("> 50% (tbc)", OrgHealthKpis.DefaultTarget(OrgHealthKpis.TeamSize.Key).Display(OrgHealthKpis.TeamSize, Invariant));
        Assert.Equal("≥ 72.5%", OrgHealthTargetRule.Create("  ", OrgHealthComparison.AtLeast, 72.5m).Display(OrgHealthKpis.Engineering, Invariant));
        Assert.Equal("< 3", OrgHealthTargetRule.Create(null, OrgHealthComparison.Below, 3).Display(OrgHealthKpis.FixedPrice, Invariant));
        Assert.Equal("n/a", OrgHealthTargetRule.None.Display(OrgHealthKpis.Headcount, Invariant));
        Assert.Equal("4 | 4", OrgHealthKpis.DefaultTarget(OrgHealthKpis.Lpm.Key).Display(OrgHealthKpis.Lpm, Invariant));
    }

    [Fact]
    public void A_rule_needs_both_a_comparison_and_a_value()
    {
        var noValue = OrgHealthTargetRule.Create("x", OrgHealthComparison.Above, null);
        var noComparison = OrgHealthTargetRule.Create(null, null, 5);

        Assert.Equal(("x", (OrgHealthComparison?)null, (decimal?)null), (noValue.Text, noValue.Comparison, noValue.Value));
        Assert.True(noComparison.IsEmpty);
        Assert.Null(noComparison.IsMet(1));
        Assert.Equal(new string('a', OrgHealthTarget.TextMaxLength), OrgHealthTargetRule.Create(new string('a', 150), null, null).Text);
    }

    [Fact]
    public void Saved_targets_replace_the_defaults_by_key_ignoring_case()
    {
        OrgHealthTarget[] stored =
        [
            new() { KpiKey = "TEAM-SIZE", Text = null, Comparison = OrgHealthComparison.AtLeast, Value = 60 },
            new() { KpiKey = "outside-train" }
        ];
        var kpis = OrgHealthKpis.Catalog(["India"]);

        var targets = OrgHealthKpis.Targets(kpis, stored);

        Assert.Equal(kpis.Count, targets.Count);
        Assert.Equal("≥ 60%", targets["team-size"].Display(OrgHealthKpis.TeamSize, Invariant));
        Assert.True(targets["outside-train"].IsEmpty);
        Assert.Equal(OrgHealthKpis.DefaultTarget("co-located"), targets["co-located"]);
        Assert.True(targets["country:India"].IsEmpty);
    }

    [Fact]
    public async Task Only_targets_that_differ_from_the_default_are_stored()
    {
        var service = Service();
        var changes = await service.SaveAsync(
            new Dictionary<string, OrgHealthTargetRule>
            {
                ["team-size"] = OrgHealthTargetRule.Create("> 60%", OrgHealthComparison.Above, 60),
                ["co-located"] = OrgHealthKpis.DefaultTarget("co-located"),
                ["country:India"] = OrgHealthTargetRule.Create(null, OrgHealthComparison.AtMost, 50),
                ["outside-train"] = OrgHealthTargetRule.None
            },
            @" CORP\hr ");

        Assert.Equal(3, changes);
        var stored = await service.GetAllAsync();
        Assert.Equal(["country:India", "outside-train", "team-size"], stored.Select(t => t.KpiKey));
        var teamSize = stored.Single(t => t.KpiKey == "team-size");
        Assert.Equal(("> 60%", (OrgHealthComparison?)OrgHealthComparison.Above, (decimal?)60, @"CORP\hr"),
            (teamSize.Text, teamSize.Comparison, teamSize.Value, teamSize.UpdatedBy));
        Assert.Equal(_db.Clock.Now.UtcDateTime, teamSize.UpdatedAt);
        Assert.True(OrgHealthTargetRule.From(stored.Single(t => t.KpiKey == "outside-train")).IsEmpty);
    }

    [Fact]
    public async Task Saving_the_same_targets_again_changes_nothing_and_the_default_removes_the_override()
    {
        var service = Service();
        var custom = new Dictionary<string, OrgHealthTargetRule>
        {
            ["team-size"] = OrgHealthTargetRule.Create(null, OrgHealthComparison.AtLeast, 40)
        };
        await service.SaveAsync(custom, null);

        Assert.Equal(0, await service.SaveAsync(custom, null));
        Assert.Equal(1, await service.SaveAsync(
            new Dictionary<string, OrgHealthTargetRule> { ["TEAM-SIZE"] = OrgHealthKpis.DefaultTarget("team-size") }, null));
        Assert.Empty(await service.GetAllAsync());
    }

    [Fact]
    public async Task Blank_or_over_long_keys_are_ignored()
    {
        var service = Service();

        var changes = await service.SaveAsync(
            new Dictionary<string, OrgHealthTargetRule>
            {
                ["  "] = OrgHealthTargetRule.Create("x", null, null),
                [new string('k', OrgHealthTarget.KpiKeyMaxLength + 1)] = OrgHealthTargetRule.Create("x", null, null)
            },
            null);

        Assert.Equal(0, changes);
        Assert.Empty(await service.GetAllAsync());
    }
}
