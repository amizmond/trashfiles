using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Excel;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureUploadServiceTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly FeatureUploadService _service;

    public FeatureUploadServiceTests()
    {
        _service = new FeatureUploadService(_db, new StubCommentService());
    }

    private sealed class StubCommentService : IFeatureCommentService
    {
        public Task<List<FeatureCommentVm>> GetForFeatureAsync(int featureId) =>
            Task.FromResult(new List<FeatureCommentVm>());

        public Task<Dictionary<int, int>> GetCountsAsync(IReadOnlyCollection<int> featureIds) =>
            Task.FromResult(new Dictionary<int, int>());

        public Task<Dictionary<int, string>> GetUnitedAsync(IReadOnlyCollection<int> featureIds, TimeZoneInfo? timeZone = null) =>
            Task.FromResult(new Dictionary<int, string>());

        public Task<FeatureCommentVm> AddAsync(int featureId, string text, string? author = null) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(int commentId, string? requestedBy = null) => Task.FromResult(false);

        public Task<FeatureCommentVm?> SetDoneAsync(int commentId, bool isDone, string? user = null) =>
            Task.FromResult<FeatureCommentVm?>(null);
    }

    private static Stream Workbook(string[] headers, params string?[][] rows)
    {
        var sheet = new ExcelWorkbookBuilder().AddSheet("Features");
        sheet.WriteHeader(headers);

        foreach (var row in rows)
        {
            var dataRow = sheet.AddRow();
            foreach (var cell in row)
            {
                dataRow.Text(cell, skipIfEmpty: true);
            }
        }

        return new MemoryStream(sheet.Workbook.ToArray());
    }

    private static readonly string[] CoreHeaders =
        { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary" };

    private Task<FeatureParseResult> ParseAsync(Stream stream) =>
        _service.ParseFileAsync(stream, FeatureUploadColumnSelection.All());

    private Task SeedProjectAsync() =>
        _db.SeedAsync(db => db.CapitalProjects.Add(new CapitalProject { Id = 50, Name = "Atlas", JiraKey = "ATL" }));

    private Task SeedFeatureAsync(int id, string jiraId, Action<Feature>? configure = null) =>
        _db.SeedAsync(db =>
        {
            var feature = new Feature
            {
                Id = id,
                JiraId = jiraId,
                ProjectKey = "ATL",
                Summary = "Existing summary",
                Name = "Existing name"
            };
            configure?.Invoke(feature);
            db.Features.Add(feature);
        });

    [Fact]
    public async Task Known_headers_are_detected_as_columns()
    {
        await SeedProjectAsync();

        var columns = await _service.DetectColumnsAsync(Workbook(CoreHeaders));

        Assert.Contains(FeatureUploadColumn.ProjectKey, columns);
        Assert.Contains(FeatureUploadColumn.JiraId, columns);
        Assert.Contains(FeatureUploadColumn.FeatureName, columns);
        Assert.Contains(FeatureUploadColumn.Summary, columns);
        Assert.DoesNotContain(FeatureUploadColumn.Labels, columns);
    }

    [Fact]
    public async Task The_team_column_is_recognised_under_its_alias()
    {
        await SeedProjectAsync();

        var columns = await _service.DetectColumnsAsync(Workbook(new[] { "Feature Summary", "Team" }));

        Assert.Contains(FeatureUploadColumn.Team, columns);
    }

    [Fact]
    public async Task An_unrecognised_header_is_not_detected()
    {
        await SeedProjectAsync();

        var columns = await _service.DetectColumnsAsync(Workbook(new[] { "Feature Summary", "Favourite Colour" }));

        Assert.Equal(new[] { FeatureUploadColumn.Summary }, columns);
    }

    [Fact]
    public async Task A_feature_the_tool_does_not_know_is_reported_as_new()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "ATL", "ATL-1", "Login", "Build the login page" }));
        var row = Assert.Single(result.Rows);

        Assert.True(row.IsNew);
        Assert.Null(row.ExistingFeatureId);
        Assert.Empty(row.ValidationErrors);
    }

    [Fact]
    public async Task A_feature_is_matched_to_the_tool_by_jira_id()
    {
        await SeedProjectAsync();
        await SeedFeatureAsync(1, "ATL-1");

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "ATL", "atl-1", "Login", "Build the login page" }));
        var row = Assert.Single(result.Rows);

        Assert.False(row.IsNew);
        Assert.Equal(1, row.ExistingFeatureId);
    }

    [Fact]
    public async Task A_new_feature_without_a_project_key_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "", "ATL-1", "Login", "Build it" }));

        Assert.Contains(nameof(FeatureUploadRow.ProjectKey), result.Rows.Single().ValidationErrors.Keys);
    }

    [Fact]
    public async Task A_new_feature_without_a_name_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "ATL", "ATL-1", "", "Build it" }));

        Assert.Contains(nameof(FeatureUploadRow.FeatureName), result.Rows.Single().ValidationErrors.Keys);
    }

    [Fact]
    public async Task A_new_feature_without_a_summary_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "ATL", "ATL-1", "Login", "" }));

        Assert.Contains(nameof(FeatureUploadRow.Summary), result.Rows.Single().ValidationErrors.Keys);
    }

    [Fact]
    public async Task An_existing_feature_is_not_held_to_the_new_feature_requirements()
    {
        await SeedProjectAsync();
        await SeedFeatureAsync(1, "ATL-1");

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "ATL", "ATL-1", "", "" }));

        Assert.Empty(result.Rows.Single().ValidationErrors);
    }

    [Fact]
    public async Task A_project_key_the_tool_does_not_know_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "MYSTERY", "M-1", "Login", "Build it" }));

        Assert.Contains("not found", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.ProjectKey)]);
    }

    [Fact]
    public async Task A_known_project_key_is_accepted_regardless_of_case()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "atl", "ATL-1", "Login", "Build it" }));

        Assert.Empty(result.Rows.Single().ValidationErrors);
    }

    [Fact]
    public async Task A_summary_beyond_the_column_width_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders,
            new[] { "ATL", "ATL-1", "Login", new string('x', 256) }));

        Assert.Contains("Max length 255", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.Summary)]);
    }

    [Fact]
    public async Task A_summary_that_exactly_fits_is_accepted()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders,
            new[] { "ATL", "ATL-1", "Login", new string('x', 255) }));

        Assert.Empty(result.Rows.Single().ValidationErrors);
    }

    [Fact]
    public async Task A_feature_name_beyond_the_column_width_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders,
            new[] { "ATL", "ATL-1", new string('x', 256), "Build it" }));

        Assert.Contains("Max length 255", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.FeatureName)]);
    }

    [Fact]
    public async Task A_jira_id_beyond_the_column_width_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders,
            new[] { "ATL", new string('x', 101), "Login", "Build it" }));

        Assert.Contains("Max length 100", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.JiraId)]);
    }

    [Fact]
    public async Task A_labels_value_beyond_the_column_width_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Labels" },
            new[] { "ATL", "ATL-1", "Login", "Build it", new string('x', 4001) }));

        Assert.Contains("Max length 4000", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.Labels)]);
    }

    [Fact]
    public async Task A_status_beyond_the_column_width_is_rejected()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Status" },
            new[] { "ATL", "ATL-1", "Login", "Build it", new string('x', 51) }));

        Assert.Contains("Max length 50", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.Status)]);
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("10.7", 10)]
    [InlineData("-3", -3)]
    public async Task A_ranking_is_parsed_as_a_whole_number(string raw, int expected)
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Ranking" },
            new[] { "ATL", "ATL-1", "Login", "Build it", raw }));

        Assert.Equal(expected, result.Rows.Single().Ranking);
    }

    [Fact]
    public async Task An_unreadable_ranking_is_reported_against_the_raw_value()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Ranking" },
            new[] { "ATL", "ATL-1", "Login", "Build it", "high" }));

        var row = result.Rows.Single();

        Assert.Null(row.Ranking);
        Assert.Contains("'high' is not a valid number", row.ValidationErrors[nameof(FeatureUploadRow.Ranking)]);
    }

    [Fact]
    public async Task An_unreadable_story_point_value_is_reported()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Story Points" },
            new[] { "ATL", "ATL-1", "Login", "Build it", "lots" }));

        Assert.Contains("not a valid number", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.StoryPoints)]);
    }

    [Theory]
    [InlineData("2026-07-01")]
    [InlineData("07/01/2026")]
    public async Task A_recognisable_target_date_is_parsed(string raw)
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Target Start" },
            new[] { "ATL", "ATL-1", "Login", "Build it", raw }));

        Assert.Equal(new DateTime(2026, 7, 1), result.Rows.Single().TargetStart);
    }

    [Fact]
    public async Task An_excel_serial_target_date_is_parsed()
    {
        await SeedProjectAsync();
        var serial = new DateTime(2026, 7, 1).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Target Start" },
            new[] { "ATL", "ATL-1", "Login", "Build it", serial }));

        Assert.Equal(new DateTime(2026, 7, 1), result.Rows.Single().TargetStart);
    }

    [Fact]
    public async Task An_unreadable_target_date_is_reported()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Target End" },
            new[] { "ATL", "ATL-1", "Login", "Build it", "next summer" }));

        Assert.Contains("not a valid date", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.TargetEnd)]);
    }

    [Fact]
    public async Task An_unreadable_expected_date_is_reported()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Date Expected" },
            new[] { "ATL", "ATL-1", "Login", "Build it", "soon" }));

        Assert.Contains("not a valid date", result.Rows.Single().ValidationErrors[nameof(FeatureUploadRow.DateExpected)]);
    }

    [Fact]
    public async Task A_changed_summary_is_reported_with_both_sides()
    {
        await SeedProjectAsync();
        await SeedFeatureAsync(1, "ATL-1", f => f.Summary = "Old summary");

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "ATL", "ATL-1", "Login", "New summary" }));
        var row = result.Rows.Single();

        Assert.True(row.SummaryChanged);
        Assert.Equal("Old summary", row.CurrentSummary);
        Assert.Equal("New summary", row.Summary);
    }

    [Fact]
    public async Task An_unchanged_summary_is_not_reported_as_a_change()
    {
        await SeedProjectAsync();
        await SeedFeatureAsync(1, "ATL-1", f => { f.Summary = "Same summary"; f.Name = "Login"; });

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "ATL", "ATL-1", "Login", "Same summary" }));

        Assert.False(result.Rows.Single().SummaryChanged);
    }

    [Fact]
    public async Task Teams_are_split_on_the_multi_value_separator()
    {
        await SeedProjectAsync();
        await _db.SeedAsync(db =>
        {
            db.Teams.Add(new Team { Id = 20, Name = "Falcons" });
            db.Teams.Add(new Team { Id = 21, Name = "Hawks" });
        });

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "GFED Teams" },
            new[] { "ATL", "ATL-1", "Login", "Build it", "Falcons; Hawks" }));

        Assert.Equal(new[] { 20, 21 }, result.Rows.Single().TeamIds.OrderBy(i => i));
    }

    [Fact]
    public async Task A_team_the_tool_does_not_know_is_not_resolved()
    {
        await SeedProjectAsync();
        await _db.SeedAsync(db => db.Teams.Add(new Team { Id = 20, Name = "Falcons" }));

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "GFED Teams" },
            new[] { "ATL", "ATL-1", "Login", "Build it", "Falcons; Mystery" }));

        Assert.Equal(new[] { 20 }, result.Rows.Single().TeamIds);
    }

    [Fact]
    public async Task A_repeated_team_is_listed_once()
    {
        await SeedProjectAsync();
        await _db.SeedAsync(db => db.Teams.Add(new Team { Id = 20, Name = "Falcons" }));

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "GFED Teams" },
            new[] { "ATL", "ATL-1", "Login", "Build it", "Falcons; falcons" }));

        Assert.Equal(new[] { 20 }, result.Rows.Single().TeamIds);
    }

    [Fact]
    public async Task A_business_outcome_is_resolved_by_its_jira_id()
    {
        await SeedProjectAsync();
        await _db.SeedAsync(db => db.BusinessOutcomes.Add(
            new BusinessOutcome { Id = 90, JiraId = "BO-1", Summary = "Faster onboarding" }));

        var result = await ParseAsync(Workbook(
            new[] { "Project Key", "Feature Jira ID", "Feature Name", "Feature Summary", "Business Outcome Jira Id" },
            new[] { "ATL", "ATL-1", "Login", "Build it", "BO-1" }));

        Assert.Equal(90, result.Rows.Single().BusinessOutcomeId);
    }

    [Fact]
    public async Task A_row_that_names_no_feature_at_all_is_skipped()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders,
            new[] { "", "", "", "" },
            new[] { "ATL", "ATL-1", "Login", "Build it" }));

        Assert.Single(result.Rows);
    }

    [Fact]
    public async Task The_columns_actually_applied_are_reported_back()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders, new[] { "ATL", "ATL-1", "Login", "Build it" }));

        Assert.Contains(FeatureUploadColumn.Summary, result.AppliedColumns.Columns);
        Assert.DoesNotContain(FeatureUploadColumn.Labels, result.AppliedColumns.Columns);
    }

    [Fact]
    public async Task A_column_the_caller_did_not_select_is_not_applied()
    {
        await SeedProjectAsync();
        var selection = new FeatureUploadColumnSelection
        {
            Columns = new HashSet<FeatureUploadColumn>
            {
                FeatureUploadColumn.ProjectKey,
                FeatureUploadColumn.JiraId,
                FeatureUploadColumn.Summary
            }
        };

        var result = await _service.ParseFileAsync(
            Workbook(CoreHeaders, new[] { "ATL", "ATL-1", "Login", "Build it" }), selection);

        Assert.DoesNotContain(FeatureUploadColumn.FeatureName, result.AppliedColumns.Columns);
        Assert.Null(result.Rows.Single().FeatureName);
    }

    [Fact]
    public async Task Several_features_are_parsed_in_one_pass()
    {
        await SeedProjectAsync();

        var result = await ParseAsync(Workbook(CoreHeaders,
            new[] { "ATL", "ATL-1", "Login", "Build the login page" },
            new[] { "ATL", "ATL-2", "Logout", "Build the logout page" }));

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.True(r.IsNew));
    }
}
