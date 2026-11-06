using System.Globalization;
using Estimation.Core.JiraIntegration.Client;

namespace Estimation.Core.JiraIntegration.Reports;

public static class SprintReportCalculator
{
    public static SprintReportWindow ResolveWindow(
        string sprintName,
        DateTime appStart,
        DateTime appEnd,
        IEnumerable<JiraIssueResponse> issues,
        DateTimeOffset now,
        TimeSpan fallbackOffset)
    {
        var issueList = issues.ToList();
        var jira = MatchingSprint(sprintName, issueList);
        var offset = jira?.StartDate?.Offset ?? MostCommonOffset(issueList) ?? fallbackOffset;

        var start = jira?.StartDate ?? new DateTimeOffset(DateTime.SpecifyKind(appStart.Date, DateTimeKind.Unspecified), offset);
        var end = jira?.EndDate ?? new DateTimeOffset(DateTime.SpecifyKind(appEnd.Date.AddDays(1), DateTimeKind.Unspecified), offset);
        if (end <= start)
        {
            end = start.AddDays(1);
        }

        var completedAt = jira?.CompleteDate;
        var state = jira?.State?.Trim().ToUpperInvariant() switch
        {
            "CLOSED" => SprintReportState.Closed,
            "ACTIVE" => SprintReportState.Active,
            "FUTURE" => SprintReportState.Future,
            _ => completedAt is not null ? SprintReportState.Closed
                : now < start ? SprintReportState.Future
                : now >= end ? SprintReportState.Closed
                : SprintReportState.Active,
        };

        return new SprintReportWindow(start, end, completedAt, state, jira?.StartDate is not null, jira?.Id, jira?.Goal);
    }

    private static JiraSprintValue? MatchingSprint(string sprintName, List<JiraIssueResponse> issues)
    {
        var values = issues
            .SelectMany(i => i.Sprints ?? [])
            .Where(s => IsSprintName(s.Name, sprintName))
            .ToList();
        if (values.Count == 0)
        {
            return null;
        }

        var preferred = values
            .GroupBy(v => v.Id)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .First();
        return preferred.FirstOrDefault(v => v.StartDate is not null) ?? preferred.First();
    }

    private static bool IsSprintName(string? candidate, string sprintName) =>
        string.Equals(candidate?.Trim(), sprintName.Trim(), StringComparison.OrdinalIgnoreCase);

    private static TimeSpan? MostCommonOffset(List<JiraIssueResponse> issues)
    {
        return issues
            .Select(i => i.Created?.Offset)
            .OfType<TimeSpan>()
            .GroupBy(o => o)
            .OrderByDescending(g => g.Count())
            .Select(g => (TimeSpan?)g.Key)
            .FirstOrDefault();
    }

