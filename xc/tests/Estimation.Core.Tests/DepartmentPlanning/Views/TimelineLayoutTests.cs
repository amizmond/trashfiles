using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.DepartmentPlanning.Views;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.DepartmentPlanning.Views;

public class TimelineLayoutTests
{
    private static readonly DateTime PiStart = new(2026, 10, 5);
    private static readonly DateTime PiEnd = new(2026, 12, 20);
    private static readonly DateTime Today = new(2026, 11, 10);

    private static PlanningRow Row(string id, PlanningLevel level, int depth, DateTime? start, DateTime? end, params PlanningRow[] children) => new()
    {
        Id = id,
        Level = level,
        EntityId = id.GetHashCode(),
        JiraId = id.ToUpperInvariant(),
        Title = id,
        TargetStart = start,
        TargetEnd = end,
        Depth = depth,
        Children = children,
    };

    private static PlanningRow Objective(string id, params PlanningRow[] epics) => Row(id, PlanningLevel.StrategicObjective, 0, null, null, epics);

    private static PlanningRow Epic(string id, DateTime? start, DateTime? end, params PlanningRow[] outcomes) => Row(id, PlanningLevel.PortfolioEpic, 1, start, end, outcomes);

    private static PlanningRow Outcome(string id, DateTime? start, DateTime? end, params PlanningRow[] features) => Row(id, PlanningLevel.BusinessOutcome, 2, start, end, features);

    private static PlanningRow Feature(string id, DateTime? start, DateTime? end) => Row(id, PlanningLevel.BusinessEpic, 3, start, end);

    private static PlanningTree Tree(params PlanningRow[] roots) => PlanningTree.Empty with { Roots = roots };

    private static TimelineSettings Settings(TimelineRange range = TimelineRange.Pi, PlanningLevel level = PlanningLevel.PortfolioEpic, IReadOnlySet<string>? collapsed = null) =>
        new(PiStart, PiEnd, Today, level, range, collapsed);

    private static PlanningTree SampleTree() => Tree(
        Objective("grow",
            Epic("payments", new DateTime(2026, 10, 10), new DateTime(2026, 11, 30),
                Outcome("rails", new DateTime(2026, 10, 12), new DateTime(2026, 11, 20),
                    Feature("f1", new DateTime(2026, 10, 12), new DateTime(2026, 11, 1)),
                    Feature("f2", null, new DateTime(2026, 11, 20))),
                Outcome("alerts", null, null,
                    Feature("f3", null, null))),
            Epic("tokens", null, null,
                Outcome("vault", null, null,
                    Feature("f4", new DateTime(2026, 11, 1), new DateTime(2027, 2, 15))))),
        Objective("resilience",
            Epic("fraud", null, null,
                Outcome("scoring", null, null,
                    Feature("f5", null, null)))));

    private static TimelineLane Lane(TimelineLayout layout, string id) => layout.Lanes.Single(l => l.Row.Id == id);

    [Fact]
    public void The_pi_range_snaps_to_whole_months_and_ticks_every_month()
    {
        var layout = TimelineLayout.Build(SampleTree(), Settings());

        Assert.Equal(new DateTime(2026, 10, 1), layout.RangeStart);
        Assert.Equal(new DateTime(2027, 1, 1), layout.RangeEnd);
        Assert.Equal(["Oct 2026", "Nov", "Dec"], layout.Ticks.Select(t => t.Label));
        Assert.All(layout.Ticks, t => Assert.True(t.IsMajor));
        Assert.Equal(0d, layout.Ticks[0].X);
        Assert.Equal(31d / 92d * 100d, layout.Ticks[1].X, 6);

        var pi = Assert.IsType<TimelineWindow>(layout.Pi);
        Assert.Equal(PiStart, pi.Start);
        Assert.Equal(PiEnd, pi.End);
        Assert.Equal(4d / 92d * 100d, pi.X, 6);
        Assert.Equal(77d / 92d * 100d, pi.Width, 6);
        Assert.Equal(40, layout.DaysToPiEnd);
        Assert.Equal(40.5d / 92d * 100d, layout.TodayX!.Value, 6);
    }

