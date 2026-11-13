using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Xunit;

namespace Estimation.HrReporting.Tests;

public class HeadcountDiffTests
{
    private static HeadcountRow Person(string brid, Action<HeadcountRow>? configure = null)
    {
        var row = new HeadcountRow
        {
            Brid = brid,
            EmployeeName = $"Person {brid}",
            Team = "Argon",
            Train = "Core Payments",
            Hc = 1,
            Fte = 1,
            LengthOfServiceDate = new DateTime(2020, 1, 1),
            LengthOfServiceInYears = 5
        };
        configure?.Invoke(row);
        return row;
    }

    [Fact]
    public void Classifies_joiners_leavers_changes_and_unchanged()
    {
        HeadcountRow[] a = [Person("B1"), Person("B2"), Person("B3")];
        HeadcountRow[] b = [Person("B1"), Person("B2", r => r.Team = "Neon"), Person("B4")];

        var result = HeadcountDiff.Compare(a, b);

        Assert.Equal(HeadcountChangeKind.Unchanged, Kind(result, "B1"));
        Assert.Equal(HeadcountChangeKind.Changed, Kind(result, "B2"));
        Assert.Equal(HeadcountChangeKind.Leaver, Kind(result, "B3"));
        Assert.Equal(HeadcountChangeKind.Joiner, Kind(result, "B4"));
        Assert.Equal(1, result.Count(HeadcountChangeKind.Joiner));
        Assert.Equal(1, result.Count(HeadcountChangeKind.Leaver));
        Assert.Equal(1, result.Count(HeadcountChangeKind.Changed));
        Assert.Equal(1, result.Count(HeadcountChangeKind.Unchanged));

        var change = Assert.Single(result.Rows.Single(r => r.Brid == "B2").Changes);
        Assert.Equal(HeadcountColumns.Team, change.Column);
        Assert.Equal("Argon", change.OldValue);
        Assert.Equal("Neon", change.NewValue);
        Assert.Equal(1, result.ChangesByColumn[HeadcountColumns.Team]);
    }

    [Fact]
    public void Matches_people_by_BRID_ignoring_case()
    {
        var result = HeadcountDiff.Compare([Person("g123")], [Person("G123", r => r.EmployeeName = "Person g123")]);

        Assert.Equal(HeadcountChangeKind.Unchanged, Assert.Single(result.Rows).Kind);
    }

    [Fact]
    public void Ignores_length_of_service_in_years_by_default()
    {
        HeadcountRow[] a = [Person("B1")];
        HeadcountRow[] b = [Person("B1", r => r.LengthOfServiceInYears = 6)];

        var byDefault = HeadcountDiff.Compare(a, b);
        var everything = HeadcountDiff.Compare(a, b, HeadcountColumns.Comparable);

        Assert.Equal(HeadcountChangeKind.Unchanged, Assert.Single(byDefault.Rows).Kind);
        Assert.DoesNotContain(HeadcountColumns.LengthOfServiceInYears, byDefault.ComparedColumns);
        var changed = Assert.Single(everything.Rows);
        Assert.Equal(HeadcountChangeKind.Changed, changed.Kind);
        Assert.Equal("5", changed.Changes.Single().OldValue);
        Assert.Equal("6", changed.Changes.Single().NewValue);
    }

    [Fact]
    public void Compares_numbers_and_dates_by_value()
    {
        HeadcountRow[] a = [Person("B1", r => { r.Fte = 1m; r.LengthOfServiceDate = new DateTime(2020, 1, 1); })];
        HeadcountRow[] b = [Person("B1", r => { r.Fte = 1.0000m; r.LengthOfServiceDate = new DateTime(2020, 1, 1); })];

        Assert.Equal(HeadcountChangeKind.Unchanged, Assert.Single(HeadcountDiff.Compare(a, b).Rows).Kind);
    }

    [Fact]
    public void Reports_a_value_that_was_cleared()
    {
        var result = HeadcountDiff.Compare([Person("B1", r => r.Vendor = "Acme")], [Person("B1")]);

        var change = Assert.Single(Assert.Single(result.Rows).Changes);
        Assert.Equal(HeadcountColumns.Vendor, change.Column);
        Assert.Equal("Acme", change.OldValue);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void Compares_only_the_requested_fields()
    {
        HeadcountRow[] a = [Person("B1")];
        HeadcountRow[] b = [Person("B1", r => { r.Team = "Neon"; r.City = "Prague"; })];

        var result = HeadcountDiff.Compare(a, b, [HeadcountColumns.City]);

        Assert.Equal([HeadcountColumns.City], Assert.Single(result.Rows).Changes.Select(c => c.Column));
    }

    [Fact]
    public void Lists_changed_fields_in_file_order()
    {
        HeadcountRow[] a = [Person("B1"), Person("B2")];
        HeadcountRow[] b =
        [
            Person("B1", r => { r.Train = "Data Platform"; r.City = "Prague"; }),
            Person("B2", r => r.Train = "Data Platform")
        ];

        var result = HeadcountDiff.Compare(a, b);

        Assert.Equal([HeadcountColumns.City, HeadcountColumns.Train], result.ChangesByColumn.Keys);
        Assert.Equal(2, result.ChangesByColumn[HeadcountColumns.Train]);
    }

    [Fact]
    public void Everyone_is_a_joiner_when_there_is_nothing_to_compare_with()
    {
        var result = HeadcountDiff.Compare([], [Person("B1"), Person("B2")]);

        Assert.All(result.Rows, r => Assert.Equal(HeadcountChangeKind.Joiner, r.Kind));
        Assert.True(result.HasDifferences);
    }

    private static HeadcountChangeKind Kind(HeadcountDiffResult result, string brid) =>
        result.Rows.Single(r => r.Brid == brid).Kind;
}
