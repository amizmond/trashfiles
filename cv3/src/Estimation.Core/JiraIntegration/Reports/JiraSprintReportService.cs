using Estimation.Core.Calendar.Services;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Core.PlanningIncrement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraIntegration.Reports;

public sealed record SprintCapacity(int MemberCount, double AvailableDays, double Points);

public sealed class SprintReportResult
{
    public SprintLookupStatus Status { get; init; }

    public string? ErrorMessage { get; init; }

    public SprintReport? Report { get; init; }

    public SprintCapacity? Capacity { get; init; }

    public DateTimeOffset LoadedAt { get; init; }

    public bool StatusCategoriesKnown { get; init; }
}

public interface IJiraSprintReportService
{
    Task<SprintReportResult> GetReportAsync(string userName, Sprint sprint, bool refresh = false, CancellationToken cancellationToken = default);
}

public class JiraSprintReportService : IJiraSprintReportService
{
    private static readonly TimeSpan ActiveCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ClosedCacheDuration = TimeSpan.FromHours(1);

    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IJiraSprintIssuesService _sprintIssues;
    private readonly IJiraSchemaService _schema;
    private readonly ICapacityCalendarService _calendar;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly JiraSettings _settings;

    public JiraSprintReportService(
        IDbContextFactory<EstimationDbContext> ctx,
        IJiraSprintIssuesService sprintIssues,
        IJiraSchemaService schema,
        ICapacityCalendarService calendar,
        IMemoryCache cache,
        TimeProvider time,
        IOptions<JiraSettings> settings)
    {
        _ctx = ctx;
        _sprintIssues = sprintIssues;
        _schema = schema;
        _calendar = calendar;
        _cache = cache;
        _time = time;
        _settings = settings.Value;
    }

    public async Task<SprintReportResult> GetReportAsync(string userName, Sprint sprint, bool refresh = false, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"sprint_report_{userName}_{sprint.Id}_{sprint.Name}";
        if (!refresh && _cache.TryGetValue(cacheKey, out SprintReportResult? cached) && cached is not null)
        {
            return cached;
        }

        var issues = await _sprintIssues.GetSprintIssuesAsync(userName, sprint.Name, includeHistory: true);
        if (issues.Status != SprintLookupStatus.Found)
        {
            return new SprintReportResult
            {
                Status = issues.Status,
                ErrorMessage = issues.ErrorMessage,
                LoadedAt = _time.GetUtcNow(),
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var schema = await LoadSchemaAsync(userName, cancellationToken);
        var now = _time.GetUtcNow();
        var window = SprintReportCalculator.ResolveWindow(
            sprint.Name, sprint.StartDate, sprint.EndDate, issues.Issues, now, TimeZoneInfo.Local.GetUtcOffset(sprint.StartDate));

        var report = SprintReportCalculator.Build(new SprintReportInput
        {
            SprintName = sprint.Name,
            Window = window,
            Now = now,
            Issues = issues.Issues,
            StatusCategories = schema.StatusCategories,
            StoryPointsFieldId = _settings.StoryPointsCustomFieldId,
            StoryPointsFieldNames = Names(schema.FieldName(_settings.StoryPointsCustomFieldId), "Story Points"),
            SprintFieldId = schema.SprintFieldId,
            SprintFieldNames = Names(schema.FieldName(schema.SprintFieldId), "Sprint"),
        });

        Log.Information(
            "Sprint report for '{SprintName}': {Returned} issues from Jira, {Scope} in the sprint at {AsOf}, {Committed} committed, {Added} added, {Ignored} outside the sprint",
            sprint.Name, issues.Issues.Count, report.ScopeIssues, report.AsOf, report.CommittedIssues, report.AddedIssues, report.IgnoredIssues);
        report.ByFeature = await WithFeatureTitlesAsync(report.ByFeature, cancellationToken);

        var result = new SprintReportResult
        {
            Status = SprintLookupStatus.Found,
            Report = report,
            Capacity = await CapacityAsync(sprint.TeamId, window),
            LoadedAt = now,
            StatusCategoriesKnown = schema.StatusCategories.Count > 0 || issues.Issues.All(i => i.StatusCategory is not null),
        };

        _cache.Set(cacheKey, result, window.State == SprintReportState.Closed ? ClosedCacheDuration : ActiveCacheDuration);
        return result;
    }

    private async Task<JiraSchema> LoadSchemaAsync(string userName, CancellationToken cancellationToken)
    {
        try
        {
            return await _schema.GetAsync(userName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "The Jira schema could not be read for the sprint report; status categories come from the issues");
            return JiraSchema.Empty;
        }
    }

    private static IReadOnlyCollection<string> Names(params string?[] names) =>
        names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private async Task<IReadOnlyList<SprintReportGroup>> WithFeatureTitlesAsync(
        IReadOnlyList<SprintReportGroup> groups, CancellationToken cancellationToken)
    {
        var keys = groups
            .Select(g => g.Name)
            .Where(k => !string.Equals(k, JiraIssueFilter.NoFeatureLinkLabel, StringComparison.Ordinal))
            .ToList();
        if (keys.Count == 0)
        {
            return groups;
        }

        try
        {
            await using var db = await _ctx.CreateDbContextAsync(cancellationToken);
            var titles = (await db.Features
                    .AsNoTracking()
                    .Where(f => f.JiraId != null && keys.Contains(f.JiraId))
                    .Select(f => new { f.JiraId, f.Name, f.Summary })
                    .ToListAsync(cancellationToken))
                .GroupBy(f => f.JiraId!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(f => f.Name ?? f.Summary).First(), StringComparer.OrdinalIgnoreCase);

            return groups
                .Select(g => titles.TryGetValue(g.Name, out var title) ? g with { Title = title } : g)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Feature names for the sprint report could not be loaded");
            return groups;
        }
    }

    private async Task<SprintCapacity?> CapacityAsync(int teamId, SprintReportWindow window)
    {
        try
        {
            var period = await _calendar.GetTeamRangeCapacityAsync(
                teamId, window.FirstDay.ToDateTime(TimeOnly.MinValue), window.LastDay.ToDateTime(TimeOnly.MinValue));

            await using var db = await _ctx.CreateDbContextAsync();
            var pointsPerDay = await db.Teams
                .Where(t => t.Id == teamId)
                .Select(t => t.StoryPointsPerDay)
                .FirstOrDefaultAsync();
            if (pointsPerDay <= 0)
            {
                pointsPerDay = 1m;
            }

            return new SprintCapacity(period.MemberCount, period.AvailablePersonDays, period.AvailablePersonDays * (double)pointsPerDay);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Sprint capacity for team {TeamId} could not be calculated", teamId);
            return null;
        }
    }
}