    [Fact]
    public void Bars_use_own_dates_and_fall_back_to_the_dates_underneath()
    {
        var layout = TimelineLayout.Build(SampleTree(), Settings());

        var payments = Lane(layout, "payments").Bar!;
        Assert.Equal(new DateTime(2026, 10, 10), payments.Start);
        Assert.Equal(new DateTime(2026, 11, 30), payments.End);
        Assert.False(payments.IsDerived);
        Assert.Equal(52, payments.DurationDays);
        Assert.Equal(9d / 92d * 100d, payments.X, 6);
        Assert.Equal(52d / 92d * 100d, payments.Width, 6);

        var tokens = Lane(layout, "tokens").Bar!;
        Assert.Equal(new DateTime(2026, 11, 1), tokens.Start);
        Assert.Equal(new DateTime(2027, 2, 15), tokens.End);
        Assert.True(tokens.StartDerived);
        Assert.True(tokens.EndDerived);
    }

    [Fact]
    public void Bars_running_past_the_pi_end_are_flagged_and_clipped_to_the_range()
    {
        var layout = TimelineLayout.Build(SampleTree(), Settings());

        var tokens = Lane(layout, "tokens").Bar!;
        Assert.True(tokens.RunsPastPi);
        Assert.True(tokens.ClippedRight);
        Assert.False(tokens.ClippedLeft);
        Assert.False(tokens.OutsideRange);
        Assert.Equal(57, tokens.DaysPastPiEnd);
        Assert.Equal(100d, tokens.Right, 6);
        Assert.Equal(1, layout.PastPiCount);

        var payments = Lane(layout, "payments").Bar!;
        Assert.False(payments.RunsPastPi);
        Assert.Equal(-20, payments.DaysPastPiEnd);
    }

    [Fact]
    public void Children_running_past_the_parent_end_get_an_extension_and_count_as_a_conflict()
    {
        var tree = Tree(Objective("so",
            Epic("pe", new DateTime(2026, 10, 10), new DateTime(2026, 11, 15),
                Outcome("bo", null, null, Feature("f", new DateTime(2026, 11, 1), new DateTime(2026, 12, 10))))));

        var layout = TimelineLayout.Build(tree, Settings());

        var bar = Lane(layout, "pe").Bar!;
        Assert.Equal(new DateTime(2026, 11, 15), bar.End);
        Assert.Equal(new DateTime(2026, 12, 10), bar.ChildrenEnd);
        Assert.True(bar.HasConflict);
        Assert.Equal(46d / 92d * 100d, bar.ExtensionX!.Value, 6);
        Assert.Equal(25d / 92d * 100d, bar.ExtensionWidth!.Value, 6);
        Assert.False(bar.ExtensionClipped);
        Assert.Equal(1, layout.ConflictCount);
    }

    [Fact]
    public void An_outcome_s_own_later_date_is_not_a_business_epic_conflict()
    {
        var tree = Tree(Objective("so",
            Epic("pe", new DateTime(2026, 10, 10), new DateTime(2026, 11, 15),
                Outcome("bo", new DateTime(2026, 10, 20), new DateTime(2026, 12, 10), Feature("f", null, null)))));

        var layout = TimelineLayout.Build(tree, Settings());

        var bar = Lane(layout, "pe").Bar!;
        Assert.Equal(new DateTime(2026, 11, 15), bar.End);
        Assert.Null(bar.ChildrenEnd);
        Assert.False(bar.HasConflict);
        Assert.Equal(0, layout.ConflictCount);
    }

    [Fact]
    public void An_epic_under_two_objectives_is_drawn_under_each_but_counted_once()
    {
        var shared = Epic("shared", new DateTime(2026, 10, 10), new DateTime(2027, 1, 20));
        var undated = Epic("open", null, null);
        var tree = Tree(Objective("a", shared, undated), Objective("b", shared, undated));

        var layout = TimelineLayout.Build(tree, Settings());

        Assert.Equal(2, layout.Lanes.Count(l => l.Row.Id == "shared"));
        Assert.Equal(2, layout.ItemCount);
        Assert.Equal(1, layout.DatedCount);
        Assert.Equal(1, layout.PastPiCount);
        Assert.Single(layout.Undated);
    }

    [Fact]
    public void Children_ending_before_the_parent_end_are_not_a_conflict()
    {
        var tree = Tree(Objective("so",
            Epic("pe", new DateTime(2026, 10, 10), new DateTime(2026, 12, 15),
                Outcome("bo", null, null, Feature("f", null, new DateTime(2026, 11, 10))))));

        var layout = TimelineLayout.Build(tree, Settings());

        Assert.Null(Lane(layout, "pe").Bar!.ChildrenEnd);
        Assert.Equal(0, layout.ConflictCount);
    }

