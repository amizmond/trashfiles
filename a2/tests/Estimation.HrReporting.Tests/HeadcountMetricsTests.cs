using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class HeadcountMetricsTests
{
    private const string Wcr = "RFT -  Wholesale Credit Risk";
    private const string Capital = "RFT - Capital";
    private const string Market = "RFT - Market Risk";

    private static int _next;

    private static HeadcountRow Person(
        string domain,
        string type = "Permanent/Regular",
        string? country = "India",
        string? art = "ART",
        string? eng = "Engineering",
        string? family = "Technology",
        string? job = "Software Engineer") =>
        new()
        {
            Brid = $"B{Interlocked.Increment(ref _next)}",
            ImhL07 = domain,
            ColleagueNonPermType = type,
            Country = country,
            ArtNonArt = art,
            EngNonEng = eng,
            JobFamilyGroup = family,
            Job = job
        };

    private static MetricsTable Table(IReadOnlyList<MetricsTable> tables, string title) => tables.Single(t => t.Title == title);

    private static IReadOnlyList<decimal?> Row(MetricsTable table, string label) => table.Rows.Single(r => r.Label == label).Values;

    [Fact]
    public void Columns_are_the_domains_in_name_order_then_the_grand_total()
    {
        var tables = HeadcountMetrics.Build([Person(Market), Person(Wcr), Person(Capital)]);

        Assert.All(tables, t => Assert.Equal([Wcr, Capital, Market, HeadcountMetrics.GrandTotal], t.Columns));
    }

    [Fact]
    public void Colleague_types_add_the_perm_and_fixed_price_rows()
    {
        var tables = HeadcountMetrics.Build(
        [
            Person(Wcr), Person(Wcr), Person(Wcr),
            Person(Wcr, "Graduate"),
            Person(Wcr, HeadcountRules.FixedPriceType), Person(Wcr, HeadcountRules.FixedPriceType),
            Person(Wcr, "Resource Augmentation (Time and Material Headcount)"),
            Person(Capital, HeadcountRules.FixedPriceType)
        ]);

        var types = Table(tables, "Colleague / Non-Perm Type");

        Assert.Equal(
            ["Graduate", "Permanent/Regular", HeadcountRules.FixedPriceType, "Resource Augmentation (Time and Material Headcount)",
             HeadcountMetrics.GrandTotal, "Total excl. fixed price", "Perm", "Perm % excl. fixed price", "Perm % incl. fixed price"],
            types.Rows.Select(r => r.Label));
        Assert.Equal([7m, 1m, 8m], Row(types, HeadcountMetrics.GrandTotal));
        Assert.Equal([5m, 0m, 5m], Row(types, "Total excl. fixed price"));
        Assert.Equal([4m, 0m, 4m], Row(types, "Perm"));
        Assert.Equal([0.8m, null, 0.8m], Row(types, "Perm % excl. fixed price"));
        Assert.Equal([4m / 7m, 0m, 0.5m], Row(types, "Perm % incl. fixed price"));
    }

    [Fact]
    public void Art_share_and_the_rest_come_from_the_column_total()
    {
        var tables = HeadcountMetrics.Build(
        [
            Person(Wcr), Person(Wcr), Person(Wcr), Person(Wcr, art: "NonART"),
            Person(Capital, art: "NonART"), Person(Capital, art: null)
        ]);

        var art = Table(tables, "ART / NonART");

        Assert.Equal(["ART", "NonART", HeadcountMetrics.Blank, HeadcountMetrics.GrandTotal, "ART %", "Non ART %"],
            art.Rows.Select(r => r.Label));
        Assert.Equal([0.75m, 0m, 0.5m], Row(art, "ART %"));
        Assert.Equal([0.25m, 1m, 0.5m], Row(art, "Non ART %"));
    }

    [Fact]
    public void Countries_are_ordered_by_people_and_get_share_rows()
    {
        var tables = HeadcountMetrics.Build(
        [
            Person(Wcr, country: "United Kingdom"),
            Person(Wcr), Person(Wcr), Person(Capital)
        ]);

        var countries = Table(tables, "Country");

        Assert.Equal(["India", "United Kingdom", HeadcountMetrics.GrandTotal, "India %", "United Kingdom %"],
            countries.Rows.Select(r => r.Label));
        Assert.Equal([2m / 3m, 1m, 0.75m], Row(countries, "India %"));
        Assert.True(countries.IsPercent(countries.Rows.Single(r => r.Label == "India %"), 0));
    }

    [Fact]
    public void The_employee_country_table_leaves_out_contractors()
    {
        var tables = HeadcountMetrics.Build(
        [
            Person(Wcr), Person(Wcr, "Temp/Contractor Payroll (Fixed Term)"),
            Person(Wcr, "Resource Augmentation (Time and Material Headcount)"),
            Person(Wcr, "T&M Agency (Headcount)")
        ]);

        Assert.Equal([2m, 2m], Row(Table(tables, "Country — employees only"), HeadcountMetrics.GrandTotal));
    }

    [Fact]
    public void Job_families_have_a_subtotal_row_and_indented_jobs()
    {
        var tables = HeadcountMetrics.Build(
        [
            Person(Wcr, family: "Change", job: "Scrum Master"),
            Person(Capital, family: "Change", job: "Business Analyst"),
            Person(Capital, family: "Change", job: "Business Analyst"),
            Person(Wcr)
        ]);

        var jobs = Table(tables, "Job Family Group and Job");

        Assert.Equal(
            [("Change", 0, MetricsRowKind.Group), ("Business Analyst", 1, MetricsRowKind.Value), ("Scrum Master", 1, MetricsRowKind.Value),
             ("Technology", 0, MetricsRowKind.Group), ("Software Engineer", 1, MetricsRowKind.Value), (HeadcountMetrics.GrandTotal, 0, MetricsRowKind.Total)],
            jobs.Rows.Select(r => (r.Label, r.Level, r.Kind)));
        Assert.Equal([1m, 2m, 3m], Row(jobs, "Change"));
        Assert.Equal([2m, 2m, 4m], Row(jobs, HeadcountMetrics.GrandTotal));
    }

    [Fact]
    public void Nobody_gives_empty_tables_without_failing()
    {
        var tables = HeadcountMetrics.Build([]);

        Assert.All(tables, t => Assert.Equal([HeadcountMetrics.GrandTotal], t.Columns));
        Assert.Equal([0m], Row(Table(tables, "Country"), HeadcountMetrics.GrandTotal));
        Assert.Equal([null], Row(Table(tables, "Colleague / Non-Perm Type"), "Perm % incl. fixed price"));
    }
}
