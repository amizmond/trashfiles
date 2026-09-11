using Estimation.Core.Features.Services;
using Estimation.Core.PlanningIncrement.Models;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureTargetSprintPlannerTests
{
    private static Pi Pi(int id, string name, string? labels = null,
        PiLabelMatchMode mode = PiLabelMatchMode.Any, int startMonth = 1, bool withDates = true) => new()
        {
            Id = id,
            Name = name,
            FeatureLabels = labels,
            LabelMatchMode = mode,
            StartDate = withDates ? new DateTime(2026, startMonth, 5) : null,
            EndDate = withDates ? new DateTime(2026, startMonth + 2, 28) : null,
        };

    private static Sprint Sprint(int id, int? piId, DateTime start, DateTime end, DateTime? uatEnd = null) => new()
    {
        Id = id,
        TeamId = 10,
        PiId = piId,
        Name = $"S{id}",
        StartDate = start,
        EndDate = end,
        UatStart = end.AddDays(1),
        UatEnd = uatEnd ?? end.AddDays(7),
    };

    [Fact]
    public void Pis_from_the_assigned_pi_and_labels_are_combined()
    {
        var pis = new[]
        {
            Pi(4, "PI4", "alpha", startMonth: 7),
            Pi(3, "PI3", startMonth: 4),
            Pi(5, "PI5", "gamma", startMonth: 10),
        };

        var result = FeatureTargetSprintPlanner.ResolvePis(3, "alpha", pis);

        Assert.Equal(new[] { "PI3", "PI4" }, result.Select(p => p.Name));
    }

    [Fact]
    public void A_pi_reached_both_ways_is_listed_once()
    {
        var pis = new[] { Pi(3, "PI3", "alpha") };

        var result = FeatureTargetSprintPlanner.ResolvePis(3, "alpha", pis);

        Assert.Equal(3, Assert.Single(result).Id);
    }

    [Fact]
    public void An_assigned_pi_alone_resolves_without_labels()
    {
        var result = FeatureTargetSprintPlanner.ResolvePis(3, null, new[] { Pi(3, "PI3"), Pi(4, "PI4", "alpha") });

        Assert.Equal(3, Assert.Single(result).Id);
    }

    [Fact]
    public void An_assigned_pi_that_no_longer_exists_still_leaves_label_matches()
    {
        var result = FeatureTargetSprintPlanner.ResolvePis(99, "alpha", new[] { Pi(4, "PI4", "alpha") });

        Assert.Equal(4, Assert.Single(result).Id);
    }

    [Fact]
    public void The_all_match_mode_requires_every_rule_label()
    {
        var pis = new[] { Pi(4, "PI4", "alpha,beta", PiLabelMatchMode.All) };

        Assert.Empty(FeatureTargetSprintPlanner.ResolvePis(null, "alpha", pis));
        Assert.Single(FeatureTargetSprintPlanner.ResolvePis(null, "alpha,beta", pis));
    }

    [Fact]
    public void A_feature_without_pi_or_labels_resolves_to_nothing()
    {
        Assert.Empty(FeatureTargetSprintPlanner.ResolvePis(null, null, new[] { Pi(3, "PI3", "alpha") }));
    }

    [Fact]
    public void Pis_without_dates_are_listed_after_dated_ones()
    {
        var pis = new[] { Pi(9, "Undated", "alpha", withDates: false), Pi(4, "PI4", "alpha", startMonth: 7) };

        var result = FeatureTargetSprintPlanner.ResolvePis(null, "alpha", pis);

        Assert.Equal(new[] { "PI4", "Undated" }, result.Select(p => p.Name));
    }

    [Fact]
    public void A_sprint_with_a_pi_belongs_only_to_that_pi()
    {
        var pi3 = Pi(3, "PI3", startMonth: 4);
        var pi4 = Pi(4, "PI4", startMonth: 7);
        var sprint = Sprint(1, 3, new DateTime(2026, 7, 6), new DateTime(2026, 7, 19));

        Assert.True(FeatureTargetSprintPlanner.BelongsToPi(sprint, pi3));
        Assert.False(FeatureTargetSprintPlanner.BelongsToPi(sprint, pi4));
    }

    [Fact]
    public void A_sprint_without_a_pi_belongs_to_pis_whose_dates_it_overlaps()
    {
        var pi4 = Pi(4, "PI4", startMonth: 7);
        var inside = Sprint(1, null, new DateTime(2026, 7, 6), new DateTime(2026, 7, 19));
        var straddling = Sprint(2, null, new DateTime(2026, 6, 29), new DateTime(2026, 7, 12));
        var outside = Sprint(3, null, new DateTime(2026, 11, 2), new DateTime(2026, 11, 15));

        Assert.True(FeatureTargetSprintPlanner.BelongsToPi(inside, pi4));
        Assert.True(FeatureTargetSprintPlanner.BelongsToPi(straddling, pi4));
        Assert.False(FeatureTargetSprintPlanner.BelongsToPi(outside, pi4));
    }

    [Fact]
    public void A_sprint_without_a_pi_cannot_match_a_pi_without_dates()
    {
        var undated = Pi(9, "Undated", withDates: false);
        var sprint = Sprint(1, null, new DateTime(2026, 7, 6), new DateTime(2026, 7, 19));

        Assert.False(FeatureTargetSprintPlanner.BelongsToPi(sprint, undated));
    }

    [Fact]
    public void Sprints_for_a_pi_come_back_in_date_order()
    {
        var pi4 = Pi(4, "PI4", startMonth: 7);
        var sprints = new[]
        {
            Sprint(2, 4, new DateTime(2026, 7, 20), new DateTime(2026, 8, 2)),
            Sprint(1, 4, new DateTime(2026, 7, 6), new DateTime(2026, 7, 19)),
            Sprint(3, 3, new DateTime(2026, 4, 6), new DateTime(2026, 4, 19)),
        };

        var result = FeatureTargetSprintPlanner.SprintsForPi(sprints, pi4);

        Assert.Equal(new[] { 1, 2 }, result.Select(s => s.Id));
    }

    [Fact]
    public void Target_end_is_the_latest_uat_end_rather_than_the_latest_sprint_end()
    {
        var selected = new[]
        {
            Sprint(1, 3, new DateTime(2026, 7, 6), new DateTime(2026, 7, 19), uatEnd: new DateTime(2026, 8, 30)),
            Sprint(2, 3, new DateTime(2026, 7, 20), new DateTime(2026, 8, 2), uatEnd: new DateTime(2026, 8, 9)),
        };

        var (start, end) = FeatureTargetSprintPlanner.ComputeTargets(selected);

        Assert.Equal(new DateTime(2026, 7, 6), start);
        Assert.Equal(new DateTime(2026, 8, 30), end);
    }

    [Fact]
    public void Targets_span_selections_from_every_team()
    {
        var teamA = Sprint(1, 3, new DateTime(2026, 7, 20), new DateTime(2026, 8, 2));
        var teamB = Sprint(2, 3, new DateTime(2026, 7, 6), new DateTime(2026, 7, 19));
        teamB.TeamId = 11;

        var (start, end) = FeatureTargetSprintPlanner.ComputeTargets([teamA, teamB]);

        Assert.Equal(new DateTime(2026, 7, 6), start);
        Assert.Equal(new DateTime(2026, 8, 9), end);
    }

    [Fact]
    public void No_selection_produces_no_targets()
    {
        var (start, end) = FeatureTargetSprintPlanner.ComputeTargets([]);

        Assert.Null(start);
        Assert.Null(end);
    }

    [Fact]
    public void Times_of_day_are_dropped_from_the_targets()
    {
        var sprint = Sprint(1, 3, new DateTime(2026, 7, 6, 14, 30, 0), new DateTime(2026, 7, 19),
            uatEnd: new DateTime(2026, 7, 26, 23, 59, 0));

        var (start, end) = FeatureTargetSprintPlanner.ComputeTargets([sprint]);

        Assert.Equal(new DateTime(2026, 7, 6), start);
        Assert.Equal(new DateTime(2026, 7, 26), end);
    }
}