    [Fact]
    public void Items_without_any_date_go_to_the_undated_bin_and_the_counts_add_up()
    {
        var layout = TimelineLayout.Build(SampleTree(), Settings());

        Assert.Equal(3, layout.ItemCount);
        Assert.Equal(2, layout.DatedCount);
        Assert.Equal(1, layout.OwnDatedCount);
        var undated = Assert.Single(layout.Undated);
        Assert.Equal("fraud", undated.Row.Id);
        Assert.Equal("resilience", undated.GroupTitle);
        Assert.Equal(0, undated.Descendants);
        Assert.DoesNotContain(layout.Lanes, l => l.Row.Id == "fraud");
    }

    [Fact]
    public void Deeper_levels_add_outcomes_and_business_epics_as_lanes_and_items()
    {
        var epics = TimelineLayout.Build(SampleTree(), Settings());
        var outcomes = TimelineLayout.Build(SampleTree(), Settings(level: PlanningLevel.BusinessOutcome));
        var features = TimelineLayout.Build(SampleTree(), Settings(level: PlanningLevel.BusinessEpic));

        Assert.Equal(["grow", "payments", "tokens", "resilience"], epics.Lanes.Select(l => l.Row.Id));
        Assert.Equal(["grow", "payments", "rails", "tokens", "vault", "resilience"], outcomes.Lanes.Select(l => l.Row.Id));
        Assert.Equal(["grow", "payments", "rails", "f1", "f2", "tokens", "vault", "f4", "resilience"], features.Lanes.Select(l => l.Row.Id));

        Assert.Equal(6, outcomes.ItemCount);
        Assert.Equal(["alerts", "fraud"], outcomes.Undated.Select(u => u.Row.Id));
        Assert.Equal(9, features.ItemCount);
        Assert.Equal(["alerts", "fraud"], features.Undated.Select(u => u.Row.Id));
        Assert.Equal(1, features.Undated[0].Descendants);
        Assert.Equal(2, features.Undated[1].Descendants);
    }

    [Fact]
    public void Lanes_are_stacked_from_the_top_in_tree_order()
    {
        var layout = TimelineLayout.Build(SampleTree(), Settings());

        Assert.Equal([0, 1, 2, 3], layout.Lanes.Select(l => l.Index));
        Assert.Equal([0d, 26d, 52d, 78d], layout.Lanes.Select(l => l.Top));
        Assert.Equal(4 * TimelineLayout.LaneHeight, layout.Height);
        Assert.True(layout.Lanes[0].IsGroup);
        Assert.Equal(2, layout.Lanes[0].ItemCount);
        Assert.False(layout.Lanes[1].IsGroup);
    }

    [Fact]
    public void A_group_lane_spans_everything_underneath_it()
    {
        var layout = TimelineLayout.Build(SampleTree(), Settings());

        var grow = Lane(layout, "grow").Bar!;
        Assert.Equal(new DateTime(2026, 10, 10), grow.Start);
        Assert.Equal(new DateTime(2027, 2, 15), grow.End);
        Assert.True(grow.IsDerived);
        Assert.Null(grow.ChildrenEnd);
        Assert.Null(Lane(layout, "resilience").Bar);
    }

    [Fact]
    public void A_collapsed_group_keeps_only_its_header_lane_but_still_counts_its_items()
    {
        var layout = TimelineLayout.Build(SampleTree(), Settings(collapsed: new HashSet<string> { "grow" }));

        Assert.Equal(["grow", "resilience"], layout.Lanes.Select(l => l.Row.Id));
        Assert.True(layout.Lanes[0].IsCollapsed);
        Assert.Equal(3, layout.ItemCount);
        Assert.Equal(2, layout.DatedCount);
        Assert.Equal(1, layout.PastPiCount);
    }

