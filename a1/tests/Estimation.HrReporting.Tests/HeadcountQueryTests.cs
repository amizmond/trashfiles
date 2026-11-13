using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class HeadcountQueryTests
{
    private static readonly HeadcountRow[] People =
    [
        new() { Brid = "B1", EmployeeName = "Anna Smith", Train = "Core Payments", Team = "Argon", Vendor = "Acme", Hc = 1, Fte = 1m, EmployeeEmailAddress = "anna.smith@example.com" },
        new() { Brid = "B2", EmployeeName = "Ben Jones", Train = "Core Payments", Team = "Neon", Hc = 1, Fte = 0.8m },
        new() { Brid = "B3", EmployeeName = "Carla Diaz", Train = "Data Platform", Team = "Xenon", Vendor = "Globex", Hc = 1, Fte = 0.5m },
        new() { Brid = "B4", EmployeeName = "Dan Brown", Train = null, Team = null, Hc = 0, Fte = null }
    ];

    [Fact]
    public void Keeps_rows_with_any_selected_value()
    {
        var filter = new HeadcountFilter();
        filter.For(HeadcountColumns.Team).Values = ["argon", "Xenon"];

        Assert.Equal(["B1", "B3"], Brids(HeadcountQuery.Apply(People, filter)));
    }

    [Fact]
    public void Not_with_values_excludes_them_and_keeps_empty_rows()
    {
        var filter = new HeadcountFilter();
        filter.For(HeadcountColumns.Train).Values = ["Core Payments"];
        filter.For(HeadcountColumns.Train).Not = true;

        Assert.Equal(["B3", "B4"], Brids(HeadcountQuery.Apply(People, filter)));
    }

    [Fact]
    public void Not_alone_keeps_rows_without_a_value()
    {
        var filter = new HeadcountFilter();
        filter.For(HeadcountColumns.Vendor).Not = true;

        Assert.Equal(["B2", "B4"], Brids(HeadcountQuery.Apply(People, filter)));
    }

    [Fact]
    public void Filters_numbers_by_their_shown_value()
    {
        var filter = new HeadcountFilter();
        filter.For(HeadcountColumns.Fte).Values = ["0.8", "0.5"];

        Assert.Equal(["B2", "B3"], Brids(HeadcountQuery.Apply(People, filter)));
    }

    [Fact]
    public void Search_needs_every_word_in_name_BRID_email_or_position()
    {
        Assert.Equal(["B1"], Brids(HeadcountQuery.Apply(People, new HeadcountFilter { Search = "smith anna" })));
        Assert.Equal(["B1"], Brids(HeadcountQuery.Apply(People, new HeadcountFilter { Search = "example.com" })));
        Assert.Equal(["B3"], Brids(HeadcountQuery.Apply(People, new HeadcountFilter { Search = "b3" })));
        Assert.Empty(HeadcountQuery.Apply(People, new HeadcountFilter { Search = "anna jones" }));
    }

    [Fact]
    public void Facets_ignore_their_own_filter_but_apply_the_others()
    {
        var filter = new HeadcountFilter();
        filter.For(HeadcountColumns.Train).Values = ["Core Payments"];

        var facets = HeadcountQuery.Facets(People, filter);

        Assert.Equal(2, facets[HeadcountColumns.Train]["Core Payments"]);
        Assert.Equal(1, facets[HeadcountColumns.Train]["Data Platform"]);
        Assert.Equal(["Argon", "Neon"], facets[HeadcountColumns.Team].Keys.Order());
    }

    [Fact]
    public void Facets_keep_selected_values_that_no_row_has()
    {
        var filter = new HeadcountFilter();
        filter.For(HeadcountColumns.Team).Values = ["Krypton"];

        var facets = HeadcountQuery.Facets(People, filter);

        Assert.Equal(0, facets[HeadcountColumns.Team]["Krypton"]);
    }

    [Fact]
    public void Sorts_empty_values_last_in_both_directions()
    {
        Assert.Equal(["B1", "B2", "B3", "B4"], Brids(HeadcountQuery.Sort(People, HeadcountColumns.Team, false)));
        Assert.Equal(["B3", "B2", "B1", "B4"], Brids(HeadcountQuery.Sort(People, HeadcountColumns.Team, true)));
    }

    [Fact]
    public void Sorts_numbers_and_number_like_text_numerically()
    {
        HeadcountRow[] rows =
        [
            new() { Brid = "B1", ControlSpan = "10", Fte = 1m },
            new() { Brid = "B2", ControlSpan = "9", Fte = 0.5m },
            new() { Brid = "B3", ControlSpan = "100", Fte = 0.75m }
        ];

        Assert.Equal(["B2", "B1", "B3"], Brids(HeadcountQuery.Sort(rows, HeadcountColumns.ControlSpan, false)));
        Assert.Equal(["B2", "B3", "B1"], Brids(HeadcountQuery.Sort(rows, HeadcountColumns.Fte, false)));
        Assert.Equal(["0.5", "0.75", "1"], HeadcountQuery.OrderedValues(HeadcountColumns.Fte, ["1", "0.75", "0.5"]));
    }

    [Fact]
    public void Totals_add_up_headcount_and_FTE()
    {
        var totals = HeadcountQuery.Totals(People);

        Assert.Equal(4, totals.People);
        Assert.Equal(3m, totals.Hc);
        Assert.Equal(2.3m, totals.Fte);
    }

    private static List<string> Brids(IEnumerable<HeadcountRow> rows) => rows.Select(r => r.Brid).ToList();
}
