namespace Estimation.Excel.Tests;

public class ArtPrioritizationExportServiceTests
{
    private static readonly DateTime Exported = new(2026, 10, 9, 14, 30, 0, DateTimeKind.Utc);

    private static ArtPrioritizationExportInfo Info(bool businessOutcomes = false, string scope = "PI 4") =>
        new("Core Payments", businessOutcomes ? "Business Outcome" : "Portfolio Epic", scope, Exported, businessOutcomes);

    private static byte[] Generate(bool businessOutcomes = false, params ArtPrioritizationExportRow[] rows) =>
        ArtPrioritizationExportService.Generate(Info(businessOutcomes), rows);

    [Fact]
    public void A_portfolio_epic_export_has_the_epics_sheet_and_an_info_sheet()
    {
        Assert.Equal(new[] { "Portfolio Epics", "Info" }, WorkbookProbe.SheetNames(Generate()));
    }

    [Fact]
    public void A_business_outcome_export_has_the_outcomes_sheet_and_an_info_sheet()
    {
        Assert.Equal(new[] { "Business Outcomes", "Info" }, WorkbookProbe.SheetNames(Generate(businessOutcomes: true)));
    }

    [Fact]
    public void Portfolio_epic_columns_start_with_ranking_and_line()
    {
        var (headers, _) = WorkbookProbe.Read(Generate(), "Portfolio Epics");

        Assert.Equal(new[] { "Ranking", "Line", "Jira ID", "Summary", "Status" }, headers);
    }

    [Fact]
    public void Business_outcome_columns_add_the_epic()
    {
        var (headers, _) = WorkbookProbe.Read(Generate(businessOutcomes: true), "Business Outcomes");

        Assert.Equal(new[] { "Ranking", "Line", "Jira ID", "Summary", "Status", "Epic Jira ID", "Epic" }, headers);
    }

    [Fact]
    public void Rows_are_written_with_their_line()
    {
        var bytes = Generate(
            false,
            new ArtPrioritizationExportRow(1, true, "EPIC-1", "Payments", "In Progress"),
            new ArtPrioritizationExportRow(2, false, "EPIC-2", "Cards", "Funnel"));

        var (_, rows) = WorkbookProbe.Read(bytes, "Portfolio Epics");

        Assert.Equal(new[] { "1", "Above", "EPIC-1", "Payments", "In Progress" }, rows[0]);
        Assert.Equal(new[] { "2", "Below", "EPIC-2", "Cards", "Funnel" }, rows[1]);
    }

    [Fact]
    public void Business_outcome_rows_carry_the_epic()
    {
        var bytes = Generate(
            true,
            new ArtPrioritizationExportRow(1, true, "BO-1", "Faster checkout", "Done", "EPIC-1", "Payments"));

        var (_, rows) = WorkbookProbe.Read(bytes, "Business Outcomes");

        Assert.Equal(new[] { "1", "Above", "BO-1", "Faster checkout", "Done", "EPIC-1", "Payments" }, Assert.Single(rows));
    }

    [Fact]
    public void The_bucket_row_has_an_empty_jira_id()
    {
        var bytes = Generate(false, new ArtPrioritizationExportRow(3, true, null, "No Portfolio Epic", null));

        var (_, rows) = WorkbookProbe.Read(bytes, "Portfolio Epics");

        var row = Assert.Single(rows);
        Assert.Equal("", row[2]);
        Assert.Equal("No Portfolio Epic", row[3]);
    }

    [Fact]
    public void The_ranking_is_a_number_cell()
    {
        var bytes = Generate(false, new ArtPrioritizationExportRow(7, true, "EPIC-7", "S", null));

        var cell = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Portfolio Epics", "A2"));

        Assert.NotNull(cell);
        Assert.Equal(CellValues.Number, cell!.DataType?.Value ?? CellValues.Number);
        Assert.Equal("7", cell.CellValue?.Text);
    }

    [Fact]
    public void The_ranking_column_only_takes_whole_numbers_from_one()
    {
        var bytes = Generate(
            false,
            new ArtPrioritizationExportRow(1, true, "EPIC-1", "A", null),
            new ArtPrioritizationExportRow(2, true, "EPIC-2", "B", null));

        var validation = Assert.Single(WorkbookProbe.Validations(bytes, "Portfolio Epics"));

        Assert.Equal(DataValidationValues.Whole, validation.Type);
        Assert.Equal(DataValidationOperatorValues.GreaterThanOrEqual, validation.Operator);
        Assert.Equal("A2:A3", validation.Range);
        Assert.Equal("1", validation.Formula);
        Assert.True(validation.ShowErrorMessage);
    }

    [Fact]
    public void The_header_is_coloured_frozen_and_filtered()
    {
        var bytes = Generate(
            true,
            new ArtPrioritizationExportRow(1, true, "BO-1", "A", null),
            new ArtPrioritizationExportRow(2, false, "BO-2", "B", null));

        Assert.Equal(ExcelPrimitives.HeaderFillStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "Business Outcomes", "A1"));
        Assert.True(WorkbookProbe.HasFrozenTopRow(bytes, "Business Outcomes"));
        Assert.Equal("A1:G3", WorkbookProbe.AutoFilterReference(bytes, "Business Outcomes"));
    }

    [Fact]
    public void The_info_sheet_names_the_art_level_scope_and_export_time()
    {
        var (headers, rows) = WorkbookProbe.Read(Generate(), "Info");

        Assert.Equal(new[] { "Field", "Value" }, headers);
        Assert.Equal(
            new[]
            {
                new[] { "ART", "Core Payments" },
                new[] { "Level", "Portfolio Epic" },
                new[] { "Scope", "PI 4" },
                new[] { "Exported (UTC)", "2026-10-09 14:30" },
            },
            rows.Select(r => r.ToArray()).ToArray());
    }

    [Fact]
    public void An_empty_export_is_still_valid()
    {
        Assert.Empty(WorkbookProbe.SchemaErrors(Generate()));
    }

    [Fact]
    public void A_full_export_passes_schema_validation()
    {
        var bytes = Generate(
            true,
            new ArtPrioritizationExportRow(1, true, "BO-1", "A", "Done", "EPIC-1", "E"),
            new ArtPrioritizationExportRow(2, false, null, "No Business Outcome", null));

        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }
}