    [Fact]
    public void A_quarter_around_the_pi_and_all_dates_widen_the_range()
    {
        var quarter = TimelineLayout.Build(SampleTree(), Settings(TimelineRange.PiPlusQuarter));
        var all = TimelineLayout.Build(SampleTree(), Settings(TimelineRange.All));

        Assert.Equal(new DateTime(2026, 7, 1), quarter.RangeStart);
        Assert.Equal(new DateTime(2027, 4, 1), quarter.RangeEnd);
        Assert.Equal(9, quarter.Ticks.Count);

        Assert.Equal(new DateTime(2026, 10, 1), all.RangeStart);
        Assert.Equal(new DateTime(2027, 3, 1), all.RangeEnd);
        var tokens = Lane(all, "tokens").Bar!;
        Assert.False(tokens.ClippedRight);
        Assert.True(tokens.RunsPastPi);
        Assert.True(tokens.Right < 100d);
    }

    [Fact]
    public void Without_pi_dates_the_range_follows_the_items_and_the_window_is_unknown()
    {
        var layout = TimelineLayout.Build(SampleTree(), new TimelineSettings(null, null, Today));

        Assert.Null(layout.Pi);
        Assert.Null(layout.DaysToPiEnd);
        Assert.Equal(new DateTime(2026, 10, 1), layout.RangeStart);
        Assert.Equal(new DateTime(2027, 3, 1), layout.RangeEnd);
        Assert.NotNull(layout.TodayX);
        Assert.All(layout.Lanes.Where(l => l.Bar is not null), l => Assert.False(l.Bar!.RunsPastPi));
        Assert.Equal(0, layout.PastPiCount);
    }

    [Fact]
    public void With_no_dates_at_all_there_is_no_range_but_every_item_is_listed_as_undated()
    {
        var tree = Tree(Objective("so", Epic("a", null, null), Epic("b", null, null)));

        var layout = TimelineLayout.Build(tree, new TimelineSettings(null, null, Today));

        Assert.False(layout.HasRange);
        Assert.Empty(layout.Lanes);
        Assert.Empty(layout.Ticks);
        Assert.Equal(["a", "b"], layout.Undated.Select(u => u.Row.Id));
        Assert.Equal(2, layout.ItemCount);
        Assert.Equal(0, layout.DatedCount);
    }

    [Fact]
    public void A_single_date_makes_a_milestone()
    {
        var tree = Tree(Objective("so",
            Epic("end-only", null, new DateTime(2026, 11, 5)),
            Epic("start-only", new DateTime(2026, 11, 5), null),
            Epic("one-day", new DateTime(2026, 11, 5), new DateTime(2026, 11, 5))));

        var layout = TimelineLayout.Build(tree, Settings());

        var endOnly = Lane(layout, "end-only").Bar!;
        Assert.True(endOnly.IsMilestone);
        Assert.Equal(0d, endOnly.Width);
        Assert.Null(endOnly.DurationDays);
        Assert.Equal(35d / 92d * 100d, endOnly.X, 6);
        Assert.True(Lane(layout, "start-only").Bar!.IsMilestone);

        var oneDay = Lane(layout, "one-day").Bar!;
        Assert.False(oneDay.IsMilestone);
        Assert.Equal(1, oneDay.DurationDays);
        Assert.Equal(1d / 92d * 100d, oneDay.Width, 6);
    }

    [Fact]
    public void A_short_bar_in_a_long_range_keeps_a_minimum_width_inside_the_chart()
    {
        var tree = Tree(Objective("so",
            Epic("long", new DateTime(2024, 1, 1), new DateTime(2027, 12, 31)),
            Epic("one-day", new DateTime(2026, 11, 5), new DateTime(2026, 11, 5)),
            Epic("last-day", new DateTime(2027, 12, 31), new DateTime(2027, 12, 31))));

        var layout = TimelineLayout.Build(tree, Settings(TimelineRange.All));

        var oneDay = Lane(layout, "one-day").Bar!;
        Assert.Equal(TimelineLayout.MinBarWidth, oneDay.Width);
        Assert.Equal(1, oneDay.DurationDays);

        var lastDay = Lane(layout, "last-day").Bar!;
        Assert.Equal(TimelineLayout.MinBarWidth, lastDay.Width);
        Assert.Equal(100d, lastDay.Right, 6);
    }

