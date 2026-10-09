namespace Estimation.Excel.Tests;

public class StrategicObjectiveExportServiceTests
{
    private static byte[] Generate(params StrategicObjectiveExportRow[] rows) =>
        StrategicObjectiveExportService.Generate(rows.ToList());

    [Fact]
    public void The_export_lives_on_a_strategic_objectives_sheet()
    {
        Assert.Equal(new[] { "Strategic Objectives" }, WorkbookProbe.SheetNames(Generate()));
    }

    [Fact]
    public void The_header_names_the_five_objective_columns()
    {
        var (headers, _) = WorkbookProbe.Read(Generate());

        Assert.Equal(new[] { "Jira ID", "Summary", "Status", "Epics", "Labels" }, headers);
    }

    [Fact]
    public void Column_widths_are_set_for_all_five_columns()
    {
        Assert.Equal(
            new[] { "1-1:18", "2-2:50", "3-3:15", "4-4:60", "5-5:30" },
            WorkbookProbe.ColumnWidths(Generate(), "Strategic Objectives"));
    }

    [Fact]
    public void Every_objective_is_exported_in_order()
    {
        var bytes = Generate(
            new StrategicObjectiveExportRow
            {
                JiraId = "SO-1",
                Summary = "Grow the platform",
                Status = "In Progress",
                Epics = "EP-1, EP-2",
                Labels = "strategic"
            },
            new StrategicObjectiveExportRow
            {
                JiraId = "SO-2",
                Summary = "Reduce cost",
                Status = "Done",
                Epics = "EP-3",
                Labels = "cost"
            });

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal(new[] { "SO-1", "Grow the platform", "In Progress", "EP-1, EP-2", "strategic" }, rows[0]);
        Assert.Equal(new[] { "SO-2", "Reduce cost", "Done", "EP-3", "cost" }, rows[1]);
    }

    [Fact]
    public void An_objective_with_no_values_writes_an_empty_row_that_the_reader_drops()
    {
        var bytes = Generate(new StrategicObjectiveExportRow());

        var (headers, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal(5, headers.Count);
        Assert.Empty(rows);
        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }

    [Fact]
    public void The_export_is_schema_valid()
    {
        var bytes = Generate(
            new StrategicObjectiveExportRow { JiraId = "SO-1", Summary = "Grow" },
            new StrategicObjectiveExportRow { JiraId = "SO-2" });

        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }
}

public class PortfolioEpicExportServiceTests
{
    private static byte[] Generate(params PortfolioEpicExportRow[] rows) =>
        PortfolioEpicExportService.Generate(rows.ToList());

    [Fact]
    public void The_export_lives_on_a_portfolio_epics_sheet()
    {
        Assert.Equal(new[] { "Portfolio Epics" }, WorkbookProbe.SheetNames(Generate()));
    }

    [Fact]
    public void The_header_names_the_seven_epic_columns()
    {
        var (headers, _) = WorkbookProbe.Read(Generate());

        Assert.Equal(
            new[]
            {
                "Jira ID",
                "Summary",
                "Status",
                "Ranking",
                "Strategic Objective Jira Id",
                "Strategic Objective Name",
                "Labels"
            },
            headers);
    }

    [Fact]
    public void Column_widths_are_set_for_all_seven_columns()
    {
        Assert.Equal(
            new[] { "1-1:18", "2-2:50", "3-3:15", "4-4:24", "5-5:30", "6-6:50", "7-7:30" },
            WorkbookProbe.ColumnWidths(Generate(), "Portfolio Epics"));
    }

    [Fact]
    public void The_ranking_is_written_as_a_real_number_so_the_column_sorts_numerically()
    {
        var bytes = Generate(new PortfolioEpicExportRow { JiraId = "EP-1", Ranking = 10 });

        var dataType = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Portfolio Epics", "D2")!.DataType!.Value);

        Assert.Equal(CellValues.Number, dataType);
        Assert.Equal("10", WorkbookProbe.ValueOf(bytes, "Portfolio Epics", "D2"));
    }

    [Fact]
    public void A_ranking_from_several_arts_is_written_as_text()
    {
        var bytes = Generate(new PortfolioEpicExportRow
        {
            JiraId = "EP-1",
            RankingText = "Core Payments: 2; Mobile: 5 (below line)",
            Labels = "epic"
        });

        var dataType = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Portfolio Epics", "D2")!.DataType!.Value);
        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.NotEqual(CellValues.Number, dataType);
        Assert.Equal("Core Payments: 2; Mobile: 5 (below line)", rows.Single()[3]);
        Assert.Equal("epic", rows.Single()[6]);
    }

