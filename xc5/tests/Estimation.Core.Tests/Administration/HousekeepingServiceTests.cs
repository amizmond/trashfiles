using System.Net;
using Estimation.Core.Administration.Models;
using Estimation.Core.Administration.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Administration;

public class HousekeepingServiceTests
{
    private const string User = "admin";

    private readonly InMemoryDatabase _db = new();
    private readonly FakeJira _jira = new();
    private readonly HousekeepingService _service;

    public HousekeepingServiceTests()
    {
        _service = new HousekeepingService(_db, _jira, TimeSpan.Zero);
    }

    [Fact]
    public async Task Issues_whose_type_changed_in_Jira_are_reported_with_their_Jira_type_and_status()
    {
        await _db.SeedAsync(db => db.Features.AddRange(
            new Feature { Id = 1, JiraId = "F-1", Summary = "Still a feature", IssueType = "Feature" },
            new Feature { Id = 2, JiraId = "F-2", Summary = "Now a story", IssueType = "Feature" },
            new Feature { Id = 3, JiraId = "F-2 ", Summary = "Duplicate row", IssueType = "Feature" },
            new Feature { Id = 4, JiraId = "F-3", Summary = "Now a task", IssueType = "Feature" },
            new Feature { Id = 5, JiraId = "F-4", Summary = "Other case in Jira", IssueType = "Feature" },
            new Feature { Id = 6, JiraId = null, Summary = "No key", IssueType = "Story" },
            new Feature { Id = 7, JiraId = " ", Summary = "Blank key", IssueType = "Story" }));
        _jira.Add("F-1", "Feature", "Open");
        _jira.Add("F-2", "Story", "In Progress");
        _jira.Add("F-3", "Task ", "Done");
        _jira.Add("F-4", "feature", "Open");

        var result = await _service.CheckIssueTypesAsync(HousekeepingTable.Features, User);

        Assert.True(result.Completed);
        Assert.Equal(4, result.Total);
        Assert.Equal(4, result.Checked);
        Assert.Equal([2, 3, 4], result.Items.Select(i => i.Id));
        var story = result.Items[0];
        Assert.Equal("F-2", story.JiraKey);
        Assert.Null(story.CurrentJiraKey);
        Assert.Equal("Story", story.IssueType);
        Assert.Equal("In Progress", story.Status);
        Assert.Equal("Feature", story.ExpectedIssueType);
        Assert.Equal("Task", result.Items[2].IssueType);
        Assert.Empty(result.NotFoundKeys);
        Assert.Empty(result.MovedKeys);
        Assert.All(_jira.Searches, s => Assert.Equal(User, s.User));
    }

    [Fact]
    public async Task Keys_are_asked_in_batches_of_a_hundred_one_request_at_a_time_with_progress()
    {
        await _db.SeedAsync(db => db.BusinessOutcomes.AddRange(Enumerable.Range(1, 250)
            .Select(n => new BusinessOutcome { JiraId = $"BO-{n}", Summary = $"Outcome {n}" })));
        foreach (var n in Enumerable.Range(1, 250))
        {
            _jira.Add($"BO-{n}", "Business Outcome", "Open");
        }
        var progress = new RecordingProgress();

        var result = await _service.CheckIssueTypesAsync(HousekeepingTable.BusinessOutcomes, User, progress);

        Assert.Equal([100, 100, 50], _jira.Searches.Select(s => s.Keys.Count));
        Assert.Equal(250, _jira.Searches.SelectMany(s => s.Keys).Distinct().Count());
        Assert.Equal([0, 100, 200, 250], progress.Reports.Select(p => p.Checked));
        Assert.All(progress.Reports, p => Assert.Equal(250, p.Total));
        Assert.True(result.Completed);
        Assert.Empty(result.Items);
        Assert.Empty(_jira.Gets);
    }

    [Fact]
    public async Task Keys_Jira_does_not_return_are_listed_as_not_found_without_extra_requests()
    {
        await _db.SeedAsync(db => db.PortfolioEpics.AddRange(
            new PortfolioEpic { JiraId = "PE-1", Summary = "Found" },
            new PortfolioEpic { JiraId = "PE-2", Summary = "Deleted in Jira" }));
        _jira.Add("PE-1", "Portfolio Epic", "Open");

        var result = await _service.CheckIssueTypesAsync(HousekeepingTable.PortfolioEpics, User);

        Assert.Equal(["PE-2"], result.NotFoundKeys);
        Assert.Empty(result.Items);
        Assert.Empty(_jira.Gets);
    }

