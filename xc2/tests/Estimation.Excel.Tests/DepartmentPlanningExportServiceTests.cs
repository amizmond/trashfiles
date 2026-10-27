namespace Estimation.Excel.Tests;

public class DepartmentPlanningExportServiceTests
{
    private static readonly DateTime Exported = new(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);

    private static DepartmentPlanningExportInfo Info(bool drawdown = false, params string[] teams) =>
        new("Retail Banking", "PI 26.09", drawdown ? "Capacity drawdown" : "Objectives", "none", 480, Exported, drawdown, teams);

    private static DepartmentPlanningExportRow Row(string level, string title, double demand, double planned) => new()
    {
        Level = level,
        StrategicObjective = "STRAT-1 Grow payments",
        PortfolioEpic = level == "Strategic objective" ? null : "EPIC-10 Instant payments",
        JiraId = "EPIC-10",
        Title = title,
        Status = "In Progress",
        Rag = "Amber",
        Ranks = "CP 1, DP 5",
        Line = "Above",
        Arts = "Core Payments",
        AlsoUnder = "STRAT-2",
        Teams = 2,
        BusinessEpics = 4,
        Done = 1,
        ProgressPercent = 25,
        Demand = demand,
        Planned = planned,
        NotPlanned = demand - planned,
        ShareOfCapacityPercent = 0.28 * 100,
        Dependencies = 3,
        DependenciesAtRisk = 1,
        HygieneFailingFeatures = 2,
        Funding = "Funded",
        TargetEnd = new DateTime(2026, 11, 30),
        TeamPlanned = [80, 55],
    };

    private static string Value(List<string> headers, List<string> row, string header)
    {
        var index = headers.IndexOf(header);
        Assert.True(index >= 0, $"No column {header}");
        return index < row.Count ? row[index] : "";
    }

    [Fact]
    public void The_export_has_the_planning_sheet_and_an_info_sheet()
    {
        var bytes = DepartmentPlanningExportService.Generate(Info(), []);

        Assert.Equal(new[] { "Department planning", "Info" }, WorkbookProbe.SheetNames(bytes));
    }

    [Fact]
    public void Headers_spell_out_one_column_per_level_and_the_planning_numbers()
    {
        var (headers, _) = WorkbookProbe.Read(DepartmentPlanningExportService.Generate(Info(), []), "Department planning");

        Assert.Equal(DepartmentPlanningExportService.HierarchyHeaders, headers);
        Assert.Equal("Strategic Objective", headers[1]);
        Assert.Equal("Feature", headers[4]);
        Assert.Equal("Planned (SP)", headers[18]);
        Assert.Contains("Also under", headers);
        Assert.Contains("Funding", headers);
        Assert.DoesNotContain("Capacity (SP)", headers);
        Assert.DoesNotContain("Drawdown (SP)", headers);
    }

    [Fact]
    public void The_drawdown_headers_name_the_art_the_drawdown_and_where_capacity_runs_out()
    {
        var (headers, _) = WorkbookProbe.Read(DepartmentPlanningExportService.Generate(Info(drawdown: true), []), "Department planning");

        Assert.Equal(DepartmentPlanningExportService.DrawdownHeaders, headers);
        Assert.Equal("ART", headers[1]);
        Assert.Contains("Drawdown (SP)", headers);
        Assert.Contains("Over capacity", headers);
        Assert.Contains("Capacity (SP)", headers);
        Assert.DoesNotContain("Strategic Objective", headers);
        Assert.DoesNotContain("ARTs", headers);
    }

    [Fact]
    public void Team_columns_follow_the_team_names_one_per_team_in_the_hierarchy_and_two_in_the_drawdown()
    {
        var (hierarchy, _) = WorkbookProbe.Read(DepartmentPlanningExportService.Generate(Info(false, "Argon", "Boron"), []), "Department planning");
        var (drawdown, _) = WorkbookProbe.Read(DepartmentPlanningExportService.Generate(Info(true, "Argon", "Boron"), []), "Department planning");

        Assert.Equal(["Argon planned (SP)", "Boron planned (SP)"], hierarchy.Skip(DepartmentPlanningExportService.HierarchyHeaders.Length));
        Assert.Equal(
            ["Argon planned (SP)", "Argon drawdown (SP)", "Boron planned (SP)", "Boron drawdown (SP)"],
            drawdown.Skip(DepartmentPlanningExportService.DrawdownHeaders.Length));
    }

    [Fact]
    public void Rows_keep_their_hierarchy_columns_and_numbers()
    {
        var bytes = DepartmentPlanningExportService.Generate(Info(false, "Argon", "Boron"),
        [
            Row("Strategic objective", "Grow payments", 185, 135),
            Row("Portfolio epic", "Instant payments", 135, 135),
        ]);

        var (headers, rows) = WorkbookProbe.Read(bytes, "Department planning");

        Assert.Equal(2, rows.Count);
        Assert.Equal("Strategic objective", rows[0][0]);
        Assert.Equal("STRAT-1 Grow payments", rows[0][1]);
        Assert.Equal("", rows[0][2]);
        Assert.Equal("EPIC-10 Instant payments", rows[1][2]);
        Assert.Equal("CP 1, DP 5", Value(headers, rows[0], "Rank"));
        Assert.Equal("STRAT-2", Value(headers, rows[0], "Also under"));
        Assert.Equal("185", Value(headers, rows[0], "Demand (SP)"));
        Assert.Equal("135", Value(headers, rows[0], "Planned (SP)"));
        Assert.Equal("50", Value(headers, rows[0], "Not planned (SP)"));
        Assert.Equal("28", Value(headers, rows[0], "Share of capacity %"));
        Assert.Equal("3", Value(headers, rows[0], "Dependencies"));
        Assert.Equal("2", Value(headers, rows[0], "Hygiene issues"));
        Assert.Equal("Funded", Value(headers, rows[0], "Funding"));
        Assert.Equal("80", Value(headers, rows[0], "Argon planned (SP)"));
        Assert.Equal("55", Value(headers, rows[0], "Boron planned (SP)"));
    }

