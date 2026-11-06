using System.Globalization;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Reports;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class SprintReportCalculatorTests
{
    private const string SprintName = "ARG 26.09.4";

    private static readonly TimeSpan Offset = TimeSpan.FromHours(1);

    private static readonly Dictionary<string, string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Open"] = "new",
        ["In Progress"] = "indeterminate",
        ["Done"] = "done",
        ["Closed"] = "done",
    };

    private static DateTimeOffset T(string text) =>
        new(DateTime.Parse(text, CultureInfo.InvariantCulture), Offset);

    private static SprintReportWindow Window(
        string start = "2026-10-26 09:00",
        string end = "2026-11-08 17:00",
        SprintReportState state = SprintReportState.Active,
        string? completedAt = null,
        int? jiraId = 37) =>
        new(T(start), T(end), completedAt is null ? null : T(completedAt), state, true, jiraId, null);

    private static JiraIssueResponse Issue(
        string key,
        string status = "Open",
        int? points = null,
        string created = "2026-10-20 08:00",
        string? assignee = null,
        string? type = "Story",
        string? feature = null,
        List<JiraSprintValue>? sprints = null,
        params JiraChangelogEntry[] changes) =>
        new()
        {
            Key = key,
            Summary = key,
            Status = status,
            StatusCategory = Categories.GetValueOrDefault(status),
            StoryPoints = points,
            Created = T(created),
            AssigneeDisplayName = assignee,
            IssueType = type,
            FeatureLink = feature,
            Sprints = sprints,
            Changelog = changes.OrderBy(c => c.Created).ToList(),
        };

    private static JiraChangelogEntry Change(string at, params JiraChangelogItem[] items) =>
        new() { Id = at, Created = T(at), Items = items.ToList() };

    private static JiraChangelogItem Status(string from, string to) =>
        new() { Field = "status", FieldType = "jira", FromText = from, ToText = to };

    private static JiraChangelogItem SprintMove(string? fromIds, string? fromNames, string? toIds, string? toNames) =>
        new() { Field = "Sprint", FieldType = "custom", From = fromIds, FromText = fromNames, To = toIds, ToText = toNames };

    private static JiraChangelogItem Points(string? from, string? to) =>
        new() { Field = "Story Points", FieldType = "custom", FromText = from, ToText = to };

    private static SprintReport Build(SprintReportWindow window, string now, params JiraIssueResponse[] issues) =>
        SprintReportCalculator.Build(new SprintReportInput
        {
            SprintName = SprintName,
            Window = window,
            Now = T(now),
            Issues = issues,
            StatusCategories = Categories,
        });

    [Fact]
    public void Issues_in_the_sprint_at_start_are_committed_and_later_ones_are_added_scope()
    {
        var committedDone = Issue("A-1", "Done", 5, changes: Change("2026-10-28 10:00", Status("Open", "Done")));
        var createdDuringSprint = Issue("A-2", "Open", 3, created: "2026-10-27 10:00");
        var pulledIn = Issue("A-3", "In Progress", 2,
            changes: Change("2026-10-29 11:00", SprintMove("", "", "37", SprintName)));

        var report = Build(Window(), "2026-11-02 12:00", committedDone, createdDuringSprint, pulledIn);

        Assert.Equal(1, report.CommittedIssues);
        Assert.Equal(5, report.CommittedPoints);
        Assert.Equal(2, report.AddedIssues);
        Assert.Equal(5, report.AddedPoints);
        Assert.Equal(3, report.ScopeIssues);
        Assert.Equal(10, report.ScopePoints);
        Assert.Equal(1, report.CompletedIssues);
        Assert.Equal(5, report.CompletedPoints);
        Assert.Equal(1, report.CompletedCommittedIssues);
        Assert.Equal(5, report.CompletedCommittedPoints);
        Assert.Equal(2, report.NotCompletedIssues);
        Assert.Equal(5, report.NotCompletedPoints);
        Assert.Equal(1.0, report.CommitmentCompletion);
        Assert.Equal(0.5, report.ScopeCompletion);
        Assert.Equal(1.0, report.ScopeChange);
        Assert.Equal(0, report.RemovedIssues);

        var byKey = report.Issues.ToDictionary(i => i.Key);
        Assert.Equal(T("2026-10-27 10:00"), byKey["A-2"].AddedAt);
        Assert.Equal(T("2026-10-29 11:00"), byKey["A-3"].AddedAt);
        Assert.Null(byKey["A-1"].AddedAt);
        Assert.Equal(T("2026-10-28 10:00"), byKey["A-1"].DoneAt);
    }

    [Fact]
    public void Status_history_is_replayed_to_the_moment_the_report_looks_at()
    {
        var reopened = Issue("B-1", "Open", 3,
            changes:
            [
                Change("2026-10-28 10:00", Status("Open", "Done")),
                Change("2026-10-30 10:00", Status("Done", "Open")),
            ]);
        var done = Issue("B-2", "Done", 2, changes: Change("2026-10-28 11:00", Status("Open", "Done")));

        var active = Build(Window(), "2026-11-02 12:00", reopened, done);
        Assert.Equal(["B-2"], active.Issues.Where(i => i.IsCompleted).Select(i => i.Key));
        Assert.Equal(["B-1"], active.Issues.Where(i => i.IsNotCompleted).Select(i => i.Key));

        var closedEarly = Build(Window(state: SprintReportState.Closed, completedAt: "2026-10-29 18:00"), "2026-11-02 12:00", reopened, done);
        Assert.Equal(["B-1", "B-2"], closedEarly.Issues.Where(i => i.IsCompleted).Select(i => i.Key).Order());
        Assert.Equal(T("2026-10-29 18:00"), closedEarly.AsOf);
        Assert.Equal(closedEarly.WorkingDays, closedEarly.WorkingDaysElapsed);
    }

    [Fact]
    public void An_issue_was_not_done_at_start_even_when_it_is_done_now()
    {
        var issue = Issue("C-1", "Done", 5, changes: Change("2026-10-28 10:00", Status("Open", "Done")));

        var report = Build(Window(), "2026-11-02 12:00", issue);

        Assert.Equal(5, report.Burndown[0].RemainingPoints);
        Assert.Equal(5, report.CompletedCommittedPoints);
    }

    [Fact]
    public void Story_points_count_at_sprint_start_for_the_commitment_and_the_latest_value_for_the_scope()
    {
        var reEstimated = Issue("D-1", "Done", 8,
            changes:
            [
                Change("2026-10-30 09:00", Points("3.0", "8.0")),
                Change("2026-11-01 09:00", Status("Open", "Done")),
            ]);
        var unestimated = Issue("D-2", "Open", null);

        var report = Build(Window(), "2026-11-02 12:00", reEstimated, unestimated);

        Assert.Equal(3, report.CommittedPoints);
        Assert.Equal(8, report.ScopePoints);
        Assert.Equal(8, report.CompletedPoints);
        Assert.Equal(3, report.CompletedCommittedPoints);
        Assert.Equal(1, report.UnestimatedIssues);
        Assert.Equal(3, report.Issues.Single(i => i.Key == "D-1").PointsAtStart);
    }

    [Fact]
    public void The_burndown_has_a_start_point_and_one_point_per_day_with_the_ideal_over_working_days()
    {
        var doneTuesday = Issue("E-1", "Done", 5, changes: Change("2026-10-27 15:00", Status("Open", "Done")));
        var open = Issue("E-2", "Open", 5);

        var report = Build(Window(), "2026-10-28 12:00", doneTuesday, open);
        var points = report.Burndown;

        Assert.Equal(15, points.Count);
        Assert.True(points[0].IsStart);
        Assert.Equal(new DateOnly(2026, 10, 26), points[0].Date);
        Assert.Equal(10, points[0].RemainingPoints);
        Assert.Equal(10, points[0].ScopePoints);
        Assert.Equal(10, points[0].IdealPoints);

        var byDate = points.Skip(1).ToDictionary(p => p.Date);
        Assert.Equal(10, byDate[new DateOnly(2026, 10, 26)].RemainingPoints);
        Assert.Equal(9, byDate[new DateOnly(2026, 10, 26)].IdealPoints);
        Assert.Equal(5, byDate[new DateOnly(2026, 10, 27)].RemainingPoints);
        Assert.Equal(1, byDate[new DateOnly(2026, 10, 27)].RemainingIssues);
        Assert.Equal(8, byDate[new DateOnly(2026, 10, 27)].IdealPoints);
        Assert.True(byDate[new DateOnly(2026, 10, 28)].HasActual);
        Assert.Equal(5, byDate[new DateOnly(2026, 10, 28)].RemainingPoints);
        Assert.False(byDate[new DateOnly(2026, 10, 29)].HasActual);
        Assert.Equal(5, byDate[new DateOnly(2026, 10, 30)].IdealPoints);
        Assert.False(byDate[new DateOnly(2026, 10, 31)].IsWorkingDay);
        Assert.Equal(5, byDate[new DateOnly(2026, 10, 31)].IdealPoints);
        Assert.Equal(4, byDate[new DateOnly(2026, 11, 2)].IdealPoints);
        Assert.Equal(0, byDate[new DateOnly(2026, 11, 6)].IdealPoints);
        Assert.Equal(0, byDate[new DateOnly(2026, 11, 8)].IdealPoints);
        Assert.Equal(10, report.WorkingDays);
        Assert.Equal(3, report.WorkingDaysElapsed);
    }

    [Fact]
    public void A_future_sprint_reports_the_planned_scope_as_commitment_and_has_no_burndown()
    {
        var planned = Issue("F-1", "Open", 5);

        var createdLater = Issue("F-2", "Open", 3, created: "2026-11-05 09:00");

        var report = Build(Window(start: "2026-11-09 09:00", end: "2026-11-22 17:00", state: SprintReportState.Future), "2026-11-02 12:00", planned, createdLater);

        Assert.Equal(T("2026-11-09 09:00"), report.AsOf);
        Assert.Equal(2, report.CommittedIssues);
        Assert.Equal(8, report.CommittedPoints);
        Assert.Equal(2, report.ScopeIssues);
        Assert.Equal(0, report.RemovedIssues);
        Assert.Empty(report.Burndown);
        Assert.Equal(0, report.WorkingDaysElapsed);
        Assert.Equal(0.0, report.CommitmentCompletion);
    }

    [Fact]
    public void Sprint_membership_follows_the_sprint_id_when_known_and_the_name_otherwise()
    {
        var carriedIn = Issue("G-1", "Open", 3,
            changes: Change("2026-10-20 10:00", SprintMove("36", "ARG 26.09.3", "36, 37", "ARG 26.09.3, ARG 26.09.4")));
        var pulledIn = Issue("G-2", "Open", 2,
            changes: Change("2026-10-28 10:00", SprintMove("36", "ARG 26.09.3", "37", SprintName)));
        var joinedUnderOldName = Issue("G-3", "Open", 1,
            changes:
            [
                Change("2026-10-28 10:00", SprintMove("37", "Sprint 4", "", "")),
                Change("2026-10-30 10:00", SprintMove("", "", "37", SprintName)),
            ]);

        var byId = Build(Window(), "2026-11-02 12:00", carriedIn, pulledIn, joinedUnderOldName);
        Assert.Equal(["G-1", "G-3"], byId.Issues.Where(i => i.InSprintAtStart).Select(i => i.Key));
        Assert.Equal(["G-1", "G-2", "G-3"], byId.Issues.Where(i => i.InSprintAtEnd).Select(i => i.Key));
        Assert.Equal(T("2026-10-28 10:00"), byId.Issues.Single(i => i.Key == "G-2").AddedAt);
        Assert.Equal(0, byId.RemovedIssues);
        Assert.Equal(6, byId.ScopePoints);

        var byName = Build(Window(jiraId: null), "2026-11-02 12:00", carriedIn, pulledIn, joinedUnderOldName);
        Assert.Equal(["G-1"], byName.Issues.Where(i => i.InSprintAtStart).Select(i => i.Key));
        Assert.Equal(["G-1", "G-2", "G-3"], byName.Issues.Where(i => i.InSprintAtEnd).Select(i => i.Key));
        Assert.Equal(T("2026-10-30 10:00"), byName.Issues.Single(i => i.Key == "G-3").AddedAt);
    }

    [Fact]
    public void Every_issue_jira_returns_for_the_sprint_is_in_it_now_whatever_its_history_says()
    {
        var staleHistory = Issue("H-1", "In Progress", 5,
            changes: Change("2026-10-28 10:00", SprintMove("37", SprintName, "", "")));
        var otherBoardsSprint = Issue("H-2", "Open", 3, sprints: [new JiraSprintValue { Id = 99, Name = SprintName }],
            changes:
            [
                Change("2026-10-28 10:00", SprintMove("99", "Sprint 4", "", "")),
                Change("2026-10-30 10:00", SprintMove("", "", "99", SprintName)),
            ]);
        var otherBoardsLateComer = Issue("H-3", "Open", 2, sprints: [new JiraSprintValue { Id = 99, Name = SprintName }],
            changes: Change("2026-10-29 10:00", SprintMove("", "", "99", SprintName)));
        var noHistoryAtAll = Issue("H-4", "Open", 1, created: "2026-10-01 08:00");

        var report = Build(Window(), "2026-11-02 12:00", staleHistory, otherBoardsSprint, otherBoardsLateComer, noHistoryAtAll);

        Assert.Equal(4, report.ScopeIssues);
        Assert.Equal(["H-1", "H-2", "H-4"], report.Issues.Where(i => i.IsCommitted).Select(i => i.Key));
        Assert.Equal(["H-3"], report.Issues.Where(i => i.IsAdded).Select(i => i.Key));
        Assert.Equal(0, report.RemovedIssues);
        Assert.Equal(0, report.IgnoredIssues);
    }

    [Fact]
    public void The_current_status_and_points_anchor_a_history_that_stops_early()
    {
        var issue = Issue("J-1", "Done", 8,
            changes:
            [
                Change("2026-10-27 10:00", Status("Open", "In Progress")),
                Change("2026-10-28 10:00", Points("5", "8")),
            ]);

        var report = Build(Window(), "2026-11-02 12:00", issue);

        var only = report.Issues.Single();
        Assert.True(only.IsCompleted);
        Assert.Equal("Done", only.Status);
        Assert.Equal(5, only.PointsAtStart);
        Assert.Equal(8, only.Points);
        Assert.Equal(T("2026-10-27 10:00"), only.DoneAt);
        Assert.Equal(5, report.Burndown[0].RemainingPoints);
        Assert.Equal(0, report.Burndown[^1].RemainingPoints);
    }

    [Fact]
    public void Issues_created_after_a_closed_sprint_was_completed_are_left_out_and_listed()
    {
        var inSprint = Issue("I-1", "Done", 5, changes: Change("2026-10-28 10:00", Status("Open", "Done")));
        var clonedLater = Issue("I-2", "Open", 3, created: "2026-11-10 09:00");

        var report = Build(Window(state: SprintReportState.Closed, completedAt: "2026-11-08 16:00"), "2026-11-12 12:00", inSprint, clonedLater);

        Assert.Equal(1, report.ScopeIssues);
        Assert.Equal(1, report.CommittedIssues);
        Assert.Equal(1, report.IgnoredIssues);
        Assert.Equal(["I-2"], report.IgnoredKeys);
        Assert.True(report.Issues.Single(i => i.Key == "I-2").IsIgnored);
        Assert.DoesNotContain(report.ByAssignee, g => g.Issues > 1);
    }

    [Fact]
    public void Issues_are_grouped_by_assignee_type_feature_and_status()
    {
        var report = Build(Window(), "2026-11-02 12:00",
            Issue("H-1", "Done", 5, assignee: "Alice", feature: "FEAT-1", changes: Change("2026-10-28 10:00", Status("Open", "Done"))),
            Issue("H-2", "In Progress", 3, assignee: "Alice", feature: "FEAT-1"),
            Issue("H-3", "Open", 8, assignee: "Bob", type: "Bug", feature: "FEAT-2"),
            Issue("H-4", "Open", null, type: "Bug"));

        Assert.Equal(["Alice", "Bob", "Unassigned"], report.ByAssignee.Select(g => g.Name));
        var alice = report.ByAssignee[0];
        Assert.Equal(2, alice.Issues);
        Assert.Equal(1, alice.CompletedIssues);
        Assert.Equal(8, alice.ScopePoints);
        Assert.Equal(5, alice.CompletedPoints);
        Assert.Equal(3, alice.OpenPoints);
        Assert.Equal(0.625, alice.Completion);
        Assert.Equal(1, report.ByAssignee[2].UnestimatedIssues);

        Assert.Equal(["Bug", "Story"], report.ByType.Select(g => g.Name));
        Assert.Equal(["FEAT-1", "FEAT-2", "(no feature)"], report.ByFeature.Select(g => g.Name));
        Assert.Equal(["Open", "In Progress", "Done"], report.ByStatus.Select(g => g.Status));
        Assert.Equal(2, report.IssuesInCategory(SprintReport.ToDoCategory));
        Assert.Equal(5, report.PointsInCategory(SprintReport.DoneCategory));
    }

    [Fact]
    public void Unknown_historical_statuses_do_not_count_as_done_but_the_current_status_category_does()
    {
        var issue = Issue("I-1", "Done", 2, changes: Change("2026-10-28 10:00", Status("Resolved", "Done")));
        issue.StatusCategory = "done";

        var report = SprintReportCalculator.Build(new SprintReportInput
        {
            SprintName = SprintName,
            Window = Window(),
            Now = T("2026-11-02 12:00"),
            Issues = [issue],
        });

        Assert.Equal(1, report.CompletedIssues);
        Assert.Equal(2, report.Burndown[0].RemainingPoints);
        Assert.Equal(0, report.Burndown.Single(p => p.Date == new DateOnly(2026, 10, 28) && !p.IsStart).RemainingPoints);
    }

    [Fact]
    public void Blocked_issues_are_counted_while_they_are_open()
    {
        var report = Build(Window(), "2026-11-02 12:00",
            Issue("J-1", "Blocked", 2),
            Issue("J-2", "Done", 2, changes: Change("2026-10-28 10:00", Status("Blocked", "Done"))));

        Assert.Equal(1, report.BlockedIssues);
    }

    [Fact]
    public void The_window_comes_from_the_jira_sprint_when_the_issues_carry_it()
    {
        var issue = Issue("K-1");
        issue.Sprints =
        [
            new JiraSprintValue { Id = 36, Name = "ARG 26.09.3", State = "CLOSED" },
            new JiraSprintValue
            {
                Id = 37, Name = " arg 26.09.4 ", State = "CLOSED", Goal = "Ship it",
                StartDate = T("2026-10-26 09:12"), EndDate = T("2026-11-08 17:00"), CompleteDate = T("2026-11-06 16:30"),
            },
        ];

        var window = SprintReportCalculator.ResolveWindow(SprintName, new DateTime(2026, 10, 26), new DateTime(2026, 11, 8),
            [issue], T("2026-11-02 12:00"), TimeSpan.FromHours(2));

        Assert.Equal(T("2026-10-26 09:12"), window.Start);
        Assert.Equal(T("2026-11-08 17:00"), window.End);
        Assert.Equal(T("2026-11-06 16:30"), window.CompletedAt);
        Assert.Equal(SprintReportState.Closed, window.State);
        Assert.True(window.DatesFromJira);
        Assert.Equal(37, window.JiraSprintId);
        Assert.Equal("Ship it", window.Goal);
        Assert.Equal(new DateOnly(2026, 10, 26), window.FirstDay);
        Assert.Equal(new DateOnly(2026, 11, 8), window.LastDay);
    }

    [Fact]
    public void Without_jira_sprint_dates_the_window_is_the_apps_sprint_in_the_issues_time_zone()
    {
        var issue = Issue("L-1");
        var start = new DateTime(2026, 10, 26);
        var end = new DateTime(2026, 11, 8);

        var active = SprintReportCalculator.ResolveWindow(SprintName, start, end, [issue], T("2026-11-02 12:00"), TimeSpan.FromHours(2));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 0, 0, Offset), active.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, Offset), active.End);
        Assert.Equal(new DateOnly(2026, 11, 8), active.LastDay);
        Assert.Equal(SprintReportState.Active, active.State);
        Assert.False(active.DatesFromJira);
        Assert.Null(active.JiraSprintId);

        Assert.Equal(SprintReportState.Future,
            SprintReportCalculator.ResolveWindow(SprintName, start, end, [issue], T("2026-10-20 12:00"), TimeSpan.Zero).State);
        Assert.Equal(SprintReportState.Closed,
            SprintReportCalculator.ResolveWindow(SprintName, start, end, [issue], T("2026-11-10 12:00"), TimeSpan.Zero).State);

        var noIssues = SprintReportCalculator.ResolveWindow(SprintName, start, end, [], T("2026-11-02 12:00"), TimeSpan.FromHours(2));
        Assert.Equal(TimeSpan.FromHours(2), noIssues.Start.Offset);
    }

    [Fact]
    public void A_jira_sprint_still_active_after_its_planned_end_extends_the_burndown_to_today()
    {
        var issue = Issue("M-1", "Open", 3);

        var report = Build(Window(start: "2026-10-26 09:00", end: "2026-11-06 17:00"), "2026-11-10 12:00", issue);

        Assert.Equal(new DateOnly(2026, 11, 10), report.Burndown[^1].Date);
        Assert.True(report.Burndown[^1].HasActual);
        Assert.Equal(0, report.Burndown[^1].IdealPoints);
        Assert.Equal(10, report.WorkingDaysElapsed);
    }
}
