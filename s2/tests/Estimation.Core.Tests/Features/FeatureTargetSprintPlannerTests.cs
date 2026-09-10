using Estimation.Core.Features.Services;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureTargetSprintPlannerTests
{
    private static Pi Pi(int id, string name, string? labels = null,
        PiLabelMatchMode mode = PiLabelMatchMode.Any, int startMonth = 1) => new()
        {
            Id = id,
            Name = name,
            FeatureLabels = labels,
            LabelMatchMode = mode,
            StartDate = new DateTime(2026, startMonth, 5),
            EndDate = new DateTime(2026, startMonth + 2, 5),
        };

    private static CapitalProjectSprint Sprint(int id, string name, int startDay, int endDay) => new()
    {
        Id = id,
        CapitalProjectId = 1,
        PiId = 7,
        Name = name,
        StartDate = new DateTime(2026, 9, startDay),
        EndDate = new DateTime(2026, 9, endDay),
    };

    private static readonly List<CapitalProjectSprint> Sprints =
    [
        Sprint(1, "186", 7, 20),
        Sprint(2, "187", 21, 30),
    ];

    [Fact]
    public void An_assigned_pi_wins_over_label_matches()
    {
        var pis = new[] { Pi(7, "2026.PI1"), Pi(8, "2026.PI2", "alpha") };

        var result = FeatureTargetSprintPlanner.ResolvePiCandidates(7, "alpha", pis);

        Assert.Equal(7, Assert.Single(result).Id);
    }

    [Fact]
    public void An_assigned_pi_that_no_longer_exists_resolves_to_nothing()
    {
        var result = FeatureTargetSprintPlanner.ResolvePiCandidates(99, "alpha", new[] { Pi(8, "2026.PI2", "alpha") });

        Assert.Empty(result);
    }

    [Fact]
    public void Labels_resolve_every_matching_pi()
    {
        var pis = new[]
        {
            Pi(7, "2026.PI1", "alpha", startMonth: 1),
            Pi(8, "2026.PI2", "alpha,beta", startMonth: 4),
            Pi(9, "2026.PI3", "gamma", startMonth: 7),
        };

        var result = FeatureTargetSprintPlanner.ResolvePiCandidates(null, "alpha", pis);

        Assert.Equal(new[] { 8, 7 }, result.Select(p => p.Id));
    }

    [Fact]
    public void The_all_match_mode_requires_every_rule_label()
    {
        var pis = new[] { Pi(8, "2026.PI2", "alpha,beta", PiLabelMatchMode.All) };

        Assert.Empty(FeatureTargetSprintPlanner.ResolvePiCandidates(null, "alpha", pis));
        Assert.Single(FeatureTargetSprintPlanner.ResolvePiCandidates(null, "alpha,beta", pis));
    }

    [Fact]
    public void A_feature_without_pi_or_labels_resolves_to_nothing()
    {
        Assert.Empty(FeatureTargetSprintPlanner.ResolvePiCandidates(null, null, new[] { Pi(7, "2026.PI1", "alpha") }));
    }

    [Fact]
    public void Selection_is_derived_from_sprints_fully_inside_the_target_window()
    {
        var selected = FeatureTargetSprintPlanner.DeriveSelection(
            Sprints, new DateTime(2026, 9, 7), new DateTime(2026, 9, 30));

        Assert.Equal(new[] { 1, 2 }, selected.OrderBy(id => id));
    }

    [Fact]
    public void A_partially_covered_sprint_is_not_selected()
    {
        var selected = FeatureTargetSprintPlanner.DeriveSelection(
            Sprints, new DateTime(2026, 9, 7), new DateTime(2026, 9, 25));

        Assert.Equal(1, Assert.Single(selected));
    }

    [Fact]
    public void No_target_dates_derive_no_selection()
    {
        Assert.Empty(FeatureTargetSprintPlanner.DeriveSelection(Sprints, null, null));
        Assert.Empty(FeatureTargetSprintPlanner.DeriveSelection(Sprints, new DateTime(2026, 9, 7), null));
    }

    [Fact]
    public void An_inverted_target_window_derives_no_selection()
    {
        var selected = FeatureTargetSprintPlanner.DeriveSelection(
            Sprints, new DateTime(2026, 9, 30), new DateTime(2026, 9, 7));

        Assert.Empty(selected);
    }

    [Fact]
    public void The_range_spans_the_lowest_start_and_highest_end()
    {
        var (start, end) = FeatureTargetSprintPlanner.ComputeRange(Sprints);

        Assert.Equal(new DateTime(2026, 9, 7), start);
        Assert.Equal(new DateTime(2026, 9, 30), end);
    }

    [Fact]
    public void An_empty_selection_produces_no_range()
    {
        var (start, end) = FeatureTargetSprintPlanner.ComputeRange([]);

        Assert.Null(start);
        Assert.Null(end);
    }

    [Fact]
    public void A_non_contiguous_selection_reports_every_sprint_it_spans()
    {
        var sprints = new List<CapitalProjectSprint>
        {
            Sprint(1, "186", 1, 10),
            Sprint(2, "187", 11, 20),
            Sprint(3, "188", 21, 30),
        };

        var (start, end) = FeatureTargetSprintPlanner.ComputeRange([sprints[0], sprints[2]]);

        Assert.Equal(new DateTime(2026, 9, 1), start);
        Assert.Equal(new DateTime(2026, 9, 30), end);
        Assert.Equal(3, FeatureTargetSprintPlanner.CountSpanned(sprints, start, end));
    }

    [Fact]
    public void Times_of_day_do_not_affect_the_derived_selection()
    {
        var selected = FeatureTargetSprintPlanner.DeriveSelection(
            Sprints,
            new DateTime(2026, 9, 7, 23, 30, 0),
            new DateTime(2026, 9, 30, 6, 15, 0));

        Assert.Equal(new[] { 1, 2 }, selected.OrderBy(id => id));
    }
}
