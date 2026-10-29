using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Estimation.Core.Capacity.Services;
using Estimation.Excel;
using Xunit;
using static Estimation.Core.Capacity.Services.TeamCapacityExcelExportService;

namespace Estimation.Core.Tests.Capacity;

public class TeamCapacityExcelExportServiceTests
{
    private const int PiId = 7;
    private const int Sql = 10;

    private static readonly DateTime ExportedUtc = new(2026, 10, 13, 9, 30, 0, DateTimeKind.Utc);

    private static TeamCapacityExportInfo Info(string? artName = null, bool unsaved = false) =>
        new(artName, PiId, "PI 4", IncludeIpSprints: false, unsaved, ExportedUtc);

    private static TeamCapacityFeatureRow Feature(int id, bool isIncluded = true, decimal effort = 5) =>
        new(id, $"PAY-{id}", $"Feature {id}", null, null, effort, isIncluded, id, FeaturePiId: PiId, FeaturePiName: "PI 4");

    private static TeamFeatureDemand Demand(int featureId, int storyPoints) =>
        new(featureId, storyPoints, [new TeamFeatureStackDemand(Sql, "SQL", storyPoints, [])], []);

    private static TeamMemberMeta Member(double days) =>
        new(1, "Member 1", days, days, new(), new(), [Sql]);

    private static TeamCapacityExportBoard Board(
        string teamName, IReadOnlyList<TeamCapacityFeatureRow> features, params TeamFeatureDemand[] demands) =>
        new(teamName, features, demands.ToDictionary(d => d.FeatureId), [Member(100)]);

    private static (List<string> Headers, List<List<string>> Rows) Read(byte[] workbook, string sheetName)
    {
        using var stream = new MemoryStream(workbook);
        return ExcelSheetReader.Read(stream, sheetName);
    }

    private static string Cell(List<string> headers, List<string> row, string header) =>
        ExcelSheetReader.GetCell(row, ExcelSheetReader.BuildColumnMap(headers), header) ?? string.Empty;

    [Fact]
    public void Line_is_the_first_column()
    {
        var (headers, _) = Read(Build(Info(), [Board("Alpha", [Feature(1)])]), FeaturesSheetName);

        Assert.Equal(
            ["Line", "Team", "Position", "Feature Jira ID", "Feature Name", "Status", "Effort", "Capacity", "Required",
             "Reserved", "Below the ART line", "Feature PI", "Mapped by", "ART Rank", "Ranking", "Business Outcome Jira Id",
             "Business Outcome Name", "Portfolio Epic Jira Id", "Portfolio Epic Name", "Teams", "Labels", "Rag Status",
             "Rag Explain", "Requirement Status", "Design Approval", "Funding Status", "Pi Objective", "Target Start",
             "Target End", "Dependencies", "External Dependencies", "Feature Summary", "Acceptance Criteria"],
            headers);
    }

    [Fact]
    public void Features_above_the_line_come_first_and_each_list_counts_its_own_positions()
    {
        var belowArtLine = Feature(4) with { BelowArtLine = [new ArtLineRef(3, "Core Payments")] };
        var board = Board("Alpha", [Feature(1), Feature(2, isIncluded: false), Feature(3), belowArtLine]);

        var (headers, rows) = Read(Build(Info(), [board]), FeaturesSheetName);

        Assert.Equal(["Above", "Above", "Below", "Below"], rows.Select(r => r[0]));
        Assert.Equal(["PAY-1", "PAY-3", "PAY-2", "PAY-4"], rows.Select(r => Cell(headers, r, "Feature Jira ID")));
        Assert.Equal(["1", "2", "1", "2"], rows.Select(r => Cell(headers, r, "Position")));
        Assert.Equal(["", "", "", "Core Payments"], rows.Select(r => Cell(headers, r, "Below the ART line")));
        Assert.All(rows, r => Assert.Equal("Alpha", Cell(headers, r, "Team")));
    }

