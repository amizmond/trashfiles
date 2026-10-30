using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.DepartmentPlanning.Views;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.DepartmentPlanning.Views;

public class WaterlineLayoutTests
{
    private const int CorePayments = 1;
    private const int DataPlatform = 2;
    private const int Argon = 1;
    private const int Boron = 2;
    private const int Zeta = 3;
    private const int GrowPayments = 1;
    private const int Resilience = 2;
    private const int InstantPayments = 10;
    private const int Tokenisation = 11;
    private const int DataLake = 12;
    private const int Fraud = 13;

    private static readonly ArtLineRef CorePaymentsLine = new(CorePayments, "Core Payments");

    private static PlanningFeatureTeam Link(int teamId, int sp, bool included = true, bool belowArtLine = false, int position = 1) =>
        new(teamId, sp, included, belowArtLine ? [CorePaymentsLine] : [], position, null, 0m);

    private static PlanningFeature Feature(int id, int? outcomeId, int? epicId, string status, string? rag, params PlanningFeatureTeam[] teams) =>
        new(id, $"PAY-{id}", $"Feature {id}", status, rag, null, id, 3, "PI 26.09", outcomeId, epicId, null, null, null, teams);

    private static DepartmentPlanningData Data()
    {
        var scope = new DepartmentPlanningScope(1, [CorePayments, DataPlatform], 3);
        var arts = new List<PlanningArt>
        {
            new(CorePayments, "Core Payments", "CP", 1, "Retail", ArtPrioritization.PortfolioEpic, ArtOrderMode.General, [Argon, Boron],
            [
                new ArtOrderEntry(InstantPayments, 1, true, ArtOrderOrigin.Saved),
                new ArtOrderEntry(Fraud, 2, true, ArtOrderOrigin.Saved),
                new ArtOrderEntry(ArtBoardItems.None, 3, true, ArtOrderOrigin.New),
                new ArtOrderEntry(Tokenisation, 4, false, ArtOrderOrigin.Saved),
            ]),
            new(DataPlatform, "Data Platform", "DP", 1, "Retail", ArtPrioritization.BusinessOutcome, ArtOrderMode.Empty, [Zeta], []),
        };
        var teams = new List<PlanningTeam>
        {
            new(Argon, "Argon", [CorePayments], 1m, 100d, 100m, 5, "Sprints"),
            new(Boron, "Boron", [CorePayments], 1m, 50d, 50m, 3, "Sprints"),
            new(Zeta, "Zeta", [DataPlatform], 1m, 40d, 40m, 2, "PI"),
        };
        var features = new List<PlanningFeature>
        {
            Feature(1, 100, InstantPayments, "In Progress", "Green", Link(Argon, 30), Link(Boron, 20)) with { DependencyIds = ["f1>f8"], RiskyDependencyIds = ["f1>f8"] },
            Feature(2, 101, InstantPayments, "Closed", null, Link(Argon, 40, position: 2)),
            Feature(3, 102, Tokenisation, "In Progress", "Red", Link(Argon, 50, belowArtLine: true, position: 4)),
            Feature(4, null, null, "Blocked", null, Link(Boron, 25, included: false, position: 3)),
            Feature(5, 103, DataLake, "To Do", "Amber", Link(Zeta, 30)),
            Feature(6, 100, InstantPayments, "To Do", null, Link(Argon, 45, position: 3)),
            Feature(7, 104, Fraud, "In Development", null, Link(Boron, 30, position: 2)),
        };
        var objectives = new List<PlanningObjective>
        {
            new(GrowPayments, "STRAT-1", "Grow payments", "In Progress", null, [CorePayments]),
            new(Resilience, "STRAT-2", "Resilience", "In Progress", null, [CorePayments]),
        };
        var epics = new List<PlanningEpic>
        {
            new(InstantPayments, "EPIC-10", "Instant payments", "In Progress", "Green", null, null, null, [GrowPayments]),
            new(Tokenisation, "EPIC-11", "Tokenisation", "In Progress", null, "Unfunded", null, null, [GrowPayments, Resilience]),
            new(DataLake, "EPIC-12", "Data lake", "Funnel", null, null, null, null, []),
            new(Fraud, "EPIC-13", "Fraud", "Analysis", null, null, null, null, [Resilience]),
        };
        var outcomes = new List<PlanningOutcome>
        {
            new(100, "BO-100", "Rails", "In Progress", "Amber", null, InstantPayments, null, null),
            new(101, "BO-101", "Notifications", "Done", null, null, InstantPayments, null, null),
            new(102, "BO-102", "Token vault", "In Progress", "Red", null, Tokenisation, null, null),
            new(103, "BO-103", "Ingestion", "To Do", null, null, DataLake, null, null),
            new(104, "BO-104", "Scoring", "In Progress", null, null, Fraud, null, null),
        };

        return new DepartmentPlanningData(scope, 3, "PI 26.09", false, null, null, arts, teams, features, objectives, epics, outcomes);
    }