    [Fact]
    public void Drawdown_rows_carry_the_art_the_drawdown_the_over_capacity_point_and_the_team_drawdown()
    {
        var row = Row("Portfolio epic", "Fraud", 30, 30) with
        {
            Art = "Core Payments",
            Ranks = null,
            Rank = 12,
            Drawdown = 165,
            OverCapacity = "Runs out here",
            TeamPlanned = [0, 30],
            TeamDrawdown = [115, 50],
        };
        var bytes = DepartmentPlanningExportService.Generate(Info(true, "Argon", "Boron"), [row]);

        var (headers, rows) = WorkbookProbe.Read(bytes, "Department planning");

        var only = Assert.Single(rows);
        Assert.Equal("Core Payments", Value(headers, only, "ART"));
        Assert.Equal("EPIC-10 Instant payments", Value(headers, only, "Portfolio Epic"));
        Assert.Equal("12", Value(headers, only, "Rank"));
        Assert.Equal("165", Value(headers, only, "Drawdown (SP)"));
        Assert.Equal("Runs out here", Value(headers, only, "Over capacity"));
        Assert.Equal("", Value(headers, only, "Capacity (SP)"));
        Assert.Equal("0", Value(headers, only, "Argon planned (SP)"));
        Assert.Equal("115", Value(headers, only, "Argon drawdown (SP)"));
        Assert.Equal("30", Value(headers, only, "Boron planned (SP)"));
        Assert.Equal("50", Value(headers, only, "Boron drawdown (SP)"));
    }

    [Fact]
    public void The_drawdown_rank_is_a_number_cell()
    {
        var bytes = DepartmentPlanningExportService.Generate(Info(drawdown: true), [Row("Portfolio epic", "Fraud", 30, 30) with { Rank = 12 }]);
        var column = Array.IndexOf(DepartmentPlanningExportService.DrawdownHeaders, "Rank");

        var cell = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Department planning", $"{(char)('A' + column)}2"));

        Assert.NotNull(cell);
        Assert.Equal(CellValues.Number, cell!.DataType!.Value);
        Assert.Equal("12", cell.CellValue?.Text);
    }

    [Fact]
    public void A_line_row_leaves_the_numbers_empty_and_shows_the_capacity()
    {
        var line = new DepartmentPlanningExportRow { Level = "ART line", Art = "Core Payments", Title = "Core Payments line", Capacity = 150 };
        var bytes = DepartmentPlanningExportService.Generate(Info(true, "Argon"), [line]);

        var (headers, rows) = WorkbookProbe.Read(bytes, "Department planning");

        var only = Assert.Single(rows);
        Assert.Equal("ART line", Value(headers, only, "Level"));
        Assert.Equal("Core Payments", Value(headers, only, "ART"));
        Assert.Equal("Core Payments line", Value(headers, only, "Title"));
        Assert.Equal("150", Value(headers, only, "Capacity (SP)"));
        Assert.Equal("", Value(headers, only, "Demand (SP)"));
        Assert.Equal("", Value(headers, only, "Teams"));
        Assert.Equal("", Value(headers, only, "Argon planned (SP)"));
    }

    [Fact]
    public void The_total_row_comes_last_and_stays_out_of_the_autofilter()
    {
        var total = new DepartmentPlanningExportRow { Level = "Total", Title = "7 of 7 features shown, each counted once", Demand = 270, Planned = 195 };
        var bytes = DepartmentPlanningExportService.Generate(Info(),
        [
            Row("Strategic objective", "Grow payments", 185, 135),
            Row("Portfolio epic", "Instant payments", 135, 135),
        ], total);

        var (headers, rows) = WorkbookProbe.Read(bytes, "Department planning");
        var filter = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Worksheet(d, "Department planning").GetFirstChild<AutoFilter>()!.Reference!.Value);

        Assert.Equal(3, rows.Count);
        Assert.Equal("Total", rows[2][0]);
        Assert.Equal("270", Value(headers, rows[2], "Demand (SP)"));
        Assert.Equal("195", Value(headers, rows[2], "Planned (SP)"));
        Assert.EndsWith("3", filter);
    }

    [Fact]
    public void The_info_sheet_describes_the_scope()
    {
        var (_, rows) = WorkbookProbe.Read(DepartmentPlanningExportService.Generate(Info(), []), "Info");

        Assert.Contains(rows, r => r[0] == "Department" && r[1] == "Retail Banking");
        Assert.Contains(rows, r => r[0] == "Planning Increment" && r[1] == "PI 26.09");
        Assert.Contains(rows, r => r[0] == "View" && r[1] == "Objectives");
        Assert.Contains(rows, r => r[0] == "Capacity (SP)" && r[1] == "480");
        Assert.Contains(rows, r => r[0] == "Exported (UTC)" && r[1] == "2026-10-23 09:00");
    }
}