    [Fact]
    public void Capacity_shows_how_much_of_each_feature_above_the_line_is_reserved()
    {
        var board = new TeamCapacityExportBoard(
            "Alpha",
            [Feature(1), Feature(2), Feature(3), Feature(4), Feature(5, isIncluded: false)],
            new[] { Demand(1, 6), Demand(2, 8), Demand(3, 5), Demand(5, 3) }.ToDictionary(d => d.FeatureId),
            [Member(10)]);

        var (headers, rows) = Read(Build(Info(), [board]), FeaturesSheetName);

        Assert.Equal(
            [FullyReserved, PartlyReserved, NotReserved, NoStackCapacity, ""],
            rows.Select(r => Cell(headers, r, "Capacity")));
        Assert.Equal(["6", "8", "5", "", ""], rows.Select(r => Cell(headers, r, "Required")));
        Assert.Equal(["6", "4", "0", "", ""], rows.Select(r => Cell(headers, r, "Reserved")));
    }

    [Fact]
    public void Every_team_of_an_art_gets_its_own_rows_in_board_order()
    {
        var bytes = Build(Info("Core Payments"),
        [
            Board("Alpha", [Feature(1), Feature(2, isIncluded: false)]),
            Board("Bravo", [Feature(2), Feature(3)])
        ]);

        var (headers, rows) = Read(bytes, FeaturesSheetName);

        Assert.Equal(
            ["Alpha Above PAY-1", "Alpha Below PAY-2", "Bravo Above PAY-2", "Bravo Above PAY-3"],
            rows.Select(r => $"{Cell(headers, r, "Team")} {r[0]} {Cell(headers, r, "Feature Jira ID")}"));

        var (_, info) = Read(bytes, InfoSheetName);
        Assert.Contains(info, r => r[0] == "ART" && r[1] == "Core Payments");
        Assert.Contains(info, r => r[0] == "Teams" && r[1] == "Alpha, Bravo");
        Assert.Contains(info, r => r[0] == "Above the line" && r[1] == "3");
        Assert.Contains(info, r => r[0] == "Below the line" && r[1] == "1");
    }

    [Fact]
    public void Mapped_by_tells_pi_label_and_unmapped_features_apart()
    {
        var board = Board("Alpha",
        [
            Feature(1),
            Feature(2) with { FeaturePiId = 8, FeaturePiName = "PI 5", MatchedByLabel = true },
            Feature(3) with { FeaturePiId = 6, FeaturePiName = "PI 3" }
        ]);

        var (headers, rows) = Read(Build(Info(), [board]), FeaturesSheetName);

        Assert.Equal([MappedByPi, MappedByLabel, NotMappedToPi], rows.Select(r => Cell(headers, r, "Mapped by")));
        Assert.Equal(["PI 4", "PI 5", "PI 3"], rows.Select(r => Cell(headers, r, "Feature PI")));
    }

    [Fact]
    public void A_team_s_share_of_a_feature_keeps_its_decimals()
    {
        var (headers, rows) = Read(Build(Info(), [Board("Alpha", [Feature(1, effort: 10.5m)])]), FeaturesSheetName);

        Assert.Equal("10.5", Cell(headers, Assert.Single(rows), "Effort"));
    }