    public static SprintReport Build(SprintReportInput input)
    {
        var window = input.Window;
        var asOf = window.AsOf(input.Now);
        var sprintIds = SprintIds(input);
        var timelines = input.Issues.Select(i => new IssueTimeline(i, input, sprintIds)).ToList();

        var issues = new List<SprintReportIssue>();
        foreach (var timeline in timelines)
        {
            var inAtStart = timeline.InSprintAt(window.Start);
            var inAtEnd = timeline.InSprintAt(asOf);
            var status = timeline.StatusAt(asOf);
            var category = timeline.CategoryOf(status);
            var isDone = IsDone(category);
            issues.Add(new SprintReportIssue
            {
                Issue = timeline.Issue,
                InSprintAtStart = inAtStart,
                InSprintAtEnd = inAtEnd,
                AddedAt = !inAtStart && inAtEnd ? timeline.JoinedAfter(window.Start, asOf) : null,
                PointsAtStart = inAtStart ? timeline.PointsAt(window.Start) : null,
                Points = timeline.PointsAt(asOf),
                Status = status,
                StatusCategory = category,
                IsDone = isDone,
                DoneAt = isDone ? timeline.DoneSince(asOf) : null,
                IsBlocked = IsBlockedStatus(status),
            });
        }

        var committed = issues.Where(i => i.InSprintAtStart).ToList();
        var scope = issues.Where(i => i.InSprintAtEnd).ToList();
        var completed = scope.Where(i => i.IsDone).ToList();
        var completedCommitted = completed.Where(i => i.InSprintAtStart).ToList();
        var notCompleted = scope.Where(i => !i.IsDone).ToList();
        var added = issues.Where(i => i.IsAdded).ToList();
        var removed = issues.Where(i => i.IsRemoved).ToList();
        var ignored = issues.Where(i => i.IsIgnored).ToList();

        var committedPoints = committed.Sum(i => i.PointsAtStart ?? 0);
        var plannedDays = Days(window.FirstDay, window.LastDay).Where(IsWorkingDay).ToList();
        var asOfDay = DayOf(asOf, window.Start.Offset);
        var elapsed = window.State switch
        {
            SprintReportState.Future => 0,
            SprintReportState.Closed => plannedDays.Count,
            _ => plannedDays.Count(d => d <= asOfDay),
        };

        return new SprintReport
        {
            SprintName = input.SprintName,
            Window = window,
            AsOf = asOf,
            Issues = issues,
            CommittedIssues = committed.Count,
            CommittedPoints = committedPoints,
            AddedIssues = added.Count,
            AddedPoints = added.Sum(i => i.Points ?? 0),
            RemovedIssues = removed.Count,
            RemovedPoints = removed.Sum(i => i.PointsAtStart ?? 0),
            IgnoredIssues = ignored.Count,
            IgnoredKeys = ignored.Select(i => i.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
            ScopeIssues = scope.Count,
            ScopePoints = scope.Sum(i => i.Points ?? 0),
            CompletedIssues = completed.Count,
            CompletedPoints = completed.Sum(i => i.Points ?? 0),
            CompletedCommittedIssues = completedCommitted.Count,
            CompletedCommittedPoints = completedCommitted.Sum(i => i.PointsAtStart ?? i.Points ?? 0),
            NotCompletedIssues = notCompleted.Count,
            NotCompletedPoints = notCompleted.Sum(i => i.Points ?? 0),
            UnestimatedIssues = scope.Count(i => i.Points is null),
            BlockedIssues = notCompleted.Count(i => i.IsBlocked),
            WorkingDays = plannedDays.Count,
            WorkingDaysElapsed = elapsed,
            Burndown = window.State == SprintReportState.Future
                ? []
                : BuildBurndown(timelines, window, asOf, committedPoints, committed.Count),
            ByAssignee = Groups(scope, i => JiraIssueFilter.AssigneeLabel(i.Issue), JiraIssueFilter.UnassignedLabel,
                g => g.Select(i => i.Issue.AssigneeAvatarUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))),
            ByType = Groups(scope, i => string.IsNullOrWhiteSpace(i.Issue.IssueType) ? "—" : i.Issue.IssueType!.Trim(), "—"),
            ByFeature = Groups(scope, i => JiraIssueFilter.FeatureLinkLabel(i.Issue), JiraIssueFilter.NoFeatureLinkLabel),
            ByStatus = StatusGroups(scope),
        };
    }