    [Fact]
    public void A_numeric_ranking_wins_over_the_text()
    {
        var bytes = Generate(new PortfolioEpicExportRow { JiraId = "EP-1", Ranking = 3, RankingText = "Atlas: 3" });

        var dataType = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Portfolio Epics", "D2")!.DataType!.Value);

        Assert.Equal(CellValues.Number, dataType);
        Assert.Equal("3", WorkbookProbe.ValueOf(bytes, "Portfolio Epics", "D2"));
    }

    [Fact]
    public void An_unranked_epic_leaves_a_gap_that_keeps_later_columns_aligned()
    {
        var bytes = Generate(new PortfolioEpicExportRow
        {
            JiraId = "EP-1",
            Summary = "Platform",
            Status = "Open",
            Ranking = null,
            StrategicObjectiveJiraIds = "SO-1",
            StrategicObjectiveNames = "Grow",
            Labels = "epic"
        });

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal(new[] { "A2", "B2", "C2", "E2", "F2", "G2" }, WorkbookProbe.CellReferences(bytes, "Portfolio Epics", 2));
        Assert.Equal(new[] { "EP-1", "Platform", "Open", "", "SO-1", "Grow", "epic" }, rows.Single());
    }

    [Fact]
    public void The_header_is_coloured_and_frozen_like_the_feature_export()
    {
        var bytes = Generate(new PortfolioEpicExportRow { JiraId = "EP-1" });

        Assert.Equal(ExcelPrimitives.HeaderFillStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "Portfolio Epics", "A1"));
        Assert.Equal(ExcelPrimitives.HeaderFillStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "Portfolio Epics", "G1"));
        Assert.True(WorkbookProbe.HasFrozenTopRow(bytes, "Portfolio Epics"));
    }

    [Fact]
    public void Every_column_gets_a_filter_dropdown_over_the_exported_rows()
    {
        var bytes = Generate(
            new PortfolioEpicExportRow { JiraId = "EP-1" },
            new PortfolioEpicExportRow { JiraId = "EP-2" },
            new PortfolioEpicExportRow { JiraId = "EP-3" });

        Assert.Equal("A1:G4", WorkbookProbe.AutoFilterReference(bytes, "Portfolio Epics"));
    }

    [Fact]
    public void The_export_is_schema_valid()
    {
        var bytes = Generate(
            new PortfolioEpicExportRow { JiraId = "EP-1", Ranking = 1 },
            new PortfolioEpicExportRow { JiraId = "EP-2", Ranking = null });

        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }
}

public class BusinessOutcomeExportServiceTests
{
    private static byte[] Generate(params BusinessOutcomeExportRow[] rows) =>
        BusinessOutcomeExportService.Generate(rows.ToList());

    [Fact]
    public void The_export_lives_on_a_business_outcomes_sheet()
    {
        Assert.Equal(new[] { "Business Outcomes" }, WorkbookProbe.SheetNames(Generate()));
    }

    [Fact]
    public void The_header_names_the_eleven_outcome_columns()
    {
        var (headers, _) = WorkbookProbe.Read(Generate());

        Assert.Equal(
            new[]
            {
                "Jira ID",
                "Summary",
                "Status",
                "Ranking",
                "Rag Status",
                "Rag Explain",
                "ARTs involved",
                "Epic Id",
                "Epic",
                "Strategic Objective Jira Id",
                "Labels"
            },
            headers);
    }

    [Fact]
    public void Column_widths_are_set_for_all_eleven_columns()
    {
        Assert.Equal(
            new[] { "1-1:18", "2-2:50", "3-3:15", "4-4:24", "5-5:14", "6-6:35", "7-7:35", "8-8:18", "9-9:40", "10-10:30", "11-11:30" },
            WorkbookProbe.ColumnWidths(Generate(), "Business Outcomes"));
    }

    [Fact]
    public void Every_outcome_is_exported_in_column_order()
    {
        var bytes = Generate(new BusinessOutcomeExportRow
        {
            JiraId = "BO-1",
            Summary = "Faster onboarding",
            Status = "In Progress",
            Ranking = 4,
            RagStatus = "Amber",
            RagExplain = "Vendor contract delayed",
            CapitalProjects = "Atlas, Borealis",
            PortfolioEpicJiraId = "EP-1",
            PortfolioEpicName = "Platform",
            StrategicObjectiveJiraIds = "SO-1",
            Labels = "outcome"
        });

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal(
            new[] { "BO-1", "Faster onboarding", "In Progress", "4", "Amber", "Vendor contract delayed", "Atlas, Borealis", "EP-1", "Platform", "SO-1", "outcome" },
            rows.Single());
    }