    [Fact]
    public async Task A_single_moved_issue_in_a_request_is_matched_to_its_stored_key()
    {
        await _db.SeedAsync(db => db.Features.AddRange(
            new Feature { Id = 1, JiraId = "OLD-1", Summary = "Moved and now a story" },
            new Feature { Id = 2, JiraId = "F-2", Summary = "Unchanged" }));
        _jira.Add("NEW-7", "Story", "Open");
        _jira.Add("F-2", "Feature", "Open");
        _jira.Move("OLD-1", "NEW-7");

        var result = await _service.CheckIssueTypesAsync(HousekeepingTable.Features, User);

        var item = Assert.Single(result.Items);
        Assert.Equal("OLD-1", item.JiraKey);
        Assert.Equal("NEW-7", item.CurrentJiraKey);
        Assert.Equal(new HousekeepingMovedKey("OLD-1", "NEW-7"), Assert.Single(result.MovedKeys));
        Assert.Empty(result.NotFoundKeys);
        Assert.Empty(_jira.Gets);
    }

    [Fact]
    public async Task Several_moved_issues_in_a_request_are_looked_up_one_by_one()
    {
        await _db.SeedAsync(db => db.Features.AddRange(
            new Feature { JiraId = "OLD-1", Summary = "Moved story" },
            new Feature { JiraId = "OLD-2", Summary = "Moved feature" },
            new Feature { JiraId = "GONE-1", Summary = "Deleted" }));
        _jira.Add("NEW-1", "Story", "Open");
        _jira.Add("NEW-2", "Feature", "Open");
        _jira.Move("OLD-1", "NEW-1");
        _jira.Move("OLD-2", "NEW-2");

        var result = await _service.CheckIssueTypesAsync(HousekeepingTable.Features, User);

        Assert.Equal(["GONE-1", "OLD-1", "OLD-2"], _jira.Gets.Order());
        Assert.Equal("NEW-1", Assert.Single(result.Items).CurrentJiraKey);
        Assert.Equal(["OLD-1", "OLD-2"], result.MovedKeys.Select(m => m.StoredKey));
        Assert.Equal(["GONE-1"], result.NotFoundKeys);
    }

    [Fact]
    public async Task Each_issue_names_the_table_for_its_Jira_type_and_whether_that_table_already_holds_it()
    {
        await _db.SeedAsync(db =>
        {
            db.Features.AddRange(
                new Feature { JiraId = "X-1", Summary = "Copied" },
                new Feature { JiraId = "X-2", Summary = "Not copied" },
                new Feature { JiraId = "X-3", Summary = "Story" });
            db.BusinessOutcomes.Add(new BusinessOutcome { JiraId = "X-1", Summary = "Copy", IssueType = "Feature" });
        });
        _jira.Add("X-1", "Business Outcome", "Open");
        _jira.Add("X-2", "business outcome", "Open");
        _jira.Add("X-3", "Story", "Open");

        var items = (await _service.CheckIssueTypesAsync(HousekeepingTable.Features, User)).Items.ToDictionary(i => i.Summary);

        Assert.Equal(HousekeepingTable.BusinessOutcomes, items["Copied"].IssueTypeTable);
        Assert.True(items["Copied"].ExistsInIssueTypeTable);
        Assert.Equal(HousekeepingTable.BusinessOutcomes, items["Not copied"].IssueTypeTable);
        Assert.False(items["Not copied"].ExistsInIssueTypeTable);
        Assert.Null(items["Story"].IssueTypeTable);
        Assert.False(items["Story"].ExistsInIssueTypeTable);
    }

    [Fact]
    public async Task A_failed_request_stops_the_check_and_keeps_what_was_already_checked()
    {
        await _db.SeedAsync(db => db.Features.AddRange(Enumerable.Range(1, 250)
            .Select(n => new Feature { JiraId = $"F-{n:000}", Summary = $"Feature {n}" })));
        foreach (var n in Enumerable.Range(1, 250))
        {
            _jira.Add($"F-{n:000}", n == 5 ? "Story" : "Feature", "Open");
        }
        _jira.FailOnSearch = 2;

        var result = await _service.CheckIssueTypesAsync(HousekeepingTable.Features, User);

        Assert.False(result.Completed);
        Assert.False(result.Cancelled);
        Assert.Contains("Unauthorized", result.Error);
        Assert.Equal(100, result.Checked);
        Assert.Equal(250, result.Total);
        Assert.Equal(2, _jira.Searches.Count);
        Assert.Equal("F-005", Assert.Single(result.Items).JiraKey);
    }