    private static WaterlineChart Build(DepartmentPlanningData? data = null, bool teamScale = true)
    {
        data ??= Data();
        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown });
        return WaterlineLayout.Build(tree, data, teamScale);
    }

    private static double PlotHeight => WaterlineLayout.PlotBottom - WaterlineLayout.PlotTop;

    [Fact]
    public void One_column_per_art_row_with_segments_stacked_bottom_up_in_rank_order()
    {
        var chart = Build();

        Assert.Equal(["Core Payments", "Data Platform"], chart.Columns.Select(c => c.Name));
        var core = chart.Columns[0];
        Assert.Equal("art:1", core.Row.Id);
        Assert.Equal(150m, core.CapacitySp);
        Assert.Equal(165m, core.PlannedSp);
        Assert.Equal(15m, core.OverSp);
        Assert.True(core.HasLine);
        Assert.Equal(PlanningLevel.PortfolioEpic, core.ItemLevel);

        Assert.Equal([(1, 0m, 135m), (2, 135m, 165m), (3, 165m, 165m)], core.Segments.Select(s => (s.Position, s.From, s.To)));
        Assert.Equal([InstantPayments, Fraud, null], core.Segments.Select(s => s.Row.EntityId));
        Assert.Equal([1, 2, 3], core.Segments.Select(s => s.Rank!.Rank));
        Assert.Equal(1, core.ZeroPlannedItems);

        var instant = core.Segments[0];
        Assert.True(instant.Block.StoryPoints.Top < instant.Block.StoryPoints.Bottom);
        Assert.Equal(WaterlineLayout.PlotBottom, instant.Block.StoryPoints.Bottom, 6);
        Assert.Equal(instant.Block.StoryPoints.Top, core.Segments[1].Block.StoryPoints.Bottom, 6);
        Assert.Equal(0d, instant.Shade);
        Assert.Equal(1d, core.Segments[2].Shade);
    }

    [Fact]
    public void The_segment_where_capacity_runs_out_is_split_at_the_tick_and_later_segments_are_over()
    {
        var chart = Build();
        var core = chart.Columns[0];

        Assert.Null(core.Segments[0].Over);

        var fraud = core.Segments[1];
        Assert.True(fraud.Row.CapacityRunsOutHere);
        Assert.NotNull(fraud.Over);
        Assert.Equal(core.CapacityY!.StoryPoints, fraud.Over!.StoryPoints.Bottom, 6);
        Assert.Equal(fraud.Block.StoryPoints.Top, fraud.Over.StoryPoints.Top, 6);
        Assert.Equal(0.5d, fraud.Over.StoryPoints.Height / fraud.Block.StoryPoints.Height, 6);
        Assert.Equal(fraud.Over.Percent.Bottom, core.CapacityY.Percent, 6);

        var bucket = core.Segments[2];
        Assert.True(bucket.Row.OverCapacity);
        Assert.Equal(0m, bucket.Sp);
        Assert.Null(bucket.Over);
    }

    [Fact]
    public void Story_points_share_one_scale_and_percent_puts_every_capacity_tick_at_the_same_height()
    {
        var chart = Build();
        var core = chart.Columns[0];
        var data = chart.Columns[1];

        Assert.True(core.CapacityY!.StoryPoints < data.CapacityY!.StoryPoints);
        Assert.Equal(core.CapacityY.Percent, data.CapacityY.Percent, 6);
        Assert.Equal(core.CapacityY.Percent, core.Teams[0].CapacityY!.Percent, 6);

        var pixelsPerSp = PlotHeight / (double)(215m * 1.08m);
        Assert.Equal(WaterlineLayout.PlotBottom - 150d * pixelsPerSp, core.CapacityY.StoryPoints, 6);
        Assert.Equal(WaterlineLayout.PlotBottom - 1d / (1.65d * 1.08d) * PlotHeight, core.CapacityY.Percent, 6);
    }

    [Fact]
    public void Without_team_columns_the_percent_scale_follows_the_art_columns_only()
    {
        var withTeams = Build();
        var withoutTeams = Build(teamScale: false);

        Assert.True(withoutTeams.Columns[0].CapacityY!.Percent < withTeams.Columns[0].CapacityY!.Percent);
        Assert.Equal(withTeams.Columns[0].CapacityY!.StoryPoints, withoutTeams.Columns[0].CapacityY!.StoryPoints, 6);
        Assert.Equal(withoutTeams.Columns[0].CapacityY!.Percent, withoutTeams.Columns[1].CapacityY!.Percent, 6);
    }

    [Fact]
    public void A_ghost_extends_a_segment_by_its_unplanned_story_points()
    {
        var chart = Build();
        var core = chart.Columns[0];

        Assert.Null(core.Segments[0].Ghost);
        Assert.Null(core.Segments[1].Ghost);

        var bucket = core.Segments[2];
        Assert.Equal(25m, bucket.Row.Metrics.UnplannedSp);
        Assert.NotNull(bucket.Ghost);
        Assert.Equal(bucket.Block.StoryPoints.Top, bucket.Ghost!.StoryPoints.Bottom, 6);
        Assert.Equal(bucket.Block.Percent.Top, bucket.Ghost.Percent.Bottom, 6);
        var pixelsPerSp = PlotHeight / (double)(215m * 1.08m);
        Assert.Equal(25d * pixelsPerSp, bucket.Ghost.StoryPoints.Height, 6);
    }

    [Fact]
    public void Below_line_items_stack_above_the_art_line_as_hollow_blocks()
    {
        var chart = Build();
        var core = chart.Columns[0];

        var tokenisation = Assert.Single(core.BelowLine);
        Assert.True(tokenisation.IsBelowLine);
        Assert.Equal(Tokenisation, tokenisation.Row.EntityId);
        Assert.Equal(4, tokenisation.Position);
        Assert.Equal(165m, tokenisation.From);
        Assert.Equal(215m, tokenisation.To);
        Assert.Equal(core.LineY.StoryPoints, tokenisation.Block.StoryPoints.Bottom, 6);
        Assert.Equal(core.TopY.StoryPoints, tokenisation.Block.StoryPoints.Top, 6);
        Assert.Null(tokenisation.Over);
        Assert.Null(tokenisation.Ghost);
    }

    [Fact]
    public void Team_columns_follow_each_team_s_own_capacity()
    {
        var chart = Build();
        var core = chart.Columns[0];

        Assert.Equal(["Argon", "Boron"], core.Teams.Select(t => t.Team.Name));
        var argon = core.Teams[0];
        var boron = core.Teams[1];
        Assert.Equal(core.BarX, argon.X, 6);
        Assert.Equal(argon.Width, boron.Width, 6);
        Assert.Equal(core.BarX + core.BarWidth, boron.X + boron.Width, 6);

        Assert.Equal(115m, argon.PlannedSp);
        Assert.Equal(15m, argon.OverSp);
        var argonInstant = Assert.Single(argon.Segments);
        Assert.Equal((0m, 115m), (argonInstant.From, argonInstant.To));
        Assert.NotNull(argonInstant.Over);
        Assert.Equal(argon.CapacityY!.StoryPoints, argonInstant.Over!.StoryPoints.Bottom, 6);
        var argonBelow = Assert.Single(argon.BelowLine);
        Assert.Equal((115m, 165m), (argonBelow.From, argonBelow.To));
        Assert.Equal(argon.LineY.StoryPoints, argonBelow.Block.StoryPoints.Bottom, 6);

        Assert.Equal(50m, boron.PlannedSp);
        Assert.Equal([(0m, 20m), (20m, 50m)], boron.Segments.Select(s => (s.From, s.To)));
        Assert.All(boron.Segments, s => Assert.Null(s.Over));
        Assert.Empty(boron.BelowLine);
    }

    [Fact]
    public void Column_subtitle_names_the_ranking_mode()
    {
        var chart = Build();

        Assert.Equal("ranked by portfolio epic, general order", chart.Columns[0].Subtitle);
        Assert.True(chart.Columns[0].IsRanked);
        Assert.Equal(WaterlineLayout.NotRanked, chart.Columns[1].Subtitle);
        Assert.False(chart.Columns[1].IsRanked);
        Assert.False(chart.Columns[1].HasLine);
        Assert.Equal(PlanningLevel.BusinessOutcome, chart.Columns[1].ItemLevel);

        var data = Data();
        var piOrder = data.Arts[0] with { OrderMode = ArtOrderMode.PiOrder };
        Assert.Equal("ranked by portfolio epic, own PI order", WaterlineLayout.SubtitleOf(piOrder));
        Assert.Equal(WaterlineLayout.NotRanked, WaterlineLayout.SubtitleOf(data.Arts[0] with { Level = null }));
        Assert.Equal(WaterlineLayout.NotRanked, WaterlineLayout.SubtitleOf(null));
    }

    [Fact]
    public void Objective_legend_colours_items_by_the_first_strategic_objective_of_their_epic()
    {
        var chart = Build();

        Assert.Equal(["Grow payments", "Resilience", DepartmentPlanningTree.NoObjectiveTitle], chart.Objectives.Select(o => o.Title));
        Assert.Equal([GrowPayments, Resilience, null], chart.Objectives.Select(o => o.Id));
        Assert.Equal([2, 1, 2], chart.Objectives.Select(o => o.Items));
        Assert.Equal("STRAT-2", chart.Objectives[1].JiraId);

        var core = chart.Columns[0];
        Assert.Equal(0, core.Segments[0].Objective);
        Assert.Equal(1, core.Segments[1].Objective);
        Assert.Equal(2, core.Segments[2].Objective);
        Assert.Equal(0, core.BelowLine[0].Objective);
        Assert.Equal(2, chart.Columns[1].Segments[0].Objective);
        Assert.Equal(core.Segments[0].Objective, core.Teams[0].Segments[0].Objective);
    }

    [Fact]
    public void Axis_ticks_are_round_numbers_from_zero_to_the_top()
    {
        var chart = Build();

        Assert.Equal(["0", "50", "100", "150", "200"], chart.StoryPointAxis.Ticks.Select(t => t.Label));
        Assert.Equal(WaterlineLayout.PlotBottom, chart.StoryPointAxis.Ticks[0].Y, 6);
        Assert.True(chart.StoryPointAxis.Ticks.Zip(chart.StoryPointAxis.Ticks.Skip(1)).All(p => p.First.Y > p.Second.Y));
        Assert.Equal(chart.Columns[0].CapacityY!.StoryPoints, chart.StoryPointAxis.Ticks[3].Y, 6);

        Assert.Equal(["0%", "50%", "100%", "150%"], chart.PercentAxis.Ticks.Select(t => t.Label));
        Assert.Equal(chart.Columns[0].CapacityY!.Percent, chart.PercentAxis.Ticks[2].Y, 6);
        Assert.Equal(WaterlineLayout.StoryPointAxisTitle, chart.Axis(WaterlineScale.StoryPoints).Title);
        Assert.Equal(WaterlineLayout.PercentAxisTitle, chart.Axis(WaterlineScale.PercentOfCapacity).Title);
    }

    [Theory]
    [InlineData(232.2, 50)]
    [InlineData(10, 2)]
    [InlineData(7, 2)]
    [InlineData(6, 1)]
    [InlineData(1500, 250)]
    [InlineData(0, 1)]
    public void Nice_steps_are_one_two_two_and_a_half_or_five_times_a_power_of_ten(decimal max, decimal step)
    {
        Assert.Equal(step, WaterlineLayout.NiceStep(max));
    }

    [Fact]
    public void Columns_sit_side_by_side_after_the_axis_and_set_the_chart_width()
    {
        var chart = Build();

        Assert.Equal(WaterlineLayout.Height, chart.Height);
        Assert.Equal(WaterlineLayout.AxisWidth, chart.Columns[0].X);
        Assert.Equal(chart.Columns[0].X + chart.Columns[0].Width, chart.Columns[1].X, 6);
        Assert.Equal(chart.Columns[1].X + chart.Columns[1].Width + WaterlineLayout.RightPad, chart.Width, 6);
        foreach (var column in chart.Columns)
        {
            Assert.Equal(column.X + WaterlineLayout.ColumnPad, column.BarX, 6);
            Assert.True(column.GhostX > column.BarX + column.BarWidth);
            Assert.True(column.TextX > column.GhostX + WaterlineLayout.GhostWidth);
            Assert.True(column.TextX + WaterlineLayout.TextWidth <= column.X + column.Width);
        }
    }

    [Fact]
    public void An_art_without_capacity_has_no_tick_everything_is_over_and_no_share_of_capacity()
    {
        var data = Data();
        var teams = data.Teams.Select(t => t.ArtIds.Contains(CorePayments) ? t with { CapacityDays = 0d, CapacitySp = 0m } : t).ToList();
        var chart = Build(data with { Teams = teams });
        var core = chart.Columns[0];

        Assert.False(core.HasCapacity);
        Assert.Null(core.CapacityY);
        Assert.Equal(165m, core.OverSp);
        var instant = core.Segments[0];
        Assert.NotNull(instant.Over);
        Assert.Equal(instant.Block, instant.Over);
        Assert.Equal(WaterlineLayout.PlotBottom, core.TopY.Percent);
        Assert.Equal(0d, instant.Block.Percent.Height);
        Assert.All(core.BelowLine, b => Assert.Equal(0d, b.Block.Percent.Height));
        Assert.Null(core.Teams[0].CapacityY);
        Assert.All(core.Teams.SelectMany(t => t.Segments), s => Assert.Equal(0d, s.Block.Percent.Height));
        Assert.True(instant.Block.StoryPoints.Height > 0d);
    }

    [Fact]
    public void Each_team_column_marks_where_its_own_capacity_runs_out()
    {
        var core = Build().Columns[0];

        var fraud = core.Segments.Single(s => s.Row.EntityId == Fraud);
        Assert.True(fraud.IsOver);
        Assert.True(fraud.RunsOutHere);
        var argon = core.Teams.Single(t => t.Team.Id == Argon).Segments.Single(s => s.Row.EntityId == InstantPayments);
        Assert.True(argon.IsOver);
        Assert.True(argon.RunsOutHere);
        var boron = core.Teams.Single(t => t.Team.Id == Boron).Segments.Single(s => s.Row.EntityId == Fraud);
        Assert.False(boron.IsOver);
        Assert.False(boron.RunsOutHere);
    }

    [Fact]
    public void Not_planned_marks_stack_instead_of_overlapping()
    {
        var data = Data();
        data = data with { Features = data.Features.Select(f => f.Id == 6 ? f with { Teams = [Link(Argon, 45, included: false, position: 3)] } : f).ToList() };

        var core = Build(data).Columns[0];

        var ghosts = core.Segments.Where(s => s.Ghost is not null).ToList();
        Assert.Equal([InstantPayments, null], ghosts.Select(s => s.Row.EntityId));
        Assert.Equal(ghosts[0].Ghost!.StoryPoints.Top, ghosts[1].Ghost!.StoryPoints.Bottom, 6);
        Assert.True(core.TopY.StoryPoints <= ghosts[1].Ghost!.StoryPoints.Top);
    }

    [Fact]
    public void An_empty_tree_or_the_objectives_lens_gives_an_empty_chart()
    {
        var data = Data();

        Assert.True(WaterlineLayout.Build(PlanningTree.Empty, data).IsEmpty);
        Assert.True(WaterlineLayout.Build(DepartmentPlanningTree.Build(data, new PlanningOptions()), data).IsEmpty);
        Assert.Empty(WaterlineChart.Empty.Columns);
        Assert.Equal(WaterlineLayout.Height, WaterlineChart.Empty.Height);
    }

    [Fact]
    public void An_art_whose_features_are_all_filtered_out_keeps_its_capacity_and_its_full_drawdown()
    {
        var data = Data();
        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown, Search = "Ingestion" });
        var chart = WaterlineLayout.Build(tree, data);

        Assert.Equal(["Core Payments", "Data Platform"], chart.Columns.Select(c => c.Name));
        var core = chart.Columns[0];
        Assert.Equal(0, core.Items);
        Assert.False(core.IsEmpty);
        Assert.True(core.HasLine);
        Assert.Equal(165m, core.PlannedSp);
        Assert.Equal(15m, core.OverSp);
        Assert.True(core.LineY.StoryPoints < core.CapacityY!.StoryPoints);
        Assert.Equal(1, chart.Columns[1].Items);
        Assert.Equal(["0", "50", "100", "150"], chart.StoryPointAxis.Ticks.Select(t => t.Label));
    }

    [Fact]
    public void A_filter_leaves_the_shown_items_at_their_real_height_and_keeps_the_excess()
    {
        var data = Data();
        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions { Lens = PlanningLens.Drawdown, HideDone = true });
        var core = WaterlineLayout.Build(tree, data).Columns[0];

        Assert.Equal(165m, core.PlannedSp);
        Assert.Equal(15m, core.OverSp);
        var instant = core.Segments[0];
        Assert.Equal(40m, instant.From);
        Assert.Equal(135m, instant.To);
        var fraud = core.Segments[1];
        Assert.Equal(165m, fraud.To);
        Assert.NotNull(fraud.Over);
        var argon = core.Teams.Single(t => t.Team.Id == Argon);
        Assert.Equal(115m, argon.PlannedSp);
    }

    [Fact]
    public void Segments_describe_their_row_for_tooltips_and_twin_highlighting()
    {
        var chart = Build();
        var core = chart.Columns[0];

        var instant = core.Segments[0];
        Assert.Equal("2:10", instant.TwinKey);
        Assert.Equal("Green", instant.Rag);
        Assert.True(instant.IsSaved);
        Assert.Equal(135m, instant.Sp);

        var bucket = core.Segments[2];
        Assert.Null(bucket.TwinKey);
        Assert.False(bucket.IsSaved);
        Assert.True(bucket.Row.IsBucket);

        var ingestion = chart.Columns[1].Segments[0];
        Assert.Equal("3:103", ingestion.TwinKey);
        Assert.Null(ingestion.Rank);
        Assert.True(ingestion.IsSaved);
    }
}
