using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class TeamsAnalysisTests
{
    private const string Wcr = "RFT -  Wholesale Credit Risk";
    private const string Market = "RFT - Market Risk";

    private static int _next;

    private static IEnumerable<HeadcountRow> People(int count, string domain, string? train, string? team, string country) =>
        Enumerable.Range(0, count).Select(_ => new HeadcountRow
        {
            Brid = $"B{Interlocked.Increment(ref _next)}",
            ImhL07 = domain,
            Train = train,
            Team = team,
            Country = country
        });

    private static TeamsAnalysisExclusion Hide(string domain, TeamsAnalysisExclusionKind kind, string value) =>
        new() { Id = Interlocked.Increment(ref _next), Domain = domain, Kind = kind, Value = value };

    [Theory]
    [InlineData(1, 5, 0, "none")]
    [InlineData(4, 1, 0, "high")]
    [InlineData(2, 3, 2, "high")]
    [InlineData(2, 0, 0, "low")]
    [InlineData(3, 0, 0, "low")]
    [InlineData(3, 2, 0, "med")]
    [InlineData(2, 0, 3, "med")]
    public void Friction_follows_the_workbook_rule(int locations, int india, int usa, string expected) =>
        Assert.Equal(expected, TeamsAnalysis.Friction(locations, india, usa));

    [Theory]
    [InlineData(7, "under 8")]
    [InlineData(8, "8-12")]
    [InlineData(12, "8-12")]
    [InlineData(13, "over 12")]
    public void Team_size_bands_match_the_workbook(int people, string expected) =>
        Assert.Equal(expected, TeamsAnalysis.SizeBand(people));

    [Fact]
    public void Teams_are_counted_per_country_with_locations_and_friction()
    {
        var rows = People(5, Wcr, "Credit AI", "Earth", "India")
            .Concat(People(2, Wcr, "Credit AI", "earth", "USA"))
            .Concat(People(3, Wcr, "Credit AI", "Mars", "Czech Republic"))
            .Concat(People(1, Wcr, "Credit AI", "Mars", "United Kingdom"))
            .ToList();

        var result = TeamsAnalysis.Build(rows, []);

        Assert.Equal(["India", "Czech Republic", "USA", "United Kingdom"], result.Countries);
        var domain = Assert.Single(result.Domains);
        Assert.Equal(Wcr, domain.Domain);
        Assert.Equal(["Earth", "Mars"], domain.Teams.Select(t => t.Team));

        var earth = domain.Teams[0];
        Assert.Equal([5, 0, 2, 0], earth.CountryCounts);
        Assert.Equal((7, 2, "high", "under 8"), (earth.Total, earth.Locations, earth.Friction, earth.Size));
        Assert.Equal(5m / 7m, earth.Share(0));

        var mars = domain.Teams[1];
        Assert.Equal((4, 2, "low"), (mars.Total, mars.Locations, mars.Friction));
    }

    [Fact]
    public void People_without_a_real_train_are_left_out()
    {
        var rows = People(2, Wcr, "Credit AI", "Earth", "India")
            .Concat(People(1, Wcr, "N/A", "Earth", "India"))
            .Concat(People(1, Wcr, "#N/A", "Earth", "India"))
            .Concat(People(1, Wcr, null, "Earth", "India"))
            .Concat(People(1, Wcr, "  ", "Earth", "India"))
            .ToList();

        var result = TeamsAnalysis.Build(rows, []);

        Assert.Equal(4, result.PeopleWithoutTrain);
        Assert.Equal(2, Assert.Single(result.Domains).People);
    }

    [Fact]
    public void A_hide_entry_only_applies_to_its_own_domain()
    {
        var rows = People(4, Wcr, "Capital Calcs", "Aluminium", "India")
            .Concat(People(6, Wcr, "Credit AI", "Earth", "India"))
            .Concat(People(9, Market, "Capital Calcs", "Aluminium", "India"))
            .Concat(People(3, Market, "Analytics", "Delta", "USA"))
            .ToList();

        var result = TeamsAnalysis.Build(rows,
        [
            Hide(Wcr, TeamsAnalysisExclusionKind.Train, "capital calcs"),
            Hide(Market, TeamsAnalysisExclusionKind.Team, "DELTA")
        ]);

        var wcr = result.Domains.Single(d => d.Domain == Wcr);
        var market = result.Domains.Single(d => d.Domain == Market);
        Assert.Equal(["Earth"], wcr.Teams.Select(t => t.Team));
        Assert.Equal(["Aluminium"], market.Teams.Select(t => t.Team));
        Assert.Equal(4, Assert.Single(wcr.Hidden).People);
        Assert.Equal(3, Assert.Single(market.Hidden).People);
    }

    [Fact]
    public void Summary_matches_the_workbook_formulas()
    {
        var rows = People(10, Wcr, "Credit AI", "Earth", "India")
            .Concat(People(3, Wcr, "Credit AI", "Mars", "India"))
            .Concat(People(2, Wcr, "Credit AI", "Mars", "Czech Republic"))
            .Concat(People(14, Wcr, "Data", "Neptune", "United Kingdom"))
            .Concat(People(4, Market, "Analytics", "Delta", "India"))
            .Concat(People(1, Market, "Analytics", "Delta", "USA"))
            .Concat(People(1, Market, "Analytics", "Delta", "United Kingdom"))
            .Concat(People(1, Market, "Analytics", "Delta", "Czech Republic"))
            .ToList();

        var tables = TeamsAnalysis.Summary(TeamsAnalysis.Build(rows, []));
        var summary = tables[0];
        IReadOnlyList<decimal?> Line(MetricsTable table, string label) => table.Rows.Single(r => r.Label == label).Values;

        Assert.Equal([Wcr, Market, TeamsAnalysis.TotalColumn], summary.Columns);
        Assert.Equal([2m, 1m, 3m], Line(summary, "# ARTs"));
        Assert.Equal([3m, 1m, 4m], Line(summary, "# Teams"));
        Assert.Equal([29m, 7m, 36m], Line(summary, "People in teams"));
        Assert.Equal([29m / 3m, 7m, 9m], Line(summary, "Avg # people per team"));
        Assert.Equal([1m, 0m, 0.75m], Line(summary, "Co-located teams (≤2 countries)"));
        Assert.Equal([2m / 3m, 0m, 0.5m], Line(summary, "No or low friction"));
        Assert.Equal([1m / 3m, 0m, 0.25m], Line(summary, "Teams sized 8-12"));

        var locations = tables.Single(t => t.Title == "Locations");
        Assert.Equal(["1", "2", "3", "4"], locations.Rows.Select(r => r.Label));
        Assert.Equal([2m, 0m, 2m, 0.5m], Line(locations, "1"));
        Assert.Equal([0m, 1m, 1m, 0.25m], Line(locations, "4"));
        Assert.Equal(locations.Columns.Count - 1, locations.PercentColumn);

        var friction = tables.Single(t => t.Title == "Friction");
        Assert.Equal([2m, 0m, 2m, 0.5m], Line(friction, "none"));
        Assert.Equal([0m, 0m, 0m, 0m], Line(friction, "low"));
        Assert.Equal([1m, 0m, 1m, 0.25m], Line(friction, "med"));
        Assert.Equal([0m, 1m, 1m, 0.25m], Line(friction, "high"));

        var size = tables.Single(t => t.Title == "Team Size");
        Assert.Equal([1m, 1m, 2m, 0.5m], Line(size, "under 8"));
        Assert.Equal([1m, 0m, 1m, 0.25m], Line(size, "over 12"));
    }

    [Fact]
    public void A_domain_whose_teams_are_all_hidden_stays_visible_with_no_teams()
    {
        var rows = People(3, Wcr, "Credit AI", "Earth", "India").ToList();

        var result = TeamsAnalysis.Build(rows, [Hide(Wcr, TeamsAnalysisExclusionKind.Train, "Credit AI")]);

        var domain = Assert.Single(result.Domains);
        Assert.Empty(domain.Teams);
        Assert.Equal(3, Assert.Single(domain.Hidden).People);
        Assert.Equal([null, null], TeamsAnalysis.Summary(result)[0].Rows.Single(r => r.Label == "Avg # people per team").Values);
    }
}