    [Fact]
    public void Feature_details_land_in_their_columns()
    {
        var feature = Feature(1, effort: 13) with
        {
            Status = "In Progress",
            Labels = "pay,core",
            ExternalDependencies = true,
            TargetStart = new DateTime(2026, 11, 2),
            Teams = [new TeamCapacityFeatureTeamRef(1, "Alpha", true), new TeamCapacityFeatureTeamRef(2, "Bravo", false)],
            BusinessOutcomeJiraId = "BO-1",
            BusinessOutcomeSummary = "Faster payments",
            PortfolioEpicJiraId = "PE-1",
            RagStatus = "Amber",
            ArtBoardPosition = 2,
            FeatureRanking = 40,
        };

        var (headers, rows) = Read(Build(Info(), [Board("Alpha", [feature])]), FeaturesSheetName);
        var row = Assert.Single(rows);

        Assert.Equal("Feature 1", Cell(headers, row, "Feature Name"));
        Assert.Equal("In Progress", Cell(headers, row, "Status"));
        Assert.Equal("13", Cell(headers, row, "Effort"));
        Assert.Equal("pay,core", Cell(headers, row, "Labels"));
        Assert.Equal("Yes", Cell(headers, row, "External Dependencies"));
        Assert.Equal("Alpha; Bravo", Cell(headers, row, "Teams"));
        Assert.Equal("BO-1", Cell(headers, row, "Business Outcome Jira Id"));
        Assert.Equal("Faster payments", Cell(headers, row, "Business Outcome Name"));
        Assert.Equal("PE-1", Cell(headers, row, "Portfolio Epic Jira Id"));
        Assert.Equal("Amber", Cell(headers, row, "Rag Status"));
        Assert.Equal("2", Cell(headers, row, "ART Rank"));
        Assert.Equal("40", Cell(headers, row, "Ranking"));
        Assert.Equal(new DateTime(2026, 11, 2).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture),
            Cell(headers, row, "Target Start"));
        Assert.Equal("", Cell(headers, row, "Target End"));
    }

    [Fact]
    public void Autofilter_spans_the_header_and_every_row()
    {
        var bytes = Build(Info(), [Board("Alpha", [Feature(1), Feature(2), Feature(3, isIncluded: false)])]);

        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart!;
        var sheet = workbookPart.Workbook!.GetFirstChild<Sheets>()!.Elements<Sheet>().First(s => s.Name?.Value == FeaturesSheetName);
        var worksheet = ((WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!)).Worksheet!;

        Assert.Equal("A1:AG4", worksheet.GetFirstChild<AutoFilter>()!.Reference!.Value);
    }

    [Fact]
    public void Info_notes_unsaved_changes_only_when_there_are_some()
    {
        var (_, saved) = Read(Build(Info(), [Board("Alpha", [Feature(1)])]), InfoSheetName);
        var (_, unsaved) = Read(Build(Info(unsaved: true), [Board("Alpha", [Feature(1)])]), InfoSheetName);

        Assert.DoesNotContain(saved, r => r[0] == "Note");
        Assert.Contains(unsaved, r => r[0] == "Note" && r[1] == UnsavedNote);
        Assert.Contains(saved, r => r[0] == "Team" && r[1] == "Alpha");
        Assert.DoesNotContain(saved, r => r[0] == "ART");
    }

    [Fact]
    public void Info_gives_the_story_points_per_day_of_the_teams()
    {
        var (_, single) = Read(Build(Info(), [Board("Alpha", [Feature(1)]) with { StoryPointsPerDay = 2m }]), InfoSheetName);
        var (_, same) = Read(Build(Info("Core Payments"),
            [Board("Alpha", [Feature(1)]), Board("Bravo", [Feature(2)])]), InfoSheetName);
        var (_, mixed) = Read(Build(Info("Core Payments"),
            [Board("Alpha", [Feature(1)]), Board("Bravo", [Feature(2)]) with { StoryPointsPerDay = 1.5m }]), InfoSheetName);

        Assert.Contains(single, r => r[0] == "Story points per day" && r[1] == "2");
        Assert.Contains(same, r => r[0] == "Story points per day" && r[1] == "1");
        Assert.Contains(mixed, r => r[0] == "Story points per day" && r[1] == "Alpha 1, Bravo 1.5");
    }

    [Fact]
    public void File_name_carries_the_scope_pi_and_date()
    {
        Assert.Equal("capacity-Core-Payments-PI-4-20261013.xlsx", FileName("Core Payments", "PI 4", ExportedUtc));
        Assert.Equal("capacity-export-PI-4-20261013.xlsx", FileName("  ", "PI 4", ExportedUtc));
    }
}
