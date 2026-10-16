namespace Estimation.Excel.Tests;

public class TeamExcelExportServiceTests
{
    private static TeamExportRow Team(int id = 1, string project = "Atlas", string name = "Falcons", string stacks = ".NET, SQL") =>
        new()
        {
            Id = id,
            Project = project,
            TeamName = name,
            TechnologyStacks = stacks
        };

    private static byte[] Generate(List<TeamExportRow>? teams = null, List<string>? projects = null) =>
        TeamExcelExportService.GenerateTeamExport(
            teams ?? new List<TeamExportRow>(),
            projects ?? new List<string>());

    [Fact]
    public void The_export_lives_on_a_teams_sheet()
    {
        Assert.Equal(new[] { "Teams" }, WorkbookProbe.SheetNames(Generate()));
    }

    [Fact]
    public void The_header_names_the_five_team_columns()
    {
        var (headers, _) = WorkbookProbe.Read(Generate());

        Assert.Equal(new[] { "DB id", "Project", "Team Name", "Technology Stacks", "Story Points per Day" }, headers);
    }

    [Fact]
    public void Column_widths_are_set_for_all_five_columns()
    {
        Assert.Equal(
            new[] { "1-1:10", "2-2:40", "3-3:30", "4-4:40", "5-5:22" },
            WorkbookProbe.ColumnWidths(Generate(), "Teams"));
    }

    [Fact]
    public void Every_team_is_exported_in_order()
    {
        var bytes = Generate(new List<TeamExportRow>
        {
            Team(7, "Atlas", "Falcons", ".NET"),
            Team(8, "Borealis", "Hawks", "Java, Kafka")
        });

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal(new[] { "7", "Atlas", "Falcons", ".NET", "1" }, rows[0]);
        Assert.Equal(new[] { "8", "Borealis", "Hawks", "Java, Kafka", "1" }, rows[1]);
    }

    [Fact]
    public void Story_points_per_day_is_written_as_a_real_number()
    {
        var team = Team();
        team.StoryPointsPerDay = 1.5m;
        var bytes = Generate(new List<TeamExportRow> { team });

        var dataType = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Teams", "E2")!.DataType!.Value);

        Assert.Equal(CellValues.Number, dataType);
        Assert.Equal("1.5", WorkbookProbe.ValueOf(bytes, "Teams", "E2"));
    }

    [Fact]
    public void The_database_id_is_written_as_a_real_number_so_it_can_be_matched_on_re_import()
    {
        var bytes = Generate(new List<TeamExportRow> { Team(id: 42) });

        var dataType = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Teams", "A2")!.DataType!.Value);

        Assert.Equal(CellValues.Number, dataType);
        Assert.Equal("42", WorkbookProbe.ValueOf(bytes, "Teams", "A2"));
    }

    [Fact]
    public void A_team_with_no_technology_stacks_still_writes_the_column()
    {
        var bytes = Generate(new List<TeamExportRow> { Team(stacks: "") });

        Assert.Equal("", WorkbookProbe.ValueOf(bytes, "Teams", "D2"));
    }

    [Fact]
    public void The_project_column_carries_a_dropdown_of_existing_projects()
    {
        var bytes = Generate(projects: new List<string> { "Atlas", "Borealis" });

        var validation = Assert.Single(WorkbookProbe.Validations(bytes, "Teams"));

        Assert.Equal(DataValidationValues.List, validation.Type);
        Assert.Equal("B2:B1000", validation.Range);
        Assert.Equal("\"Atlas,Borealis\"", validation.Formula);
        Assert.Equal("Invalid Project", validation.ErrorTitle);
        Assert.True(validation.ShowErrorMessage);
    }

    [Fact]
    public void No_projects_means_no_dropdown()
    {
        Assert.Empty(WorkbookProbe.Validations(Generate(projects: new List<string>()), "Teams"));
    }

    [Fact]
    public void The_dropdown_covers_a_thousand_rows_for_a_small_team_list()
    {
        var bytes = Generate(
            new List<TeamExportRow> { Team(), Team(2) },
            new List<string> { "Atlas" });

        Assert.Equal("B2:B1000", Assert.Single(WorkbookProbe.Validations(bytes, "Teams")).Range);
    }

    [Fact]
    public void The_dropdown_stretches_past_a_thousand_rows_for_a_large_team_list()
    {
        var teams = Enumerable.Range(1, 1500).Select(i => Team(i, name: $"Team {i}")).ToList();

        var bytes = Generate(teams, new List<string> { "Atlas" });

        Assert.Equal("B2:B1502", Assert.Single(WorkbookProbe.Validations(bytes, "Teams")).Range);
    }

    [Fact]
    public void A_project_name_containing_a_quote_is_escaped_into_the_dropdown()
    {
        var bytes = Generate(projects: new List<string> { "The \"Big\" One" });

        Assert.Equal("\"The \"\"Big\"\" One\"", Assert.Single(WorkbookProbe.Validations(bytes, "Teams")).Formula);
        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }

    [Fact]
    public void An_empty_export_still_produces_a_usable_template()
    {
        var bytes = Generate();

        var (headers, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal(5, headers.Count);
        Assert.Empty(rows);
        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }

    [Fact]
    public void The_export_is_schema_valid()
    {
        var bytes = Generate(
            new List<TeamExportRow> { Team(), Team(2, "Borealis", "Hawks", "") },
            new List<string> { "Atlas", "Borealis" });

        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }
}
