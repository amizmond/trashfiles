using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.DepartmentPlanning.Views;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.DepartmentPlanning.Views;

public class RiskQuadrantLayoutTests
{
    private const int CorePayments = 1;
    private const int DataPlatform = 2;
    private const int Argon = 1;
    private const int Boron = 2;
    private const int Zeta = 3;
    private const int GrowPayments = 1;
    private const int Resilience = 2;
    private const int EmptyObjective = 3;
    private const int InstantPayments = 10;
    private const int Tokenisation = 11;
    private const int DataLake = 12;
    private const int Fraud = 13;
    private const int EmptyEpic = 14;

    private static readonly ArtLineRef CorePaymentsLine = new(CorePayments, "Core Payments");

    private static PlanningFeatureTeam Link(int teamId, int sp, bool included = true, bool belowArtLine = false, int position = 1) =>
        new(teamId, sp, included, belowArtLine ? [CorePaymentsLine] : [], position, null, 0m);

    private static PlanningFeature Feature(int id, int? outcomeId, int? epicId, string status, string? rag, params PlanningFeatureTeam[] teams) =>
        new(id, $"PAY-{id}", $"Feature {id}", status, rag, null, id, 3, "PI 26.09", outcomeId, epicId, null, new DateTime(2026, 11, 1).AddDays(id), null, teams);

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
            Feature(1, 100, InstantPayments, "In Progress", "Green", Link(Argon, 30), Link(Boron, 20)) with { DependencyIds = ["f1>f8", "f9>f1"], RiskyDependencyIds = ["f1>f8"], HygieneFailures = 1 },
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
            new(EmptyObjective, "STRAT-3", "Empty objective", "To Do", null, [CorePayments]),
        };
        var epics = new List<PlanningEpic>
        {
            new(InstantPayments, "EPIC-10", "Instant payments", "In Progress", "Green", null, null, null, [GrowPayments]),
            new(Tokenisation, "EPIC-11", "Tokenisation", "In Progress", null, "Unfunded", null, null, [GrowPayments, Resilience]),
            new(DataLake, "EPIC-12", "Data lake", "Funnel", null, null, null, null, []),
            new(Fraud, "EPIC-13", "Fraud", "Analysis", null, null, null, null, [Resilience]),
            new(EmptyEpic, "EPIC-14", "Nothing planned", "Funnel", null, null, null, null, [EmptyObjective]),
        };
        var outcomes = new List<PlanningOutcome>
        {
            new(100, "BO-100", "Rails", "In Progress", "Amber", "Vendor late", InstantPayments, null, null),
            new(101, "BO-101", "Notifications", "Done", null, null, InstantPayments, null, null),
            new(102, "BO-102", "Token vault", "In Progress", "Red", null, Tokenisation, null, null),
            new(103, "BO-103", "Ingestion", "To Do", null, null, DataLake, null, null),
            new(104, "BO-104", "Scoring", "In Progress", null, null, Fraud, null, null),
            new(105, "BO-105", "Empty outcome", "Funnel", null, null, EmptyEpic, null, null),
        };

        return new DepartmentPlanningData(scope, 3, "PI 26.09", false, null, null, arts, teams, features, objectives, epics, outcomes);
    }

    private static DepartmentPlanningData HealthyData()
    {
        var data = Data();
        var features = data.Features.Select(f => f.Id switch
        {
            1 => f with { RiskyDependencyIds = [] },
            3 => f with { RagStatus = "Green" },
            4 => f with { Status = "To Do" },
            _ => f,
        }).ToList();
        return data with { Features = features };
    }

    private static RiskQuadrantLayout Build(DepartmentPlanningData? data = null, RiskQuadrantSettings? settings = null, PlanningOptions? options = null) =>
        RiskQuadrantLayout.Build(DepartmentPlanningTree.Build(data ?? Data(), options ?? new PlanningOptions()), settings ?? new RiskQuadrantSettings());

    private static RiskQuadrantPoint Point(RiskQuadrantLayout layout, int? entityId) => layout.Points.Single(p => p.EntityId == entityId);

    [Fact]
    public void Portfolio_epics_appear_once_with_the_first_path_and_buckets_are_kept_by_id()
    {
        var layout = Build();

        Assert.Equal([InstantPayments, Tokenisation, DataLake, Fraud, null], layout.Points.Select(p => p.EntityId));
        Assert.Equal("so:1/pe:11", Point(layout, Tokenisation).RowId);
        Assert.Equal(1, Point(layout, Tokenisation).SharedWith);
        Assert.Equal(1, layout.SharedCollapsed);
        Assert.Equal(5, layout.Candidates);

        var bucket = Point(layout, null);
        Assert.True(bucket.IsBucket);
        Assert.Equal("so:0/pe:0", bucket.RowId);
        Assert.Equal(DepartmentPlanningTree.NoEpicTitle, bucket.Title);
    }

    [Fact]
    public void Risk_share_counts_the_business_epics_at_risk_underneath()
    {
        var layout = Build();

        var instant = Point(layout, InstantPayments);
        Assert.Equal(3, instant.Features);
        Assert.Equal(1, instant.AtRiskFeatures);
        Assert.Equal(50m, instant.AtRiskSp);
        Assert.Equal(1d / 3d, instant.RiskValue, 6);
        Assert.Equal(1, instant.DependenciesAtRisk);
        Assert.Equal(0, instant.Blocked);
        Assert.Equal(0, instant.RedFeatures);

        var tokenisation = Point(layout, Tokenisation);
        Assert.Equal(1d, tokenisation.RiskValue);
        Assert.Equal(1, tokenisation.RedFeatures);

        var bucket = Point(layout, null);
        Assert.Equal(1d, bucket.RiskValue);
        Assert.Equal(1, bucket.Blocked);

        Assert.Equal(0d, Point(layout, Fraud).RiskValue);
        Assert.Equal(0d, Point(layout, DataLake).RiskValue);
        Assert.Equal(RiskQuadrantRiskAxis.AtRiskEpics, layout.RiskAxis);
        Assert.False(layout.RiskAxisSwitched);
    }

    [Fact]
    public void Risk_share_can_weigh_business_epics_by_demand_or_show_the_not_planned_share()
    {
        var byDemand = Build(settings: new RiskQuadrantSettings { RiskAxis = RiskQuadrantRiskAxis.AtRiskDemand });
        var notPlanned = Build(settings: new RiskQuadrantSettings { RiskAxis = RiskQuadrantRiskAxis.NotPlanned });

        Assert.Equal(50d / 135d, Point(byDemand, InstantPayments).RiskValue, 6);
        Assert.Equal(1d, Point(byDemand, Tokenisation).RiskValue);

        Assert.Equal(0d, Point(notPlanned, InstantPayments).RiskValue);
        Assert.Equal(1d, Point(notPlanned, Tokenisation).RiskValue);
        Assert.Equal(1d, Point(notPlanned, null).RiskValue);
        Assert.Equal(0d, Point(notPlanned, Fraud).RiskValue);
        Assert.False(notPlanned.RiskAxisSwitched);
    }

    [Fact]
    public void When_nothing_is_at_risk_the_risk_axis_switches_to_the_not_planned_share()
    {
        var layout = Build(HealthyData());

        Assert.True(layout.RiskAxisSwitched);
        Assert.Equal(RiskQuadrantRiskAxis.NotPlanned, layout.RiskAxis);
        Assert.Equal(1d, Point(layout, Tokenisation).RiskValue);
        Assert.Equal(0d, Point(layout, InstantPayments).RiskValue);
        Assert.Equal(0, Point(layout, Tokenisation).AtRiskFeatures);
    }

    [Fact]
    public void Size_is_the_demand_with_the_median_as_the_threshold_and_nice_ticks()
    {
        var layout = Build();

        Assert.Equal([135d, 50d, 30d, 30d, 25d], layout.Points.Select(p => p.SizeValue));
        Assert.Equal(30d, layout.SizeMedian);
        Assert.Equal(30d, layout.SizeThreshold);
        Assert.Equal(150d, layout.SizeMax);
        Assert.Equal(25d, layout.SizeThresholdStep);
        Assert.Equal([0d, 50d, 100d, 150d], layout.SizeTicks.Select(t => t.Value));
        Assert.Equal([0d, 0.25d, 0.5d, 0.75d, 1d], layout.RiskTicks.Select(t => t.Value));
        Assert.Equal(1d, layout.RiskMax);
        Assert.Equal(0.25d, layout.RiskThreshold);
        Assert.True(layout.SizeTicks.Zip(layout.SizeTicks.Skip(1)).All(pair => pair.First.Position < pair.Second.Position));
        Assert.True(layout.RiskTicks.Zip(layout.RiskTicks.Skip(1)).All(pair => pair.First.Position > pair.Second.Position));
        Assert.Equal(layout.SizeTicks[^1].Position, Point(layout, InstantPayments).X + ((layout.SizeTicks[1].Position - layout.SizeTicks[0].Position) * 15d / 50d), 6);
        Assert.All(layout.Points, p => Assert.InRange(p.X, layout.PlotLeft + p.Radius, layout.PlotRight - p.Radius));
        Assert.All(layout.Points, p => Assert.InRange(p.Y, layout.PlotTop + p.Radius, layout.PlotBottom - p.Radius));
    }

    [Fact]
    public void Quadrants_follow_the_thresholds()
    {
        var layout = Build();

        Assert.Equal(RiskQuadrantZone.DecideNow, Point(layout, InstantPayments).Zone);
        Assert.Equal(RiskQuadrantZone.DecideNow, Point(layout, Tokenisation).Zone);
        Assert.Equal(RiskQuadrantZone.Protect, Point(layout, Fraud).Zone);
        Assert.Equal(RiskQuadrantZone.Protect, Point(layout, DataLake).Zone);
        Assert.Equal(RiskQuadrantZone.Delegate, Point(layout, null).Zone);
        Assert.Equal(2, layout.CountIn(RiskQuadrantZone.DecideNow));
        Assert.Equal(0, layout.CountIn(RiskQuadrantZone.Watch));
    }

    [Fact]
    public void Threshold_steps_move_the_lines_and_stay_inside_the_axes()
    {
        var sizeUp = Build(settings: new RiskQuadrantSettings { SizeThresholdSteps = 1 });
        var sizeDown = Build(settings: new RiskQuadrantSettings { SizeThresholdSteps = -5 });
        var riskUp = Build(settings: new RiskQuadrantSettings { RiskThresholdSteps = 2 });
        var riskDown = Build(settings: new RiskQuadrantSettings { RiskThresholdSteps = -10 });

        Assert.Equal(55d, sizeUp.SizeThreshold);
        Assert.Equal(RiskQuadrantZone.Delegate, Point(sizeUp, Tokenisation).Zone);
        Assert.Equal(0d, sizeDown.SizeThreshold);
        Assert.All(sizeDown.Points, p => Assert.True(p.Zone is RiskQuadrantZone.DecideNow or RiskQuadrantZone.Protect));
        Assert.Equal(0.35d, riskUp.RiskThreshold, 6);
        Assert.Equal(RiskQuadrantZone.Protect, Point(riskUp, InstantPayments).Zone);
        Assert.Equal(0.05d, riskDown.RiskThreshold, 6);
        Assert.True(riskDown.RiskThresholdY < riskDown.PlotBottom);
        Assert.True(riskUp.RiskThresholdY < riskDown.RiskThresholdY);
    }

    [Fact]
    public void Bubble_radius_grows_with_the_square_root_of_demand()
    {
        var layout = Build();

        Assert.Equal(RiskQuadrantLayout.MaxRadius, Point(layout, InstantPayments).Radius);
        var expectedBucket = RiskQuadrantLayout.MinRadius + ((RiskQuadrantLayout.MaxRadius - RiskQuadrantLayout.MinRadius) * Math.Sqrt(25d / 135d));
        Assert.Equal(expectedBucket, Point(layout, null).Radius, 6);
        Assert.True(Point(layout, Tokenisation).Radius > Point(layout, Fraud).Radius);
        Assert.Equal(1d / 3d, Point(layout, InstantPayments).Progress, 6);
        Assert.Equal(Point(layout, InstantPayments).ArcCircumference / 3d, Point(layout, InstantPayments).ArcLength, 6);
    }

    [Fact]
    public void Colour_is_the_own_rag_or_the_worst_rag_underneath_and_hollow_marks_below_the_line()
    {
        var layout = Build();

        Assert.Equal("Green", Point(layout, InstantPayments).Rag);
        Assert.Equal("Red", Point(layout, Tokenisation).Rag);
        Assert.Equal("Amber", Point(layout, DataLake).Rag);
        Assert.Null(Point(layout, Fraud).Rag);
        Assert.True(Point(layout, Tokenisation).IsBelowArtLine);
        Assert.False(Point(layout, InstantPayments).IsBelowArtLine);
    }

    [Fact]
    public void Labels_go_to_the_decide_quadrant_and_the_selected_bubble_only()
    {
        var layout = Build(settings: new RiskQuadrantSettings { SelectedId = "so:2/pe:13" });

        Assert.Equal("EPIC-10", Point(layout, InstantPayments).Label);
        Assert.Equal("EPIC-11", Point(layout, Tokenisation).Label);
        Assert.Equal("EPIC-13", Point(layout, Fraud).Label);
        Assert.True(Point(layout, Fraud).IsSelected);
        Assert.Null(Point(layout, DataLake).Label);
        Assert.Null(Point(layout, null).Label);
        Assert.Single(layout.Points, p => p.IsSelected);
        Assert.All(layout.Points.Where(p => p.Label is not null), p =>
        {
            Assert.InRange(p.LabelX, layout.PlotLeft, layout.PlotRight);
            Assert.InRange(p.LabelY, layout.PlotTop, layout.PlotBottom);
        });
    }

    [Fact]
    public void Coincident_bubbles_are_nudged_apart_and_the_first_keeps_its_exact_place()
    {
        var layout = Build();

        var dataLake = Point(layout, DataLake);
        var fraud = Point(layout, Fraud);
        Assert.Equal(dataLake.SizeValue, fraud.SizeValue);
        Assert.Equal(dataLake.RiskValue, fraud.RiskValue);
        Assert.NotEqual((dataLake.X, dataLake.Y), (fraud.X, fraud.Y));
        Assert.True(Math.Abs(dataLake.X - fraud.X) <= 12d && Math.Abs(dataLake.Y - fraud.Y) <= 12d);
        Assert.Equal(dataLake.X, layout.SizeTicks[0].Position + ((layout.SizeTicks[1].Position - layout.SizeTicks[0].Position) * 30d / 50d), 6);
    }

    [Fact]
    public void The_decide_list_is_sorted_by_demand()
    {
        var layout = Build();

        Assert.Equal([InstantPayments, Tokenisation], layout.ListFor(RiskQuadrantZone.DecideNow).Select(p => p.EntityId));
        Assert.Equal([DataLake, Fraud], layout.ListFor(RiskQuadrantZone.Protect).Select(p => p.EntityId));
        Assert.Empty(layout.ListFor(RiskQuadrantZone.Watch));
    }

    [Fact]
    public void Share_of_capacity_sizes_by_planned_share_and_falls_back_to_demand_without_capacity()
    {
        var byShare = Build(settings: new RiskQuadrantSettings { SizeAxis = RiskQuadrantSizeAxis.ShareOfCapacity });
        var data = Data();
        var noCapacity = Build(data with { Teams = data.Teams.Select(t => t with { CapacitySp = 0m }).ToList() }, new RiskQuadrantSettings { SizeAxis = RiskQuadrantSizeAxis.ShareOfCapacity });

        Assert.Equal(RiskQuadrantSizeAxis.ShareOfCapacity, byShare.SizeAxis);
        Assert.False(byShare.SizeAxisSwitched);
        Assert.Equal(135d / 190d, Point(byShare, InstantPayments).SizeValue, 6);
        Assert.Equal(0d, Point(byShare, Tokenisation).SizeValue);
        Assert.Equal(0.8d, byShare.SizeMax, 6);
        Assert.Equal([0d, 0.2d, 0.4d, 0.6d, 0.8d], byShare.SizeTicks.Select(t => Math.Round(t.Value, 6)));

        Assert.Equal(RiskQuadrantSizeAxis.Demand, noCapacity.SizeAxis);
        Assert.True(noCapacity.SizeAxisSwitched);
        Assert.Equal(135d, Point(noCapacity, InstantPayments).SizeValue);
    }

    [Fact]
    public void Business_outcomes_can_be_the_bubbles()
    {
        var layout = Build(settings: new RiskQuadrantSettings { Level = PlanningLevel.BusinessOutcome });

        Assert.Equal(PlanningLevel.BusinessOutcome, layout.Level);
        Assert.Equal(6, layout.Points.Count);
        Assert.Equal(1, layout.SharedCollapsed);
        var rails = Point(layout, 100);
        Assert.Equal("so:1/pe:10/bo:100", rails.RowId);
        Assert.Equal(95m, rails.DemandSp);
        Assert.Equal(0.5d, rails.RiskValue);
        Assert.Equal("Amber", rails.Rag);
        Assert.Equal("so:1/pe:11/bo:102", Point(layout, 102).RowId);
    }

    [Fact]
    public void Items_without_business_epics_are_left_out_and_counted()
    {
        var layout = Build(options: new PlanningOptions { IncludeEmptyNodes = true });

        Assert.Equal(1, layout.ExcludedEmpty);
        Assert.Equal(6, layout.Candidates);
        Assert.Equal(5, layout.Points.Count);
        Assert.DoesNotContain(layout.Points, p => p.EntityId == EmptyEpic);
    }

    [Fact]
    public void Late_items_end_after_the_pi()
    {
        var layout = Build(settings: new RiskQuadrantSettings { PiEnd = new DateTime(2026, 11, 5) });
        var noPi = Build();

        Assert.True(Point(layout, InstantPayments).IsLate);
        Assert.False(Point(layout, Tokenisation).IsLate);
        Assert.All(noPi.Points, p => Assert.False(p.IsLate));
    }

    [Fact]
    public void An_empty_tree_gives_an_empty_layout_without_errors()
    {
        var layout = RiskQuadrantLayout.Build(PlanningTree.Empty, new RiskQuadrantSettings());

        Assert.Empty(layout.Points);
        Assert.False(layout.RiskAxisSwitched);
        Assert.False(layout.SizeAxisSwitched);
        Assert.Equal([0d], layout.SizeTicks.Select(t => t.Value));
        Assert.Equal(0.25d, layout.RiskThreshold);
        Assert.Empty(layout.ListFor(RiskQuadrantZone.DecideNow));
        Assert.True(layout.PlotRight > layout.PlotLeft && layout.PlotBottom > layout.PlotTop);
    }

    [Fact]
    public void Statistics_helpers_behave()
    {
        Assert.Equal(30d, RiskQuadrantLayout.Median([25d, 135d, 30d, 50d, 30d]));
        Assert.Equal(40d, RiskQuadrantLayout.Median([30d, 50d]));
        Assert.Equal(0d, RiskQuadrantLayout.Median([]));
        Assert.Equal(50d, RiskQuadrantLayout.NiceStep(135d, 5));
        Assert.Equal(10d, RiskQuadrantLayout.NiceStep(30d, 5));
        Assert.Equal(0.2d, RiskQuadrantLayout.NiceStep(0.71d, 5), 9);
        Assert.Equal(0.25d, RiskQuadrantLayout.NiceStep(1d, 4), 9);
        Assert.Equal(RiskQuadrantZone.DecideNow, RiskQuadrantLayout.ZoneOf(true, true));
        Assert.Equal(RiskQuadrantZone.Protect, RiskQuadrantLayout.ZoneOf(true, false));
        Assert.Equal(RiskQuadrantZone.Delegate, RiskQuadrantLayout.ZoneOf(false, true));
        Assert.Equal(RiskQuadrantZone.Watch, RiskQuadrantLayout.ZoneOf(false, false));
    }

    [Fact]
    public void A_zero_size_line_only_counts_items_with_some_size_as_big()
    {
        Assert.False(RiskQuadrantLayout.IsBig(0d, 0d));
        Assert.True(RiskQuadrantLayout.IsBig(0.01d, 0d));
        Assert.True(RiskQuadrantLayout.IsBig(30d, 30d));
        Assert.False(RiskQuadrantLayout.IsBig(29.9d, 30d));
    }

    [Fact]
    public void A_bubble_exactly_on_a_stepped_risk_line_counts_as_at_risk()
    {
        var layout = Build(settings: new RiskQuadrantSettings { RiskThresholdSteps = 1 });

        Assert.Equal(0.3d, layout.RiskThreshold);
        Assert.All(layout.Points.Where(p => Math.Abs(p.RiskValue - 0.3d) < 1e-9), p =>
            Assert.Contains(p.Zone, new[] { RiskQuadrantZone.DecideNow, RiskQuadrantZone.Delegate }));
    }

    [Fact]
    public void Selecting_a_shared_epic_under_its_other_objective_still_highlights_its_bubble()
    {
        var layout = Build(settings: new RiskQuadrantSettings { SelectedId = "so:2/pe:11" });

        Assert.True(Point(layout, Tokenisation).IsSelected);
        Assert.Equal("so:1/pe:11", Point(layout, Tokenisation).RowId);
        Assert.Single(layout.Points, p => p.IsSelected);
    }

    [Fact]
    public void Twenty_five_identical_bubbles_are_packed_inside_their_own_quadrant()
    {
        var features = new List<PlanningFeature>();
        var outcomes = new List<PlanningOutcome>();
        for (var i = 1; i <= 51; i++)
        {
            var (sp, rag) = i <= 25 ? (8, "Green") : i <= 50 ? (9, "Red") : (100, "Green");
            features.Add(Feature(i, 200 + i, InstantPayments, "In Progress", rag, Link(Argon, sp, position: i)));
            outcomes.Add(new PlanningOutcome(200 + i, $"BO-{200 + i}", $"Outcome {i}", "In Progress", null, null, InstantPayments, null, null));
        }

        var data = Data() with { Features = features, Outcomes = outcomes };
        var layout = Build(data, new RiskQuadrantSettings { Level = PlanningLevel.BusinessOutcome, RiskThresholdSteps = 14 });

        Assert.Equal(9d, layout.SizeThreshold);
        Assert.Equal(0.95d, layout.RiskThreshold, 6);
        Assert.Equal(25, layout.CountIn(RiskQuadrantZone.Watch));
        Assert.Equal(25, layout.CountIn(RiskQuadrantZone.DecideNow));
        Assert.Equal(1, layout.CountIn(RiskQuadrantZone.Protect));
        Assert.All(layout.Points, p =>
        {
            Assert.Equal(p.Zone is RiskQuadrantZone.DecideNow or RiskQuadrantZone.Protect, p.X >= layout.SizeThresholdX);
            Assert.Equal(p.Zone is RiskQuadrantZone.DecideNow or RiskQuadrantZone.Delegate, p.Y <= layout.RiskThresholdY);
            Assert.InRange(p.X, layout.PlotLeft + p.Radius, layout.PlotRight - p.Radius);
            Assert.InRange(p.Y, layout.PlotTop + p.Radius, layout.PlotBottom - p.Radius);
        });

        foreach (var group in layout.Points.GroupBy(p => p.SizeValue).Where(g => g.Count() > 1))
        {
            var first = group.First();
            Assert.All(group, p => Assert.True(Math.Sqrt(Math.Pow(p.X - first.X, 2) + Math.Pow(p.Y - first.Y, 2)) <= 3d * RiskQuadrantLayout.MaxRadius));
            var points = group.ToList();
            for (var i = 0; i < points.Count; i++)
            {
                for (var j = i + 1; j < points.Count; j++)
                {
                    Assert.True(Math.Sqrt(Math.Pow(points[i].X - points[j].X, 2) + Math.Pow(points[i].Y - points[j].Y, 2)) >= 1d, $"bubbles {i} and {j} of the {first.SizeValue} group sit on top of each other");
                }
            }
        }
    }

    [Fact]
    public void Share_of_capacity_shows_as_many_decimals_as_the_step_needs()
    {
        Assert.Equal("40%", RiskQuadrantLayout.SharePercent(0.4d, 0.1d));
        Assert.Equal("1%", RiskQuadrantLayout.SharePercent(0.01d, 0.01d));
        Assert.Equal("0.5%", RiskQuadrantLayout.SharePercent(0.005d, 0.005d));
        Assert.Equal("0.4%", RiskQuadrantLayout.SharePercent(0.0036d, 0.001d));
        Assert.Equal("0.36%", RiskQuadrantLayout.SharePercent(0.0036d, 0.0005d));
        Assert.Equal("0.36%", RiskQuadrantLayout.SharePercent(0.0036d, 0.0001d));
        Assert.Equal("0.005%", RiskQuadrantLayout.SharePercent(0.00005d, 0.00001d));
        Assert.Equal("0%", RiskQuadrantLayout.SharePercent(0d, 0.002d));
        Assert.Equal("71%", RiskQuadrantLayout.SharePercent(0.71d, 0d));
        Assert.Equal("7.5%", RiskQuadrantLayout.SharePercent(0.075d, 0.025d));
        Assert.Equal("2.5%", RiskQuadrantLayout.SharePercent(0.025d, 0.0125d));
        Assert.Equal("1.25%", RiskQuadrantLayout.SharePercent(0.0125d, 0.0125d));
        Assert.Equal("0.75%", RiskQuadrantLayout.SharePercent(0.0075d, 0.0025d));

        var data = Data();
        var layout = Build(data with { Teams = data.Teams.Select(t => t with { CapacitySp = t.CapacitySp * 100m }).ToList() }, new RiskQuadrantSettings { SizeAxis = RiskQuadrantSizeAxis.ShareOfCapacity });

        Assert.Equal(["0%", "0.2%", "0.4%", "0.6%", "0.8%"], layout.SizeTicks.Select(t => RiskQuadrantLayout.SharePercent(t.Value, layout.SizeThresholdStep)));
        Assert.Equal("0.2%", RiskQuadrantLayout.SharePercent(layout.SizeThreshold, layout.SizeThresholdStep));
        Assert.Equal("0.3%", RiskQuadrantLayout.SharePercent(layout.SizeThreshold + layout.SizeThresholdStep, layout.SizeThresholdStep));
    }

    [Fact]
    public void Share_values_keep_two_significant_digits_so_small_shares_never_read_zero()
    {
        Assert.Equal("0.36%", RiskQuadrantLayout.ShareValuePercent(0.0036d, 0.01d));
        Assert.Equal("0.35%", RiskQuadrantLayout.ShareValuePercent(0.0035d, 0.01d));
        Assert.Equal("3.4%", RiskQuadrantLayout.ShareValuePercent(0.034d, 0.01d));
        Assert.Equal("12%", RiskQuadrantLayout.ShareValuePercent(0.12d, 0.01d));
        Assert.Equal("1.25%", RiskQuadrantLayout.ShareValuePercent(0.0125d, 0.0125d));
        Assert.Equal("0%", RiskQuadrantLayout.ShareValuePercent(0d, 0.01d));
        Assert.Equal("0.000001%", RiskQuadrantLayout.ShareValuePercent(0.00000001d, 0.01d));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    public void A_large_group_of_identical_bubbles_never_puts_two_on_the_same_spot(int count)
    {
        var features = new List<PlanningFeature>();
        var outcomes = new List<PlanningOutcome>();
        for (var i = 1; i <= count + 1; i++)
        {
            var sp = i <= count ? 8 : 100;
            features.Add(Feature(i, 200 + i, InstantPayments, "In Progress", "Green", Link(Argon, sp, position: i)));
            outcomes.Add(new PlanningOutcome(200 + i, $"BO-{200 + i}", $"Outcome {i}", "In Progress", null, null, InstantPayments, null, null));
        }

        var layout = Build(Data() with { Features = features, Outcomes = outcomes }, new RiskQuadrantSettings { Level = PlanningLevel.BusinessOutcome });

        var points = layout.Points.Where(p => p.SizeValue == 8d).ToList();
        Assert.Equal(count, points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            for (var j = i + 1; j < points.Count; j++)
            {
                Assert.True(Math.Sqrt(Math.Pow(points[i].X - points[j].X, 2) + Math.Pow(points[i].Y - points[j].Y, 2)) >= 1d, $"bubbles {i} and {j} sit on top of each other");
            }
        }
    }

    [Fact]
    public void Done_features_do_not_count_as_reasons_for_being_at_risk()
    {
        var data = Data();
        var features = data.Features.Select(f => f.Id switch
        {
            1 => f with { DependencyIds = ["f1>f8", "f9>f1", "f1>f6"], RiskyDependencyIds = ["f1>f8", "f1>f6"] },
            2 => f with { RagStatus = "Red", DependencyIds = ["f2>f7"], RiskyDependencyIds = ["f2>f7"] },
            6 => f with { DependencyIds = ["f1>f6"], RiskyDependencyIds = ["f1>f6"] },
            _ => f,
        }).ToList();

        var epics = Build(data with { Features = features });
        var outcomes = Build(data with { Features = features }, new RiskQuadrantSettings { Level = PlanningLevel.BusinessOutcome });

        var instant = Point(epics, InstantPayments);
        Assert.Equal(1, instant.Done);
        Assert.Equal(2, instant.AtRiskFeatures);
        Assert.Equal(2, instant.DependenciesAtRisk);
        Assert.Equal(0, instant.RedFeatures);
        Assert.Equal(0, instant.Blocked);

        var notifications = Point(outcomes, 101);
        Assert.Equal(1, notifications.Done);
        Assert.Equal(0, notifications.AtRiskFeatures);
        Assert.Equal(0, notifications.DependenciesAtRisk);
        Assert.Equal(0, notifications.RedFeatures);
    }
}