    [Fact]
    public void Bars_outside_the_range_are_marked_at_the_edge_they_lie_beyond()
    {
        var tree = Tree(Objective("so",
            Epic("past", new DateTime(2026, 3, 1), new DateTime(2026, 6, 30)),
            Epic("future", new DateTime(2027, 5, 1), new DateTime(2027, 8, 31)),
            Epic("straddles", new DateTime(2026, 8, 1), new DateTime(2027, 3, 31))));

        var layout = TimelineLayout.Build(tree, Settings());

        var past = Lane(layout, "past").Bar!;
        Assert.True(past.OutsideRange);
        Assert.True(past.ClippedLeft);
        Assert.Equal(0d, past.X);
        Assert.Equal(0d, past.Width);

        var future = Lane(layout, "future").Bar!;
        Assert.True(future.OutsideRange);
        Assert.True(future.ClippedRight);
        Assert.Equal(100d, future.X);

        var straddles = Lane(layout, "straddles").Bar!;
        Assert.False(straddles.OutsideRange);
        Assert.True(straddles.ClippedLeft);
        Assert.True(straddles.ClippedRight);
        Assert.Equal(0d, straddles.X);
        Assert.Equal(100d, straddles.Width);
    }

    [Fact]
    public void Progress_fills_a_share_of_the_bar()
    {
        var epic = Epic("pe", new DateTime(2026, 10, 1), new DateTime(2026, 12, 31)) with
        {
            Metrics = PlanningMetrics.Empty with { Features = 4, Done = 1 },
        };

        var layout = TimelineLayout.Build(Tree(Objective("so", epic)), Settings());

        var bar = Lane(layout, "pe").Bar!;
        Assert.Equal(100d, bar.Width, 6);
        Assert.Equal(25d, bar.FillWidth, 6);
    }

    [Fact]
    public void Long_ranges_label_only_quarters_or_years()
    {
        var tree = Tree(Objective("so", Epic("pe", new DateTime(2025, 2, 1), new DateTime(2027, 6, 30))));

        var quarters = TimelineLayout.Build(tree, Settings(TimelineRange.All));

        Assert.Equal(29, quarters.Ticks.Count);
        Assert.Equal(["Apr 2025", "Jul", "Oct", "Jan 2026", "Apr", "Jul", "Oct", "Jan 2027", "Apr"], quarters.Ticks.Where(t => t.IsMajor).Select(t => t.Label));
        Assert.Equal(string.Empty, quarters.Ticks[0].Label);

        var longTree = Tree(Objective("so", Epic("pe", new DateTime(2022, 2, 1), new DateTime(2027, 6, 30))));
        var years = TimelineLayout.Build(longTree, Settings(TimelineRange.All));
        Assert.Equal(["Jan 2023", "Jan 2024", "Jan 2025", "Jan 2026", "Jan 2027"], years.Ticks.Where(t => t.IsMajor).Select(t => t.Label));
    }

    [Fact]
    public void Today_outside_the_range_has_no_line()
    {
        var layout = TimelineLayout.Build(SampleTree(), Settings() with { Today = new DateTime(2027, 6, 1) });

        Assert.Null(layout.TodayX);
        Assert.Equal(-163, layout.DaysToPiEnd);
    }

    [Fact]
    public void Dates_are_compared_by_day_regardless_of_the_time_part()
    {
        var tree = Tree(Objective("so", Epic("pe", new DateTime(2026, 10, 5, 23, 30, 0), new DateTime(2026, 12, 20, 1, 0, 0))));

        var layout = TimelineLayout.Build(tree, new TimelineSettings(new DateTime(2026, 10, 5, 8, 0, 0), new DateTime(2026, 12, 20, 22, 0, 0), new DateTime(2026, 11, 10, 23, 59, 0)));

        var bar = Lane(layout, "pe").Bar!;
        Assert.Equal(new DateTime(2026, 10, 5), bar.Start);
        Assert.Equal(new DateTime(2026, 12, 20), bar.End);
        Assert.False(bar.RunsPastPi);
        Assert.Equal(0, bar.DaysPastPiEnd);
        Assert.Equal(layout.Pi!.X, bar.X, 6);
        Assert.Equal(layout.Pi.Width, bar.Width, 6);
        Assert.Equal(40, layout.DaysToPiEnd);
    }

