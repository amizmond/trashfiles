using Estimation.Core.Capacity.Services;
using Estimation.Core.Train.Models;
using Estimation.Excel;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class ArtPrioritizationExcelTests
{
    private static readonly DateTime Exported = new(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);

    private static ArtBoardItem Item(int id, string? jiraId, int rank, bool included = true, string? summary = null) =>
        new(id, jiraId, summary ?? $"Summary {id}", rank, included, ArtOrderOrigin.Saved, Array.Empty<int>())
        {
            Status = "In Progress",
        };

    private static ArtOrderPosition Position(int id, string? jiraId, int rank, bool included = true) =>
        new(id, jiraId, rank, included);

    private static List<ArtOrderPosition> FourAboveTwoBelow() =>
    [
        Position(1, "EPIC-1", 1),
        Position(2, "EPIC-2", 2),
        Position(3, "EPIC-3", 3),
        Position(4, "EPIC-4", 4),
        Position(5, "EPIC-5", 5, included: false),
        Position(6, "EPIC-6", 6, included: false),
    ];

    private static Stream Sheet(string sheetName, string[] headers, params string[][] rows)
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet(sheetName);
        sheet.WriteHeader(headers);
        foreach (var row in rows)
        {
            var cells = sheet.AddRow();
            foreach (var value in row)
            {
                cells.Text(value);
            }
        }

        return new MemoryStream(sheet.Workbook.ToArray());
    }

    private static Stream Upload(params (string JiraId, string Ranking)[] rows) =>
        Sheet(
            "Portfolio Epics",
            ["Ranking", "Jira ID"],
            rows.Select(r => new[] { r.Ranking, r.JiraId }).ToArray());

    private static ArtUploadResult Parse(Stream file, IReadOnlyList<ArtOrderPosition>? current = null) =>
        ArtPrioritizationExcel.Parse(file, ArtPrioritization.PortfolioEpic, current ?? FourAboveTwoBelow());

    private static List<int> Order(IEnumerable<ArtOrderPosition> positions) => positions.Select(p => p.ItemId).ToList();

    private static List<ArtOrderPosition> ApplyUpload(params (string JiraId, string Ranking)[] rows)
    {
        var current = FourAboveTwoBelow();
        return ArtPrioritizationExcel.Apply(current, Parse(Upload(rows), current).Rows);
    }

    [Fact]
    public void An_export_reads_back_unchanged_and_applies_to_the_same_order()
    {
        var items = new List<ArtBoardItem>
        {
            Item(1, "EPIC-1", 1),
            Item(ArtBoardItems.None, null, 2),
            Item(2, "EPIC-2", 3, included: false),
        };

        var bytes = ArtPrioritizationExcel.Export("Core Payments", ArtPrioritization.PortfolioEpic, "PI 4", items, Exported);
        var current = ArtPrioritizationExcel.PositionsOf(items);
        var result = ArtPrioritizationExcel.Parse(new MemoryStream(bytes), ArtPrioritization.PortfolioEpic, current, "Core Payments", "PI 4");

        Assert.Equal(
            new[] { ArtUploadStatus.Unchanged, ArtUploadStatus.Skipped, ArtUploadStatus.Unchanged },
            result.Rows.Select(r => r.Status));
        Assert.False(result.HasChanges);
        Assert.Empty(result.Warnings);
        Assert.Equal(current, ArtPrioritizationExcel.Apply(current, result.Rows));
    }

    [Fact]
    public void The_export_lists_above_the_line_first_and_names_the_bucket()
    {
        var items = new List<ArtBoardItem>
        {
            Item(2, "EPIC-2", 3, included: false),
            Item(ArtBoardItems.None, null, 2, summary: "ignored"),
            Item(1, "EPIC-1", 1),
        };

        var bytes = ArtPrioritizationExcel.Export("Core Payments", ArtPrioritization.PortfolioEpic, "General", items, Exported);
        var (headers, rows) = ExcelSheetReader.Read(new MemoryStream(bytes), "Portfolio Epics");

        Assert.Equal(new[] { "Ranking", "Line", "Jira ID", "Summary", "Status" }, headers);
        Assert.Equal(new[] { "1", "Above", "EPIC-1", "Summary 1", "In Progress" }, rows[0]);
        Assert.Equal(new[] { "2", "Above", "", "No Portfolio Epic", "" }, rows[1].Take(5));
        Assert.Equal(new[] { "3", "Below", "EPIC-2", "Summary 2", "In Progress" }, rows[2]);
    }

    [Fact]
    public void A_business_outcome_export_carries_the_epic_but_not_for_the_bucket()
    {
        var items = new List<ArtBoardItem>
        {
            Item(10, "BO-10", 1) with { ParentJiraId = "EPIC-1", ParentSummary = "Payments" },
            Item(ArtBoardItems.None, null, 2),
        };

        var bytes = ArtPrioritizationExcel.Export("Data Platform", ArtPrioritization.BusinessOutcome, "PI 4", items, Exported);
        var (_, rows) = ExcelSheetReader.Read(new MemoryStream(bytes), "Business Outcomes");

        Assert.Equal(new[] { "1", "Above", "BO-10", "Summary 10", "In Progress", "EPIC-1", "Payments" }, rows[0]);
        Assert.Equal("No Business Outcome", rows[1][3]);
        Assert.All(rows[1].Skip(5), v => Assert.Equal("", v));
    }

    [Fact]
    public void The_info_sheet_records_art_level_and_scope()
    {
        var bytes = ArtPrioritizationExcel.Export("Data Platform", ArtPrioritization.BusinessOutcome, "General", [], Exported);
        var (_, rows) = ExcelSheetReader.Read(new MemoryStream(bytes), "Info");

        Assert.Equal(new[] { "ART", "Data Platform" }, rows[0]);
        Assert.Equal(new[] { "Level", "Business Outcome" }, rows[1]);
        Assert.Equal(new[] { "Scope", "General" }, rows[2]);
    }

    [Fact]
    public void A_file_without_jira_id_and_ranking_columns_is_rejected()
    {
        var file = Sheet("Portfolio Epics", ["Jira ID", "Summary"], ["EPIC-1", "x"]);

        var error = Assert.Throws<InvalidOperationException>(() => Parse(file));

        Assert.Contains("\"Ranking\"", error.Message);
    }

    [Fact]
    public void An_empty_file_is_rejected()
    {
        var bytes = new ExcelWorkbookBuilder().AddSheet("Portfolio Epics").Workbook.ToArray();

        Assert.Throws<InvalidOperationException>(() => Parse(new MemoryStream(bytes)));
    }

    [Fact]
    public void Headers_match_regardless_of_case()
    {
        var file = Sheet("Portfolio Epics", ["jira id", "RANKING"], ["EPIC-2", "1"]);

        var row = Assert.Single(Parse(file).Rows);

        Assert.Equal(ArtUploadStatus.Updated, row.Status);
        Assert.Equal(1, row.Ranking);
    }

    [Fact]
    public void An_old_portfolio_epic_list_export_is_accepted()
    {
        var bytes = PortfolioEpicExportService.Generate(
        [
            new PortfolioEpicExportRow { JiraId = "EPIC-3", Summary = "Three", Ranking = 1 },
            new PortfolioEpicExportRow { JiraId = "EPIC-1", Summary = "One", Ranking = 1 },
        ]);

        var result = Parse(new MemoryStream(bytes));

        Assert.Equal(new[] { ArtUploadStatus.Updated, ArtUploadStatus.Unchanged }, result.Rows.Select(r => r.Status));
        Assert.Null(result.FileArtName);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Rows_match_the_list_by_jira_id_ignoring_case_and_spaces()
    {
        var row = Assert.Single(Parse(Upload(("  epic-3 ", "1"))).Rows);

        Assert.Equal(3, row.ItemId);
        Assert.Equal(3, row.CurrentRank);
        Assert.True(row.IsIncluded);
        Assert.Equal(ArtUploadStatus.Updated, row.Status);
    }

    [Fact]
    public void A_jira_id_not_on_the_list_is_reported()
    {
        var row = Assert.Single(Parse(Upload(("EPIC-99", "1"))).Rows);

        Assert.Equal(ArtUploadStatus.NotOnList, row.Status);
        Assert.Null(row.ItemId);
    }

    [Fact]
    public void A_repeated_jira_id_is_a_duplicate_and_the_first_row_wins()
    {
        var result = Parse(Upload(("EPIC-2", "1"), ("epic-2", "4")));

        Assert.Equal(new[] { ArtUploadStatus.Updated, ArtUploadStatus.Duplicate }, result.Rows.Select(r => r.Status));
        Assert.Equal(new[] { 2, 1, 3, 4, 5, 6 }, Order(ArtPrioritizationExcel.Apply(FourAboveTwoBelow(), result.Rows)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("2.5")]
    [InlineData("first")]
    public void A_ranking_that_is_not_a_whole_number_from_one_is_invalid(string ranking)
    {
        var row = Assert.Single(Parse(Upload(("EPIC-2", ranking))).Rows);

        Assert.Equal(ArtUploadStatus.Invalid, row.Status);
        Assert.Null(row.Ranking);
        Assert.Equal(2, row.ItemId);
    }

    [Fact]
    public void A_whole_number_written_with_decimals_is_accepted()
    {
        var row = Assert.Single(Parse(Upload(("EPIC-2", "3.0"))).Rows);

        Assert.Equal(ArtUploadStatus.Updated, row.Status);
        Assert.Equal(3, row.Ranking);
    }

    [Fact]
    public void A_blank_ranking_is_skipped()
    {
        var file = Sheet("Portfolio Epics", ["Jira ID", "Ranking", "Summary"], ["EPIC-2", "", "Two"]);

        var row = Assert.Single(Parse(file).Rows);

        Assert.Equal(ArtUploadStatus.Skipped, row.Status);
        Assert.Equal(2, row.ItemId);
        Assert.Equal("Two", row.Summary);
    }

    [Fact]
    public void The_bucket_row_without_a_jira_id_is_skipped()
    {
        var file = Sheet("Portfolio Epics", ["Ranking", "Jira ID", "Summary"], ["1", "", "No Portfolio Epic"]);

        var row = Assert.Single(Parse(file).Rows);

        Assert.Equal(ArtUploadStatus.Skipped, row.Status);
        Assert.Null(row.ItemId);
        Assert.Equal("No Portfolio Epic", row.Summary);
    }

    [Fact]
    public void An_unchanged_number_is_reported_as_unchanged()
    {
        var row = Assert.Single(Parse(Upload(("EPIC-5", "5"))).Rows);

        Assert.Equal(ArtUploadStatus.Unchanged, row.Status);
        Assert.False(row.IsIncluded);
    }

    [Fact]
    public void An_export_of_another_art_and_scope_warns_without_blocking()
    {
        var bytes = ArtPrioritizationExcel.Export(
            "Mobile", ArtPrioritization.PortfolioEpic, "General", [Item(2, "EPIC-2", 1)], Exported);

        var result = ArtPrioritizationExcel.Parse(
            new MemoryStream(bytes), ArtPrioritization.PortfolioEpic, FourAboveTwoBelow(), "Core Payments", "PI 4");

        Assert.Equal("Mobile", result.FileArtName);
        Assert.Equal("General", result.FileScope);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("Mobile") && w.Contains("Core Payments"));
        Assert.Contains(result.Warnings, w => w.Contains("General") && w.Contains("PI 4"));
        Assert.Equal(ArtUploadStatus.Updated, Assert.Single(result.Rows).Status);
    }

    [Fact]
    public void An_export_of_the_other_level_warns()
    {
        var bytes = ArtPrioritizationExcel.Export(
            "Core Payments", ArtPrioritization.BusinessOutcome, "PI 4", [Item(2, "EPIC-2", 1)], Exported);

        var result = ArtPrioritizationExcel.Parse(
            new MemoryStream(bytes), ArtPrioritization.PortfolioEpic, FourAboveTwoBelow(), "Core Payments", "PI 4");

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("Business Outcome", warning);
    }

    [Fact]
    public void A_non_seekable_stream_is_read()
    {
        var bytes = ((MemoryStream)Upload(("EPIC-4", "1"))).ToArray();

        var result = Parse(new ForwardOnlyStream(bytes));

        Assert.Equal(ArtUploadStatus.Updated, Assert.Single(result.Rows).Status);
    }

    [Fact]
    public void Applying_no_numbers_keeps_the_order()
    {
        var current = FourAboveTwoBelow();

        Assert.Equal(current, ArtPrioritizationExcel.Apply(current, []));
    }

    [Fact]
    public void A_number_moves_an_item_within_its_side()
    {
        var result = ApplyUpload(("EPIC-4", "1"));

        Assert.Equal(new[] { 4, 1, 2, 3, 5, 6 }, Order(result));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, result.Select(p => p.Rank));
    }

    [Fact]
    public void A_number_past_the_line_keeps_an_above_item_last_above()
    {
        var result = ApplyUpload(("EPIC-1", "6"));

        Assert.Equal(new[] { 2, 3, 4, 1, 5, 6 }, Order(result));
        Assert.All(result.Take(4), p => Assert.True(p.IsIncluded));
    }

    [Fact]
    public void A_number_above_the_line_keeps_a_below_item_first_below()
    {
        var result = ApplyUpload(("EPIC-6", "1"));

        Assert.Equal(new[] { 1, 2, 3, 4, 6, 5 }, Order(result));
        Assert.False(result.Single(p => p.ItemId == 6).IsIncluded);
        Assert.Equal(5, result.Single(p => p.ItemId == 6).Rank);
    }

    [Fact]
    public void On_a_tie_the_uploaded_number_goes_first()
    {
        var result = ApplyUpload(("EPIC-4", "2"));

        Assert.Equal(new[] { 1, 4, 2, 3, 5, 6 }, Order(result));
    }

    [Fact]
    public void Two_uploaded_numbers_that_tie_keep_their_current_order()
    {
        var result = ApplyUpload(("EPIC-3", "1"), ("EPIC-2", "1"));

        Assert.Equal(new[] { 2, 3, 1, 4, 5, 6 }, Order(result));
    }

    [Fact]
    public void Items_missing_from_the_file_keep_their_place()
    {
        var result = ApplyUpload(("EPIC-1", "3"), ("EPIC-3", "1"));

        Assert.Equal(new[] { 3, 2, 1, 4, 5, 6 }, Order(result));
    }

    [Fact]
    public void Rows_that_cannot_apply_change_nothing()
    {
        var result = ApplyUpload(("EPIC-99", "1"), ("EPIC-3", "x"), ("EPIC-4", ""), ("", "1"));

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, Order(result));
    }

    [Fact]
    public void Apply_never_adds_items_or_changes_the_line()
    {
        var result = ApplyUpload(("EPIC-5", "1"), ("EPIC-1", "9"), ("EPIC-99", "2"));

        Assert.Equal(6, result.Count);
        Assert.Equal(
            FourAboveTwoBelow().ToDictionary(p => p.ItemId, p => p.IsIncluded),
            result.ToDictionary(p => p.ItemId, p => p.IsIncluded));
    }

    [Fact]
    public void Apply_renumbers_from_one_above_first()
    {
        var current = new List<ArtOrderPosition>
        {
            Position(7, "EPIC-7", 40, included: false),
            Position(8, "EPIC-8", 10),
            Position(ArtBoardItems.None, null, 20),
        };

        var result = ArtPrioritizationExcel.Apply(current, []);

        Assert.Equal(new[] { 8, ArtBoardItems.None, 7 }, Order(result));
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(p => p.Rank));
    }

    [Fact]
    public void Apply_to_board_items_updates_their_rank_and_order()
    {
        var items = new List<ArtBoardItem>
        {
            Item(1, "EPIC-1", 1),
            Item(2, "EPIC-2", 2),
            Item(3, "EPIC-3", 3, included: false),
        };
        var rows = ArtPrioritizationExcel.Parse(
            Upload(("EPIC-2", "1")), ArtPrioritization.PortfolioEpic, ArtPrioritizationExcel.PositionsOf(items)).Rows;

        var result = ArtPrioritizationExcel.ApplyTo(items, rows);

        Assert.Equal(new[] { 2, 1, 3 }, result.Select(i => i.Id));
        Assert.Equal(new[] { 1, 2, 3 }, result.Select(i => i.Rank));
        Assert.Equal("Summary 2", result[0].Summary);
    }

    [Fact]
    public void The_file_name_has_no_invalid_characters()
    {
        var name = ArtPrioritizationExcel.FileName("Core/Payments", "PI 4");

        Assert.Equal("ART Prioritization - Core_Payments - PI 4.xlsx", name);
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