    private static HashSet<string> SprintIds(SprintReportInput input)
    {
        var ids = input.Issues
            .SelectMany(i => i.Sprints ?? [])
            .Where(s => s.Id is not null && IsSprintName(s.Name, input.SprintName))
            .Select(s => s.Id!.Value.ToString(CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
        if (input.Window.JiraSprintId is { } id)
        {
            ids.Add(id.ToString(CultureInfo.InvariantCulture));
        }
        return ids;
    }

    public static bool IsWorkingDay(DateOnly day) =>
        day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday;

    public static bool IsDone(string? category) =>
        string.Equals(category, SprintReport.DoneCategory, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedStatus(string? status) =>
        status is not null && status.Contains("block", StringComparison.OrdinalIgnoreCase);

    private static DateOnly DayOf(DateTimeOffset at, TimeSpan offset) =>
        DateOnly.FromDateTime(at.ToOffset(offset).DateTime);

    private static IEnumerable<DateOnly> Days(DateOnly first, DateOnly last)
    {
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    private static List<SprintBurndownPoint> BuildBurndown(
        List<IssueTimeline> timelines,
        SprintReportWindow window,
        DateTimeOffset asOf,
        int committedPoints,
        int committedIssues)
    {
        var offset = window.Start.Offset;
        var firstDay = window.FirstDay;
        var plannedLastDay = window.LastDay;
        var asOfDay = DayOf(asOf, offset);
        var lastDay = asOfDay > plannedLastDay ? asOfDay : plannedLastDay;
        var plannedWorkingDays = Days(firstDay, plannedLastDay).Where(IsWorkingDay).ToList();

        var points = new List<SprintBurndownPoint>();
        var (startRemainingPoints, startRemainingIssues, startScopePoints, startScopeIssues) = Measure(timelines, window.Start);
        points.Add(new SprintBurndownPoint(firstDay, true, IsWorkingDay(firstDay), true,
            startRemainingPoints, startRemainingIssues, startScopePoints, startScopeIssues, committedPoints, committedIssues));

        foreach (var day in Days(firstDay, lastDay))
        {
            var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), offset);
            var dayEnd = dayStart.AddDays(1).AddTicks(-1);
            var hasActual = dayStart <= asOf;
            var at = dayEnd < asOf ? dayEnd : asOf;

            var remainingWork = plannedWorkingDays.Count(d => d > day);
            var idealShare = plannedWorkingDays.Count == 0
                ? (day >= plannedLastDay ? 0d : 1d)
                : (double)remainingWork / plannedWorkingDays.Count;

            var (remainingPoints, remainingIssues, scopePoints, scopeIssues) = hasActual ? Measure(timelines, at) : (0, 0, 0, 0);
            points.Add(new SprintBurndownPoint(day, false, IsWorkingDay(day), hasActual,
                remainingPoints, remainingIssues, scopePoints, scopeIssues,
                committedPoints * idealShare, committedIssues * idealShare));
        }

        return points;
    }

    private static (int RemainingPoints, int RemainingIssues, int ScopePoints, int ScopeIssues) Measure(
        List<IssueTimeline> timelines, DateTimeOffset at)
    {
        var remainingPoints = 0;
        var remainingIssues = 0;
        var scopePoints = 0;
        var scopeIssues = 0;
        foreach (var timeline in timelines)
        {
            if (!timeline.InSprintAt(at))
            {
                continue;
            }

            var points = timeline.PointsAt(at) ?? 0;
            scopeIssues++;
            scopePoints += points;
            if (!IsDone(timeline.CategoryOf(timeline.StatusAt(at))))
            {
                remainingIssues++;
                remainingPoints += points;
            }
        }
        return (remainingPoints, remainingIssues, scopePoints, scopeIssues);
    }

    private static List<SprintReportGroup> Groups(
        List<SprintReportIssue> scope,
        Func<SprintReportIssue, string> key,
        string lastLabel,
        Func<IEnumerable<SprintReportIssue>, string?>? avatar = null)
    {
        return scope
            .GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SprintReportGroup(
                g.Key,
                null,
                avatar?.Invoke(g),
                g.Count(),
                g.Count(i => i.IsDone),
                g.Count(i => i.Points is null),
                g.Where(i => i.InSprintAtStart).Sum(i => i.PointsAtStart ?? 0),
                g.Where(i => i.IsAdded).Sum(i => i.Points ?? 0),
                g.Sum(i => i.Points ?? 0),
                g.Where(i => i.IsDone).Sum(i => i.Points ?? 0)))
            .OrderBy(g => string.Equals(g.Name, lastLabel, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(g => g.ScopePoints)
            .ThenByDescending(g => g.Issues)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SprintStatusGroup> StatusGroups(List<SprintReportIssue> scope)
    {
        return scope
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Status) ? "—" : i.Status!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new SprintStatusGroup(g.Key, g.Select(i => i.StatusCategory).FirstOrDefault(c => c is not null), g.Count(), g.Sum(i => i.Points ?? 0)))
            .OrderBy(g => CategoryOrder(g.Category))
            .ThenByDescending(g => g.Issues)
            .ThenBy(g => g.Status, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int CategoryOrder(string? category) => category?.ToLowerInvariant() switch
    {
        SprintReport.ToDoCategory => 0,
        SprintReport.InProgressCategory => 1,
        SprintReport.DoneCategory => 2,
        _ => 3,
    };

    private sealed class IssueTimeline
    {
        private readonly SprintReportInput _input;
        private readonly HashSet<string> _sprintIds;
        private readonly DateTimeOffset _created;
        private readonly bool _initialMembership;
        private readonly string? _initialStatus;
        private readonly int? _initialPoints;
        private readonly List<(DateTimeOffset At, bool Value)> _membership;
        private readonly List<(DateTimeOffset At, string? Value)> _status;
        private readonly List<(DateTimeOffset At, int? Value)> _points;

        public IssueTimeline(JiraIssueResponse issue, SprintReportInput input, HashSet<string> sprintIds)
        {
            Issue = issue;
            _input = input;
            _sprintIds = sprintIds;
            var changelog = (issue.Changelog ?? []).OrderBy(e => e.Created).ToList();
            _created = issue.Created ?? changelog.FirstOrDefault()?.Created ?? DateTimeOffset.MinValue;

            var membership = new List<(DateTimeOffset At, bool Before)>();
            var status = new List<(DateTimeOffset At, string? Before)>();
            var points = new List<(DateTimeOffset At, int? Before)>();
            foreach (var entry in changelog)
            {
                foreach (var item in entry.Items)
                {
                    if (IsField(item, input.SprintFieldId, input.SprintFieldNames))
                    {
                        membership.Add((entry.Created, Contains(item.From, item.FromText)));
                    }
                    else if (IsStatusItem(item))
                    {
                        status.Add((entry.Created, item.FromText));
                    }
                    else if (IsField(item, input.StoryPointsFieldId, input.StoryPointsFieldNames))
                    {
                        points.Add((entry.Created, ParsePoints(item.FromText ?? item.From)));
                    }
                }
            }

            (_initialMembership, _membership) = Anchor(membership, true);
            (_initialStatus, _status) = Anchor(status, issue.Status);
            (_initialPoints, _points) = Anchor(points, issue.StoryPoints);
        }

        public JiraIssueResponse Issue { get; }

        public bool InSprintAt(DateTimeOffset at) =>
            at >= _created && ValueAt(_membership, _initialMembership, at);

        public string? StatusAt(DateTimeOffset at) =>
            at < _created ? null : ValueAt(_status, _initialStatus, at);

        public int? PointsAt(DateTimeOffset at) =>
            at < _created ? null : ValueAt(_points, _initialPoints, at);

        public string? CategoryOf(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return null;
            }
            if (_input.StatusCategories.TryGetValue(status.Trim(), out var category))
            {
                return category;
            }
            return string.Equals(status.Trim(), Issue.Status?.Trim(), StringComparison.OrdinalIgnoreCase)
                ? Issue.StatusCategory
                : null;
        }

        public DateTimeOffset? JoinedAfter(DateTimeOffset start, DateTimeOffset asOf)
        {
            DateTimeOffset? joined = _created > start && _initialMembership ? _created : null;
            foreach (var (at, value) in _membership)
            {
                if (at <= start || at > asOf)
                {
                    continue;
                }
                if (value)
                {
                    joined ??= at;
                }
                else
                {
                    joined = null;
                }
            }
            return joined ?? (_created > start ? _created : start);
        }

        public DateTimeOffset? DoneSince(DateTimeOffset asOf)
        {
            DateTimeOffset? since = IsDone(CategoryOf(_initialStatus)) && _created != DateTimeOffset.MinValue ? _created : null;
            foreach (var (at, value) in _status)
            {
                if (at > asOf)
                {
                    break;
                }
                if (IsDone(CategoryOf(value)))
                {
                    since ??= at;
                }
                else
                {
                    since = null;
                }
            }
            return since;
        }

        private static (T Initial, List<(DateTimeOffset At, T Value)> Changes) Anchor<T>(
            List<(DateTimeOffset At, T Before)> befores, T current)
        {
            var changes = new List<(DateTimeOffset At, T Value)>(befores.Count);
            for (var i = 0; i < befores.Count; i++)
            {
                changes.Add((befores[i].At, i + 1 < befores.Count ? befores[i + 1].Before : current));
            }
            return (befores.Count > 0 ? befores[0].Before : current, changes);
        }

        private bool Contains(string? ids, string? names)
        {
            if (_sprintIds.Count > 0 && !string.IsNullOrWhiteSpace(ids)
                && ids.Split(',').Any(s => _sprintIds.Contains(s.Trim())))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(names)
                   && names.Split(',').Any(n => IsSprintName(n, _input.SprintName));
        }

        private static bool IsStatusItem(JiraChangelogItem item) =>
            string.Equals(item.Field, "status", StringComparison.OrdinalIgnoreCase)
            && (item.FieldType is null || string.Equals(item.FieldType, "jira", StringComparison.OrdinalIgnoreCase));

        private static bool IsField(JiraChangelogItem item, string? fieldId, IReadOnlyCollection<string> names) =>
            (!string.IsNullOrEmpty(fieldId) && string.Equals(item.FieldId, fieldId, StringComparison.OrdinalIgnoreCase))
            || names.Any(n => string.Equals(n, item.Field, StringComparison.OrdinalIgnoreCase));

        private static int? ParsePoints(string? text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? (int)Math.Round(value)
                : null;

        private static T ValueAt<T>(List<(DateTimeOffset At, T Value)> changes, T initial, DateTimeOffset at)
        {
            var value = initial;
            foreach (var (changedAt, changed) in changes)
            {
                if (changedAt > at)
                {
                    break;
                }
                value = changed;
            }
            return value;
        }
    }
}
