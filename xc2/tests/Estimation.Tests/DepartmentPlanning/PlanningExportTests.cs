using Estimation.Components.DepartmentPlanning;
using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.Train.Models;
using Estimation.Excel;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningExportTests
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

    private static readonly DateTime Exported = new(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);

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
            new(DataPlatform, "Data Platform", "DP", 1, "Retail", ArtPrioritization.PortfolioEpic, ArtOrderMode.General, [Zeta],
            [
                new ArtOrderEntry(DataLake, 1, true, ArtOrderOrigin.Saved),
                new ArtOrderEntry(Fraud, 5, true, ArtOrderOrigin.Saved),
            ]),
        };
        var teams = new List<PlanningTeam>
        {
            new(Argon, "Argon", [CorePayments], 1m, 100d, 100m, 5, "Sprints"),
            new(Boron, "Boron", [CorePayments], 1m, 50d, 50m, 3, "Sprints"),
            new(Zeta, "Zeta", [DataPlatform], 1m, 40d, 40m, 2, "PI"),
        };
        var features = new List<PlanningFeature>
        {
            Feature(1, 100, InstantPayments, "In Progress", "Green", Link(Argon, 30, position: 1), Link(Boron, 20, position: 1)),
            Feature(2, 101, InstantPayments, "Closed", null, Link(Argon, 40, position: 2)),
            Feature(3, 102, Tokenisation, "In Progress", "Red", Link(Argon, 50, belowArtLine: true, position: 4)),
            Feature(4, null, null, "Blocked", null, Link(Boron, 25, included: false, position: 3)),
            Feature(5, 103, DataLake, "To Do", "Amber", Link(Zeta, 30, position: 1)),
            Feature(6, 100, InstantPayments, "To Do", null, Link(Argon, 45, position: 3)),
            Feature(7, 104, Fraud, "In Development", null, Link(Boron, 30, position: 2)) with { HygieneFailures = 3 },
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

    private static PlanningTree Hierarchy() => DepartmentPlanningTree.Build(Data(), new PlanningOptions());

    private static PlanningTree Drawdown() => DepartmentPlanningTree.Build(Data(), new PlanningOptions { Lens = PlanningLens.Drawdown });

    private static DepartmentPlanningExportRow Single(IEnumerable<DepartmentPlanningExportRow> rows, string level, string title) =>
        Assert.Single(rows, r => r.Level == level && r.Title == title);

    [Fact]
    public void Drawdown_rows_name_their_art_on_every_row_below_it()
    {
        var rows = PlanningExport.Rows(Drawdown(), false);

        var dataPlatform = rows.FindIndex(r => r.Level == "ART" && r.Title == "Data Platform");
        Assert.Equal("ART", rows[0].Level);
        Assert.Equal("Core Payments", rows[0].Title);
        Assert.True(dataPlatform > 1);
        Assert.All(rows.Take(dataPlatform), r => Assert.Equal("Core Payments", r.Art));
        Assert.All(rows.Skip(dataPlatform), r => Assert.Equal("Data Platform", r.Art));
        Assert.Contains(rows.Take(dataPlatform), r => r.Level == "Feature");
        Assert.All(rows, r => Assert.Null(r.StrategicObjective));
        Assert.All(rows, r => Assert.Null(r.Arts));
    }

    [Fact]
    public void Drawdown_writes_each_art_line_in_place_with_the_art_s_capacity()
    {
        var rows = PlanningExport.Rows(Drawdown(), false);

        var line = rows.FindIndex(r => r.Level == PlanningExport.LineLevel && r.Art == "Core Payments");
        Assert.True(line > 0);
        Assert.Equal("Core Payments line", rows[line].Title);
        Assert.Equal(150d, rows[line].Capacity);
        Assert.Null(rows[line].Demand);
        Assert.Null(rows[line].Teams);
        Assert.Equal("Tokenisation", rows[line + 1].Title);
        Assert.DoesNotContain(rows.Take(line), r => r.Title == "Tokenisation");
        Assert.Single(rows, r => r.Level == PlanningExport.LineLevel && r.Art == "Data Platform");
    }

    [Fact]
    public void Drawdown_rank_is_a_number_and_the_hierarchy_rank_names_each_art()
    {
        var drawdown = PlanningExport.Rows(Drawdown(), false);
        var hierarchy = PlanningExport.Rows(Hierarchy(), false);

        var instant = Single(drawdown, "Portfolio epic", "Instant payments");
        Assert.Equal(1, instant.Rank);
        Assert.Null(instant.Ranks);
        Assert.Equal(2, Single(drawdown, "Portfolio epic", "Fraud").Rank);

        var fraud = Single(hierarchy, "Portfolio epic", "Fraud");
        Assert.Equal("CP 2, DP 5", fraud.Ranks);
        Assert.Null(fraud.Rank);
    }

    [Fact]
    public void Drawdown_shows_the_drawdown_and_where_capacity_runs_out()
    {
        var rows = PlanningExport.Rows(Drawdown(), false);

        var art = Single(rows, "ART", "Core Payments");
        Assert.Equal(165d, art.Drawdown);
        Assert.Equal(PlanningExport.OverCapacity, art.OverCapacity);
        Assert.Equal(150d, art.Capacity);

        var instant = Single(rows, "Portfolio epic", "Instant payments");
        Assert.Equal(135d, instant.Drawdown);
        Assert.Null(instant.OverCapacity);

        var fraud = Single(rows, "Portfolio epic", "Fraud");
        Assert.Equal(165d, fraud.Drawdown);
        Assert.Equal(PlanningExport.RunsOutHere, fraud.OverCapacity);

        var bucket = Single(rows, "Portfolio epic", DepartmentPlanningTree.NoEpicTitle);
        Assert.Equal(165d, bucket.Drawdown);
        Assert.Equal(PlanningExport.OverCapacity, bucket.OverCapacity);

        var tokenisation = Single(rows, "Portfolio epic", "Tokenisation");
        Assert.Null(tokenisation.Drawdown);
        Assert.Null(tokenisation.OverCapacity);
        Assert.Equal("Below", tokenisation.Line);
    }

    [Fact]
    public void A_feature_row_counts_itself_once_in_the_hygiene_column_however_many_rules_it_fails()
    {
        var hierarchy = PlanningExport.Rows(Hierarchy(), false);
        var drawdown = PlanningExport.Rows(Drawdown(), false);

        Assert.Equal(1, Single(hierarchy, "Feature", "Feature 7").HygieneFailingFeatures);
        Assert.Equal(1, Single(drawdown, "Feature", "Feature 7").HygieneFailingFeatures);
        Assert.Equal(0, Single(hierarchy, "Feature", "Feature 1").HygieneFailingFeatures);
        Assert.Equal(1, Single(hierarchy, "Strategic objective", "Resilience").HygieneFailingFeatures);
    }

    [Fact]
    public void Hierarchy_rows_keep_their_parents_also_under_funding_and_arts()
    {
        var rows = PlanningExport.Rows(Hierarchy(), false);

        var tokenisation = rows.First(r => r.Level == "Portfolio epic" && r.Title == "Tokenisation" && r.StrategicObjective == "STRAT-1 Grow payments");
        Assert.Equal("STRAT-2", tokenisation.AlsoUnder);
        Assert.Equal("Unfunded", tokenisation.Funding);
        Assert.Equal("Core Payments", tokenisation.Arts);
        Assert.Null(tokenisation.Art);

        var feature = Single(rows, "Feature", "Feature 7");
        Assert.Equal("STRAT-2 Resilience", feature.StrategicObjective);
        Assert.Equal("EPIC-13 Fraud", feature.PortfolioEpic);
        Assert.Equal("BO-104 Scoring", feature.BusinessOutcome);
        Assert.Equal("PAY-7 Feature 7", feature.BusinessEpic);
        Assert.DoesNotContain(rows, r => r.Level == PlanningExport.LineLevel);
    }

    [Fact]
    public void The_total_row_counts_each_feature_once()
    {
        var hierarchy = Hierarchy();
        var drawdown = Drawdown();

        var total = PlanningExport.Total(hierarchy, true);

        Assert.Equal(PlanningExport.TotalLevel, total.Level);
        Assert.Equal("7 of 7 features shown, each counted once", total.Title);
        Assert.Equal(7, total.BusinessEpics);
        Assert.Equal(270d, total.Demand);
        Assert.Equal(195d, total.Planned);
        Assert.Equal(75d, total.NotPlanned);
        Assert.Equal(195d / 190d * 100, total.ShareOfCapacityPercent!.Value, 6);
        Assert.Equal(3, total.Teams);
        Assert.Equal("2", total.Arts);
        Assert.Null(total.Capacity);
        Assert.Equal([115d, 50d, 30d], total.TeamPlanned);
        Assert.Empty(total.TeamDrawdown);
        Assert.True(PlanningExport.Rows(hierarchy, false).Where(r => r.Level == "Strategic objective").Sum(r => r.Demand) > total.Demand);

        var drawdownTotal = PlanningExport.Total(drawdown, false);
        Assert.Equal(270d, drawdownTotal.Demand);
        Assert.Equal(190d, drawdownTotal.Capacity);
        Assert.Null(drawdownTotal.Arts);
        Assert.Empty(drawdownTotal.TeamPlanned);
    }

    [Fact]
    public void Team_columns_appear_only_when_the_grid_shows_one_column_per_team()
    {
        var tree = Drawdown();

        var off = PlanningExport.Rows(tree, false);
        Assert.All(off, r => Assert.Empty(r.TeamPlanned));
        Assert.All(off, r => Assert.Empty(r.TeamDrawdown));
        Assert.Empty(PlanningExport.Info(tree, false, "Retail", "PI 26.09", "none", Exported).TeamNames);

        var on = PlanningExport.Rows(tree, true);
        var instant = Single(on, "Portfolio epic", "Instant payments");
        Assert.Equal([115d, 20d, null], instant.TeamPlanned);
        Assert.Equal([115d, 20d, null], instant.TeamDrawdown);
        var fraud = Single(on, "Portfolio epic", "Fraud");
        Assert.Equal([0d, 30d, null], fraud.TeamPlanned);
        Assert.Equal([115d, 50d, null], fraud.TeamDrawdown);
        var line = Single(on, PlanningExport.LineLevel, "Core Payments line");
        Assert.Empty(line.TeamPlanned);
        Assert.Equal(["Argon", "Boron", "Zeta"], PlanningExport.Info(tree, true, "Retail", "PI 26.09", "none", Exported).TeamNames);

        var hierarchyFeature = Single(PlanningExport.Rows(Hierarchy(), true), "Feature", "Feature 1");
        Assert.Equal([30d, 20d, null], hierarchyFeature.TeamPlanned);
        Assert.Empty(hierarchyFeature.TeamDrawdown);
    }

    [Fact]
    public void The_info_follows_the_lens()
    {
        var drawdown = PlanningExport.Info(Drawdown(), false, "Retail", "PI 26.09", "done hidden", Exported);
        var hierarchy = PlanningExport.Info(Hierarchy(), false, "Retail", "PI 26.09", "none", Exported);

        Assert.True(drawdown.Drawdown);
        Assert.Equal("Capacity drawdown", drawdown.View);
        Assert.Equal("done hidden", drawdown.Filters);
        Assert.Equal(190d, drawdown.Capacity);
        Assert.False(hierarchy.Drawdown);
        Assert.Equal("Hierarchy", hierarchy.View);
        Assert.Equal("Retail", hierarchy.Department);
        Assert.Equal(Exported, hierarchy.ExportedUtc);
    }

    [Fact]
    public void A_drawdown_workbook_carries_the_line_row_and_the_total_row()
    {
        var tree = Drawdown();
        var bytes = DepartmentPlanningExportService.Generate(
            PlanningExport.Info(tree, true, "Retail", "PI 26.09", "none", Exported),
            PlanningExport.Rows(tree, true),
            PlanningExport.Total(tree, true));

        using var stream = new MemoryStream(bytes);
        var (headers, rows) = ExcelSheetReader.Read(stream, DepartmentPlanningExportService.SheetName);

        Assert.Equal("ART", headers[1]);
        Assert.Contains("Argon drawdown (SP)", headers);
        Assert.Contains(rows, r => r[0] == PlanningExport.LineLevel && r[1] == "Core Payments");
        Assert.Equal(PlanningExport.TotalLevel, rows[^1][0]);
    }
}
