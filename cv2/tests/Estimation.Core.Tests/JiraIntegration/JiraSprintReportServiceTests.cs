using Estimation.Core.Calendar.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Reports;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraSprintReportServiceTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, Offset);

    private readonly InMemoryDatabase _db = new();
    private readonly FakeSprintIssues _issues = new();
    private readonly StubSchema _schema = new();
    private readonly StubCalendar _calendar = new();
    private readonly JiraSprintReportService _service;

    private readonly Sprint _sprint = new()
    {
        Id = 1134,
        TeamId = 1,
        Name = "ARG 26.09.4",
        StartDate = new DateTime(2026, 10, 26),
        EndDate = new DateTime(2026, 11, 8),
    };

    public JiraSprintReportServiceTests()
    {
        _service = new JiraSprintReportService(
            _db,
            _issues,
            _schema,
            _calendar,
            new MemoryCache(new MemoryCacheOptions()),
            new FixedTime(Now),
            Options.Create(new JiraSettings()));
    }

    private static JiraIssueResponse Issue(string key, string? feature = null, int? points = 5) =>
        new()
        {
            Key = key,
            Summary = key,
            Status = "Done",
            StatusCategory = "done",
            StoryPoints = points,
            FeatureLink = feature,
            Created = new DateTimeOffset(2026, 10, 20, 8, 0, 0, Offset),
            Sprints =
            [
                new JiraSprintValue
                {
                    Id = 37, Name = "ARG 26.09.4", State = "ACTIVE",
                    StartDate = new DateTimeOffset(2026, 10, 26, 9, 12, 0, Offset),
                    EndDate = new DateTimeOffset(2026, 11, 8, 17, 0, 0, Offset),
                },
            ],
        };

    [Fact]
    public async Task The_report_uses_the_jira_sprint_window_the_teams_capacity_and_feature_names_from_the_database()
    {
        await _db.SeedAsync(db =>
        {
            db.Teams.Add(new Team { Id = 1, Name = "Argon", StoryPointsPerDay = 2m });
            db.Features.Add(new Feature { JiraId = "FEAT-1", Name = "Login", Summary = "Login summary" });
            db.Features.Add(new Feature { JiraId = "FEAT-2", Summary = "Only a summary" });
        });
        _issues.Result = new SprintIssuesResult
        {
            Status = SprintLookupStatus.Found,
            Issues = [Issue("A-1", "FEAT-1"), Issue("A-2", "FEAT-2"), Issue("A-3")],
        };
        _schema.Schema = new JiraSchema(
            [new JiraField { Id = "customfield_10004", Name = "Sprint", SchemaCustom = JiraSchema.SprintFieldSchema }],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Done"] = "done" });

        var result = await _service.GetReportAsync("alice", _sprint);

        Assert.Equal(SprintLookupStatus.Found, result.Status);
        Assert.Equal(("ARG 26.09.4", true), Assert.Single(_issues.Calls));
        var report = result.Report!;
        Assert.True(report.Window.DatesFromJira);
        Assert.Equal(37, report.Window.JiraSprintId);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 12, 0, Offset), report.Window.Start);
        Assert.Equal(SprintReportState.Active, report.Window.State);
        Assert.Equal(Now, report.AsOf);
        Assert.Equal(3, report.CompletedIssues);
        Assert.Equal(15, report.CompletedCommittedPoints);
        Assert.Equal(["FEAT-1", "FEAT-2", "(no feature)"], report.ByFeature.Select(g => g.Name));
        Assert.Equal(["Login", "Only a summary", null], report.ByFeature.Select(g => g.Title));
        Assert.Equal(new SprintCapacity(4, 36, 72), result.Capacity);
        Assert.Equal((1, new DateTime(2026, 10, 26), new DateTime(2026, 11, 8)), Assert.Single(_calendar.Calls));
        Assert.True(result.StatusCategoriesKnown);
    }

    [Fact]
    public async Task A_report_is_cached_until_a_refresh_is_asked_for()
    {
        _issues.Result = new SprintIssuesResult { Status = SprintLookupStatus.Found, Issues = [Issue("A-1")] };

        var first = await _service.GetReportAsync("alice", _sprint);
        var second = await _service.GetReportAsync("alice", _sprint);
        Assert.Same(first, second);
        Assert.Single(_issues.Calls);

        var refreshed = await _service.GetReportAsync("alice", _sprint, refresh: true);
        Assert.NotSame(first, refreshed);
        Assert.Equal(2, _issues.Calls.Count);

        await _service.GetReportAsync("bob", _sprint);
        Assert.Equal(3, _issues.Calls.Count);
    }

    [Fact]
    public async Task A_failed_lookup_is_passed_through_and_not_cached()
    {
        _issues.Result = new SprintIssuesResult { Status = SprintLookupStatus.NotFound, ErrorMessage = "no such sprint" };

        var first = await _service.GetReportAsync("alice", _sprint);
        var second = await _service.GetReportAsync("alice", _sprint);

        Assert.Equal(SprintLookupStatus.NotFound, first.Status);
        Assert.Equal("no such sprint", first.ErrorMessage);
        Assert.Null(first.Report);
        Assert.Null(second.Report);
        Assert.Equal(2, _issues.Calls.Count);
    }

    [Fact]
    public async Task Without_a_schema_the_window_falls_back_to_the_apps_dates_and_categories_to_the_issues()
    {
        var issue = Issue("A-1");
        issue.Sprints = null;
        _issues.Result = new SprintIssuesResult { Status = SprintLookupStatus.Found, Issues = [issue] };
        _schema.Throw = true;

        var result = await _service.GetReportAsync("alice", _sprint);

        var window = result.Report!.Window;
        Assert.False(window.DatesFromJira);
        Assert.Equal(new DateOnly(2026, 10, 26), window.FirstDay);
        Assert.Equal(new DateOnly(2026, 11, 8), window.LastDay);
        Assert.Equal(1, result.Report.CompletedIssues);
        Assert.True(result.StatusCategoriesKnown);
    }

    private sealed class FakeSprintIssues : IJiraSprintIssuesService
    {
        public SprintIssuesResult Result { get; set; } = new();

        public List<(string Sprint, bool History)> Calls { get; } = [];

        public Task<SprintIssuesResult> GetSprintIssuesAsync(string userName, string sprintName, bool includeHistory = false)
        {
            Calls.Add((sprintName, includeHistory));
            return Task.FromResult(Result);
        }
    }

    private sealed class StubSchema : IJiraSchemaService
    {
        public JiraSchema Schema { get; set; } = JiraSchema.Empty;

        public bool Throw { get; set; }

        public Task<JiraSchema> GetAsync(string userName, CancellationToken cancellationToken = default) =>
            Throw ? throw new InvalidOperationException("Not authenticated to Jira.") : Task.FromResult(Schema);
    }

    private sealed class StubCalendar : ICapacityCalendarService
    {
        public List<(int TeamId, DateTime From, DateTime To)> Calls { get; } = [];

        public Task<TeamPeriodCapacity> GetTeamRangeCapacityAsync(int teamId, DateTime from, DateTime to)
        {
            Calls.Add((teamId, from, to));
            return Task.FromResult(new TeamPeriodCapacity(4, 10, 40, 2, 2, 0, 36, 90));
        }

        public Task<TeamPeriodCapacity> GetTeamMonthCapacityAsync(int teamId, int year, int month) => throw new NotSupportedException();
        public Task<List<MemberLeaveUsage>> GetTeamLeaveUsageAsync(int teamId, int year) => throw new NotSupportedException();
        public Task<PersonLeaveSummary> GetPersonLeaveAsync(int humanResourceId, int year, DateTime today) => throw new NotSupportedException();
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTime(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