    [Fact]
    public void The_ranking_is_written_as_a_real_number_so_the_column_sorts_numerically()
    {
        var bytes = Generate(new BusinessOutcomeExportRow { JiraId = "BO-1", Ranking = 10 });

        var dataType = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Business Outcomes", "D2")!.DataType!.Value);

        Assert.Equal(CellValues.Number, dataType);
        Assert.Equal("10", WorkbookProbe.ValueOf(bytes, "Business Outcomes", "D2"));
    }

    [Fact]
    public void A_ranking_from_several_arts_is_written_as_text()
    {
        var bytes = Generate(new BusinessOutcomeExportRow
        {
            JiraId = "BO-1",
            RankingText = "Atlas: 1; Borealis: 4",
            CapitalProjects = "Atlas, Borealis"
        });

        var dataType = WorkbookProbe.Inspect(bytes, d => WorkbookProbe.Cell(d, "Business Outcomes", "D2")!.DataType!.Value);
        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.NotEqual(CellValues.Number, dataType);
        Assert.Equal("Atlas: 1; Borealis: 4", rows.Single()[3]);
        Assert.Equal("Atlas, Borealis", rows.Single()[6]);
    }

    [Fact]
    public void An_unranked_outcome_leaves_a_gap_that_keeps_later_columns_aligned()
    {
        var bytes = Generate(new BusinessOutcomeExportRow
        {
            JiraId = "BO-1",
            Summary = "Faster onboarding",
            Status = "Open",
            Ranking = null,
            CapitalProjects = "Atlas",
            Labels = "outcome"
        });

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal("", rows.Single()[3]);
        Assert.Equal("Atlas", rows.Single()[6]);
        Assert.Equal("outcome", rows.Single()[10]);
    }

    [Fact]
    public void An_outcome_without_a_rag_status_leaves_both_rag_cells_empty()
    {
        var bytes = Generate(new BusinessOutcomeExportRow
        {
            JiraId = "BO-3",
            Ranking = 2,
            RagStatus = null,
            RagExplain = null,
            CapitalProjects = "Atlas"
        });

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal("", rows.Single()[4]);
        Assert.Equal("", rows.Single()[5]);
        Assert.Equal("Atlas", rows.Single()[6]);
    }

    [Fact]
    public void An_outcome_not_linked_to_an_epic_still_exports_its_own_fields()
    {
        var bytes = Generate(new BusinessOutcomeExportRow
        {
            JiraId = "BO-2",
            Summary = "Standalone",
            PortfolioEpicJiraId = null,
            PortfolioEpicName = null
        });

        var (_, rows) = WorkbookProbe.Read(bytes);

        Assert.Equal("BO-2", rows.Single()[0]);
        Assert.Equal("", rows.Single()[7]);
        Assert.Equal("", rows.Single()[8]);
    }

    [Fact]
    public void The_header_is_coloured_and_frozen_like_the_feature_export()
    {
        var bytes = Generate(new BusinessOutcomeExportRow { JiraId = "BO-1" });

        Assert.Equal(ExcelPrimitives.HeaderFillStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "Business Outcomes", "A1"));
        Assert.Equal(ExcelPrimitives.HeaderFillStyleIndex, WorkbookProbe.StyleIndexOf(bytes, "Business Outcomes", "K1"));
        Assert.True(WorkbookProbe.HasFrozenTopRow(bytes, "Business Outcomes"));
    }

    [Fact]
    public void Every_column_gets_a_filter_dropdown_over_the_exported_rows()
    {
        var bytes = Generate(
            new BusinessOutcomeExportRow { JiraId = "BO-1" },
            new BusinessOutcomeExportRow { JiraId = "BO-2" });

        Assert.Equal("A1:K3", WorkbookProbe.AutoFilterReference(bytes, "Business Outcomes"));
    }

    [Fact]
    public void The_export_is_schema_valid()
    {
        var bytes = Generate(
            new BusinessOutcomeExportRow { JiraId = "BO-1", Summary = "Faster onboarding" },
            new BusinessOutcomeExportRow());

        Assert.Empty(WorkbookProbe.SchemaErrors(bytes));
    }
}