    [Fact]
    public async Task Stopping_returns_the_issues_checked_so_far()
    {
        await _db.SeedAsync(db => db.StrategicObjectives.AddRange(Enumerable.Range(1, 250)
            .Select(n => new StrategicObjective { JiraId = $"SO-{n:000}", Summary = $"Objective {n}" })));
        foreach (var n in Enumerable.Range(1, 250))
        {
            _jira.Add($"SO-{n:000}", n == 1 ? "Portfolio Epic" : "Strategic Objective", "Open");
        }
        using var cts = new CancellationTokenSource();
        _jira.OnSearch = _ => cts.Cancel();

        var result = await _service.CheckIssueTypesAsync(HousekeepingTable.StrategicObjectives, User, null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Null(result.Error);
        Assert.Equal(100, result.Checked);
        Assert.Single(_jira.Searches);
        Assert.Equal("SO-001", Assert.Single(result.Items).JiraKey);
    }

    [Fact]
    public async Task Counts_hold_the_distinct_Jira_keys_of_each_table()
    {
        await _db.SeedAsync(db =>
        {
            db.Features.AddRange(
                new Feature { JiraId = "F-1", Summary = "One" },
                new Feature { JiraId = "f-1 ", Summary = "Same key" },
                new Feature { JiraId = "F-2", Summary = "Two" },
                new Feature { JiraId = null, Summary = "None" },
                new Feature { JiraId = "", Summary = "Blank" });
            db.BusinessOutcomes.Add(new BusinessOutcome { JiraId = "BO-1", Summary = "One" });
        });

        var counts = await _service.CountJiraKeysAsync();

        Assert.Equal(2, counts[HousekeepingTable.Features]);
        Assert.Equal(1, counts[HousekeepingTable.BusinessOutcomes]);
        Assert.Equal(0, counts[HousekeepingTable.PortfolioEpics]);
        Assert.Equal(0, counts[HousekeepingTable.StrategicObjectives]);
    }

    [Fact]
    public async Task Removing_deletes_the_chosen_rows_and_skips_rows_gone_or_with_another_key_since_the_check()
    {
        await _db.SeedAsync(db =>
        {
            db.Features.AddRange(
                new Feature { Id = 1, JiraId = "F-1", Summary = "Story" },
                new Feature { Id = 2, JiraId = "F-9", Summary = "Key changed since" },
                new Feature { Id = 3, JiraId = "F-3", Summary = "Not chosen" });
            db.BusinessOutcomes.Add(new BusinessOutcome { Id = 1, JiraId = "BO-1 ", Summary = "Feature" });
        });

        var result = await _service.RemoveAsync(
        [
            new HousekeepingItemKey(HousekeepingTable.Features, 1, "f-1"),
            new HousekeepingItemKey(HousekeepingTable.Features, 1, "F-1"),
            new HousekeepingItemKey(HousekeepingTable.Features, 2, "F-2"),
            new HousekeepingItemKey(HousekeepingTable.Features, 99, "F-99"),
            new HousekeepingItemKey(HousekeepingTable.BusinessOutcomes, 1, "BO-1"),
        ]);

        Assert.Equal(new HousekeepingRemoveResult(2, 2), result);
        Assert.Equal([2, 3], await _db.ReadAsync(db => db.Features.OrderBy(f => f.Id).Select(f => f.Id).ToListAsync()));
        Assert.Empty(await _db.ReadAsync(db => db.BusinessOutcomes.ToListAsync()));
    }

    [Fact]
    public async Task Removing_a_strategic_objective_or_portfolio_epic_also_removes_their_links()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = 1, Name = "ART" });
            db.StrategicObjectives.AddRange(
                new StrategicObjective { Id = 1, JiraId = "SO-1", Summary = "Wrong" },
                new StrategicObjective { Id = 2, JiraId = "SO-2", Summary = "Right" });
            db.PortfolioEpics.AddRange(
                new PortfolioEpic { Id = 1, JiraId = "PE-1", Summary = "Right" },
                new PortfolioEpic { Id = 2, JiraId = "PE-2", Summary = "Wrong" });
            db.CapitalProjectStrategicObjectives.AddRange(
                new ArtStrategicObjective { CapitalProjectId = 1, StrategicObjectiveId = 1 },
                new ArtStrategicObjective { CapitalProjectId = 1, StrategicObjectiveId = 2 });
            db.StrategicObjectivePortfolioEpics.AddRange(
                new StrategicObjectivePortfolioEpic { StrategicObjectiveId = 1, PortfolioEpicId = 1 },
                new StrategicObjectivePortfolioEpic { StrategicObjectiveId = 2, PortfolioEpicId = 1 },
                new StrategicObjectivePortfolioEpic { StrategicObjectiveId = 2, PortfolioEpicId = 2 });
        });

        var result = await _service.RemoveAsync(
        [
            new HousekeepingItemKey(HousekeepingTable.StrategicObjectives, 1, "SO-1"),
            new HousekeepingItemKey(HousekeepingTable.PortfolioEpics, 2, "PE-2"),
        ]);

        Assert.Equal(new HousekeepingRemoveResult(2, 0), result);
        Assert.Equal([2], await _db.ReadAsync(db => db.StrategicObjectives.Select(x => x.Id).ToListAsync()));
        Assert.Equal([1], await _db.ReadAsync(db => db.PortfolioEpics.Select(x => x.Id).ToListAsync()));
        Assert.Equal([2], await _db.ReadAsync(db => db.CapitalProjectStrategicObjectives.Select(x => x.StrategicObjectiveId).ToListAsync()));
        var link = Assert.Single(await _db.ReadAsync(db => db.StrategicObjectivePortfolioEpics
            .Select(x => new { x.StrategicObjectiveId, x.PortfolioEpicId })
            .ToListAsync()));
        Assert.Equal(2, link.StrategicObjectiveId);
        Assert.Equal(1, link.PortfolioEpicId);
    }

    [Fact]
    public async Task Removing_nothing_returns_zero_counts()
    {
        Assert.Equal(new HousekeepingRemoveResult(0, 0), await _service.RemoveAsync([]));
    }

    [Theory]
    [InlineData("Feature", HousekeepingTable.Features)]
    [InlineData(" business outcome ", HousekeepingTable.BusinessOutcomes)]
    [InlineData("PORTFOLIO EPIC", HousekeepingTable.PortfolioEpics)]
    [InlineData("Strategic Objective", HousekeepingTable.StrategicObjectives)]
    [InlineData("Story", null)]
    [InlineData("Task", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void An_issue_type_maps_to_the_table_that_holds_it(string? issueType, HousekeepingTable? expected)
    {
        Assert.Equal(expected, HousekeepingTables.ForIssueType(issueType));
    }

    private sealed class RecordingProgress : IProgress<HousekeepingCheckProgress>
    {
        public List<HousekeepingCheckProgress> Reports { get; } = [];

        public void Report(HousekeepingCheckProgress value) => Reports.Add(value);
    }

    internal sealed class FakeJira : IJiraIssueTypeLookup
    {
        private readonly Dictionary<string, JiraIssueTypeInfo> _issues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _moved = new(StringComparer.OrdinalIgnoreCase);

        public List<(string User, IReadOnlyCollection<string> Keys)> Searches { get; } = [];

        public List<string> Gets { get; } = [];

        public int? FailOnSearch { get; set; }

        public Action<int>? OnSearch { get; set; }

        public void Add(string key, string issueType, string status) => _issues[key] = new JiraIssueTypeInfo(key, issueType, status);

        public void Move(string oldKey, string newKey) => _moved[oldKey] = newKey;

        public Task<IReadOnlyList<JiraIssueTypeInfo>> SearchAsync(string userName, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            Searches.Add((userName, keys.ToList()));
            OnSearch?.Invoke(Searches.Count);
            if (FailOnSearch == Searches.Count)
            {
                throw new JiraSearchException(HttpStatusCode.Unauthorized, "token expired");
            }

            IReadOnlyList<JiraIssueTypeInfo> found = keys
                .Select(Resolve)
                .OfType<JiraIssueTypeInfo>()
                .DistinctBy(i => i.Key)
                .ToList();
            return Task.FromResult(found);
        }

        public Task<JiraIssueTypeInfo?> GetAsync(string userName, string key, CancellationToken cancellationToken)
        {
            Gets.Add(key);
            return Task.FromResult(Resolve(key));
        }

        private JiraIssueTypeInfo? Resolve(string key) =>
            _issues.GetValueOrDefault(_moved.GetValueOrDefault(key) ?? key);
    }
}
