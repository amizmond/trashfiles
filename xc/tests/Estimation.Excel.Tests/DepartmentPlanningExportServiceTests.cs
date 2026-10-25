namespace Estimation.Excel.Tests;

public class DepartmentPlanningExportServiceTests
{
    private static readonly DateTime Exported = new(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);

    private static DepartmentPlanningExportInfo Info() =>
        new("Retail Banking", "PI 26.09", "Objectives", "none", 480, Exported);

    private static DepartmentPlanningExportRow Row(string level, string title, double demand, double planned) =>
        new(level, "STRAT-1 Grow payments", level == "Strategic objective" ? null : "EPIC-10 Instant payments", null, null,
            "EPIC-10", title, "In Progress", "Amber", "CP 1", "Above", "Core Payments", 2, 4, 1, 25, demand, planned, demand - planned,
            null, 0.28 * 100, 3, 1, 2, new DateTime(2026, 11, 30));

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

        Assert.Equal(DepartmentPlanningExportService.Headers, headers);
        Assert.Equal("Strategic Objective", headers[1]);
        Assert.Equal("Business Epic", headers[4]);
        Assert.Equal("Planned (SP)", headers[17]);
    }

    [Fact]
    public void Rows_keep_their_hierarchy_columns_and_numbers()
    {
        var bytes = DepartmentPlanningExportService.Generate(Info(),
        [
            Row("Strategic objective", "Grow payments", 185, 135),
            Row("Portfolio epic", "Instant payments", 135, 135),
        ]);

        var (_, rows) = WorkbookProbe.Read(bytes, "Department planning");

        Assert.Equal(2, rows.Count);
        Assert.Equal("Strategic objective", rows[0][0]);
        Assert.Equal("STRAT-1 Grow payments", rows[0][1]);
        Assert.Equal("", rows[0][2]);
        Assert.Equal("EPIC-10 Instant payments", rows[1][2]);
        Assert.Equal("185", rows[0][16]);
        Assert.Equal("135", rows[0][17]);
        Assert.Equal("50", rows[0][18]);
        Assert.Equal("28", rows[0][20]);
        Assert.Equal("3", rows[0][21]);
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