    [Fact]
    public void Trees_built_by_the_planning_tree_work_and_drawdown_line_markers_are_skipped()
    {
        var data = PlanningData();
        var objectives = TimelineLayout.Build(DepartmentPlanningTree.Build(data, new PlanningOptions()), Settings());
        var drawdown = TimelineLayout.Build(DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown }), Settings());

        var instant = objectives.Lanes.Single(l => l.Row.EntityId == 10 && !l.IsGroup);
        Assert.True(instant.Bar!.EndDerived);
        Assert.Equal(instant.Row.Metrics.TargetEnd, instant.Bar.End);
        Assert.Equal(new DateTime(2026, 11, 2), instant.Bar.Start);
        Assert.Equal(new DateTime(2026, 11, 7), instant.Bar.End);
        Assert.Empty(objectives.Undated);

        Assert.All(drawdown.Lanes, l => Assert.Equal(PlanningRowKind.Node, l.Row.Kind));
        Assert.Equal(["Core Payments", "Data Platform"], drawdown.Lanes.Where(l => l.IsGroup).Select(l => l.Row.Title));
        Assert.Contains(drawdown.Lanes, l => l.Row.Level == PlanningLevel.BusinessOutcome && l.Row.Depth == 1);
        Assert.Contains(drawdown.Lanes, l => l.Row.IsBelowArtLine);
    }

    private static DepartmentPlanningData PlanningData()
    {
        const int corePayments = 1;
        const int dataPlatform = 2;
        const int argon = 1;
        const int zeta = 3;
        const int instantPayments = 10;
        const int tokenisation = 11;
        const int dataLake = 12;

        PlanningFeatureTeam Link(int teamId, int sp, bool included = true, bool below = false) =>
            new(teamId, sp, included, below ? [new ArtLineRef(corePayments, "Core Payments")] : [], 1, null, 0m);

        PlanningFeature Feature(int id, int? outcomeId, int? epicId, DateTime? start, DateTime? end, params PlanningFeatureTeam[] teams) =>
            new(id, $"PAY-{id}", $"Feature {id}", "In Progress", null, null, id, 3, "PI", outcomeId, epicId, start, end, null, teams);

        var arts = new List<PlanningArt>
        {
            new(corePayments, "Core Payments", "CP", 1, "Retail", ArtPrioritization.PortfolioEpic, ArtOrderMode.General, [argon],
            [
                new ArtOrderEntry(instantPayments, 1, true, ArtOrderOrigin.Saved),
                new ArtOrderEntry(tokenisation, 2, false, ArtOrderOrigin.Saved),
            ]),
            new(dataPlatform, "Data Platform", "DP", 1, "Retail", ArtPrioritization.BusinessOutcome, ArtOrderMode.Empty, [zeta], []),
        };
        var teams = new List<PlanningTeam>
        {
            new(argon, "Argon", [corePayments], 1m, 100d, 100m, 5, "Sprints"),
            new(zeta, "Zeta", [dataPlatform], 1m, 40d, 40m, 2, "PI"),
        };
        var features = new List<PlanningFeature>
        {
            Feature(1, 100, instantPayments, new DateTime(2026, 11, 2), new DateTime(2026, 11, 7), Link(argon, 30)),
            Feature(2, 100, instantPayments, null, new DateTime(2026, 11, 5), Link(argon, 40)),
            Feature(3, 102, tokenisation, null, new DateTime(2026, 11, 30), Link(argon, 50, below: true)),
            Feature(5, 103, dataLake, null, new DateTime(2026, 12, 1), Link(zeta, 30)),
        };
        var objectives = new List<PlanningObjective> { new(1, "STRAT-1", "Grow payments", "In Progress", null, [corePayments]) };
        var epics = new List<PlanningEpic>
        {
            new(instantPayments, "EPIC-10", "Instant payments", "In Progress", "Green", null, null, null, [1]),
            new(tokenisation, "EPIC-11", "Tokenisation", "In Progress", null, null, null, null, [1]),
            new(dataLake, "EPIC-12", "Data lake", "Funnel", null, null, null, null, []),
        };
        var outcomes = new List<PlanningOutcome>
        {
            new(100, "BO-100", "Rails", "In Progress", "Amber", null, instantPayments, null, null),
            new(102, "BO-102", "Token vault", "In Progress", "Red", null, tokenisation, null, null),
            new(103, "BO-103", "Ingestion", "To Do", null, null, dataLake, null, null),
        };

        return new DepartmentPlanningData(new DepartmentPlanningScope(1, [corePayments, dataPlatform], 3), 3, "PI 26.09", false, PiStart, PiEnd, arts, teams, features, objectives, epics, outcomes);
    }
}
