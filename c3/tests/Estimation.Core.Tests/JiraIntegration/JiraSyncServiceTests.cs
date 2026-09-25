using System.Globalization;
using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraSyncServiceTests
{
    private static readonly DateTime OldWatermark = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryDatabase _db = new();
    private readonly FakeJira _jira = new();
    private readonly RecordingWriter _writer = new();
    private readonly JiraSyncService _service;

    public JiraSyncServiceTests()
    {
        _service = new JiraSyncService(_db, _jira, _writer);
    }

    private static string ProjectOf(string issueKey) => issueKey[..issueKey.IndexOf('-')];

    private sealed class FakeJira : IJiraIssueService
    {
        public List<string> Queries { get; } = [];

        public Func<Task>? DuringSearch { get; set; }

        public List<JiraIssueResponse> Issues { get; } = [];

        public HashSet<string> FailingKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Func<string, bool>? FailWhen { get; set; }

        public async Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql)
        {
            Queries.Add(jql);
            if (DuringSearch is not null)
            {
                await DuringSearch();
            }

            if (FailingKeys.Any(k => jql.StartsWith($"project = \"{k}\"", StringComparison.Ordinal))
                || FailWhen?.Invoke(jql) == true)
            {
                throw new InvalidOperationException("Jira is down");
            }

            return Issues
                .Where(i => jql.StartsWith($"project = \"{ProjectOf(i.Key)}\"", StringComparison.Ordinal))
                .Where(i => jql.Contains($"\"{i.IssueType}\""))
                .ToList();
        }

        public Task<string> CreateIssueAsync(string userName, JiraCreateIssueRequest request) => throw new NotSupportedException();
        public Task UpdateIssueAsync(string userName, string issueKey, JiraUpdateIssueRequest request) => throw new NotSupportedException();
        public Task UpdateIssueWithStatusAsync(string userName, string issueKey, JiraUpdateIssueRequest request, string? targetStatusName) => throw new NotSupportedException();
        public Task<JiraIssueResponse?> GetIssueAsync(string userName, string issueKey) => throw new NotSupportedException();
        public Task<JiraSearchPage> SearchIssuesPageAsync(string userName, string jql, int maxResults) => throw new NotSupportedException();
        public Task<List<JiraIssueResponse>> GetIssuesByKeysAsync(string userName, List<string> issueKeys) => throw new NotSupportedException();
        public Task<List<JiraTransition>> GetTransitionsAsync(string userName, string issueKey) => throw new NotSupportedException();
        public Task<bool> TransitionToStatusAsync(string userName, string issueKey, string targetStatusName) => throw new NotSupportedException();
    }

    private sealed record WriteCall(HierarchyEntityType Type, bool Background, IReadOnlyList<string> Keys);

    private sealed class RecordingWriter : IJiraSyncWriter
    {
        public List<WriteCall> Calls { get; } = [];

        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> FailingKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Warnings { get; } = [];

        public IReadOnlyList<string> CallLog() =>
            Calls.Select(c => $"{c.Type}: {string.Join(", ", c.Keys)}").ToList();

        public IReadOnlyList<string> KeysWritten(HierarchyEntityType type = HierarchyEntityType.Feature) =>
            Calls.Where(c => c.Type == type).SelectMany(c => c.Keys).Order().ToList();

        public Task<JiraSyncWriteResult> WriteAsync(
            HierarchyEntityType type,
            IReadOnlyList<JiraIssueResponse> issues,
            bool background,
            CancellationToken cancellationToken)
        {
            if (issues.Any(i => FailingKeys.Contains(ProjectOf(i.Key))))
            {
                throw new InvalidOperationException("The database is locked");
            }

            Calls.Add(new WriteCall(type, background, issues.Select(i => i.Key).ToList()));
            var created = issues.Count(i => !Existing.Contains(i.Key));
            return Task.FromResult(new JiraSyncWriteResult(created, issues.Count - created, Warnings.ToList()));
        }
    }

    private static JiraToken ServiceToken() => new()
    {
        UserName = JiraSyncSettings.DefaultServiceAccountUserName,
        AccessToken = "t",
        AccessTokenSecret = "s",
        Created = OldWatermark
    };

    private static Art Art(int id, string name, params ArtJiraKey[] keys) =>
        new() { Id = id, Name = name, JiraKeys = keys.ToList() };

    private static ArtJiraKey Key(string key, string? components = null, string? labels = null) =>
        new() { JiraKey = key, Components = components, Labels = labels };

    private static JiraSyncKey Syncing(string key, string issueTypes = "Feature") => new()
    {
        JiraKey = key,
        CreateNotExisted = true,
        UpdateExisted = true,
        IssueTypesCsv = issueTypes,
        LastSyncedWatermarkUtc = OldWatermark
    };

    private static JiraSyncKey UpdateOnly(string key) => new()
    {
        JiraKey = key,
        UpdateExisted = true,
        IssueTypesCsv = "Feature",
        LastSyncedWatermarkUtc = OldWatermark
    };

    private static string JiraDate(DateTime value) => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private Task SeedKeysAsync(params JiraSyncKey[] keys) => _db.SeedAsync(db =>
    {
        db.JiraTokens.Add(ServiceToken());
        db.JiraSyncSettings.Add(new JiraSyncSettings { JiraTimeZoneId = "UTC" });
        db.JiraSyncKeys.AddRange(keys);
    });

    private Task SeedArtsAsync(params Art[] arts) => _db.SeedAsync(db => db.CapitalProjects.AddRange(arts));

    private async Task SeedFeaturesAsync(params string[] jiraIds)
    {
        await _db.SeedAsync(db =>
        {
            foreach (var jiraId in jiraIds)
            {
                db.Features.Add(new Feature { JiraId = jiraId, ProjectKey = ProjectOf(jiraId), Summary = jiraId });
            }
        });
        _writer.Existing.UnionWith(jiraIds);
    }

    private void InJira(
        string key,
        string type = "Feature",
        string? status = null,
        string[]? components = null,
        string[]? labels = null) =>
        _jira.Issues.Add(new JiraIssueResponse
        {
            Key = key,
            Summary = key,
            IssueType = type,
            Status = status,
            Components = components?.ToList(),
            Labels = labels?.ToList()
        });

    private Task<JiraSyncKey> RowAsync(string key) =>
        _db.ReadAsync(db => db.JiraSyncKeys.AsNoTracking().SingleAsync(k => k.JiraKey == key));

    private async Task<DateTime?> WatermarkAsync(string key) => (await RowAsync(key)).LastSyncedWatermarkUtc;

    private Task<List<string>> RowKeysAsync() =>
        _db.ReadAsync(db => db.JiraSyncKeys.Select(k => k.JiraKey).OrderBy(k => k).ToListAsync());

    [Fact]
    public async Task Each_key_is_fetched_once_per_entity_type_with_its_own_filters()
    {
        await SeedKeysAsync(
            new JiraSyncKey
            {
                JiraKey = "PAY",
                CreateNotExisted = true,
                UpdateExisted = true,
                IssueTypesCsv = "Feature, Story, Business Outcome",
                LabelsCsv = "web, mobile",
                StatusesCsv = "Open",
                LastSyncedWatermarkUtc = OldWatermark
            },
            new JiraSyncKey
            {
                JiraKey = "LOAN",
                UpdateExisted = true,
                IssueTypesCsv = "Feature",
                DateFilterMode = JiraSyncDateFilterMode.Created,
                SinceFloorUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
                LastSyncedWatermarkUtc = OldWatermark
            });

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[]
        {
            "project = \"PAY\" AND issuetype in (\"Business Outcome\") AND labels in (\"web\", \"mobile\") AND status in (\"Open\") AND updated >= \"2026-09-01 00:00\" ORDER BY created ASC, key ASC",
            "project = \"LOAN\" AND issuetype in (\"Feature\") AND created >= \"2026-09-10 00:00\" ORDER BY created ASC, key ASC",
            "project = \"PAY\" AND issuetype in (\"Feature\", \"Story\") AND labels in (\"web\", \"mobile\") AND status in (\"Open\") AND updated >= \"2026-09-01 00:00\" ORDER BY created ASC, key ASC",
        }, _jira.Queries);
        Assert.Contains("Jira requests (3):", history.Message);
        Assert.Contains("  • LOAN: project = \"LOAN\" AND issuetype in (\"Feature\") AND created >=", history.Message);
    }

    [Fact]
    public async Task A_key_shared_by_two_arts_is_fetched_and_written_once()
    {
        await SeedArtsAsync(Art(1, "Payments", Key("PAY")), Art(2, "Cards", Key("PAY", components: "Cards")));
        await SeedKeysAsync(Syncing("PAY"));
        InJira("PAY-1", components: ["Cards"]);
        InJira("PAY-2");

        await _service.RunSyncAsync("test");

        Assert.StartsWith("project = \"PAY\" AND issuetype in (\"Feature\")", Assert.Single(_jira.Queries));
        Assert.Equal(new[] { "Feature: PAY-1, PAY-2" }, _writer.CallLog());
    }

    [Fact]
    public async Task Parents_of_every_key_are_written_before_any_key_writes_its_features()
    {
        await SeedKeysAsync(
            Syncing("APP"),
            Syncing("ZED", "Business Outcome,Portfolio Epic,Strategic Objective"));
        InJira("APP-1");
        InJira("APP-2");
        InJira("ZED-1", type: "Strategic Objective");
        InJira("ZED-2", type: "Business Outcome");
        InJira("ZED-3", type: "Portfolio Epic");

        await _service.RunSyncAsync("test");

        Assert.Equal(new[]
        {
            "StrategicObjective: ZED-1",
            "PortfolioEpic: ZED-3",
            "BusinessOutcome: ZED-2",
            "Feature: APP-1, APP-2",
        }, _writer.CallLog());
        Assert.All(_writer.Calls, c => Assert.True(c.Background));
        Assert.Equal(4, _jira.Queries.Count);
    }

    private async Task SeedMatcherScenarioAsync()
    {
        await SeedArtsAsync(Art(1, "Web", Key("PAY", labels: "web")), Art(2, "Cards", Key("PAY", components: "Cards")));
        await SeedKeysAsync(Syncing("PAY"), Syncing("LOAN"));
        await SeedFeaturesAsync("PAY-4");
        InJira("PAY-1", labels: ["web"]);
        InJira("PAY-2", components: ["Cards"], labels: ["web"]);
        InJira("PAY-3");
        InJira("PAY-4", labels: ["mobile"]);
        InJira("LOAN-1");
    }

    [Fact]
    public async Task New_features_are_written_whatever_art_the_matcher_finds_for_them()
    {
        await SeedMatcherScenarioAsync();

        await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "LOAN-1", "PAY-1", "PAY-2", "PAY-3", "PAY-4" }, _writer.KeysWritten());
    }

    [Fact]
    public async Task The_history_describes_every_key_and_where_its_new_features_belong()
    {
        await SeedMatcherScenarioAsync();

        var history = await _service.RunSyncAsync("test");

        Assert.Equal("Success", history.Status);
        Assert.Equal(2, history.ProjectsProcessed);
        Assert.Equal(5, history.IssuesFetched);
        Assert.Equal(4, history.Created);
        Assert.Equal(1, history.Updated);
        Assert.StartsWith(
            "2 key(s) processed, 5 fetched, 4 created, 1 updated, 0 issue(s) skipped, 0 key(s) failed.",
            history.Message);
        Assert.Contains("Keys (2):", history.Message);
        Assert.Contains("  • LOAN: fetched 1, created 1, updated 0; new Features by ART: no ART 1", history.Message);
        Assert.Contains(
            "  • PAY: fetched 4, created 3, updated 1; new Features by ART: no matching ART 1, several ARTs 1, Web 1",
            history.Message);
        Assert.Contains("Created (4): LOAN-1, PAY-1, PAY-2, PAY-3", history.Message);
        Assert.Contains("Updated (1): PAY-4", history.Message);
    }

    [Fact]
    public async Task Writer_warnings_are_counted_on_the_key_line_and_listed()
    {
        await SeedKeysAsync(Syncing("PAY"));
        InJira("PAY-1");
        _writer.Warnings.Add("PAY-1: team Falcons not found");

        var history = await _service.RunSyncAsync("test");

        Assert.Contains("  • PAY: fetched 1, created 1, updated 0, 1 warning(s)", history.Message);
        Assert.Contains("Warnings (1):", history.Message);
        Assert.Contains("  • PAY-1: team Falcons not found", history.Message);
    }

    [Fact]
    public async Task Exclude_create_statuses_skip_new_issues_but_still_update_existing_ones()
    {
        var pay = Syncing("PAY");
        pay.ExcludeCreateStatusesCsv = "Done, Cancelled";
        await SeedKeysAsync(pay);
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1", status: "Done");
        InJira("PAY-2", status: "done");
        InJira("PAY-3", status: "Open");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1", "PAY-3" }, _writer.KeysWritten());
        Assert.Contains("  • PAY: fetched 3, created 1, updated 1, 1 not created (status)", history.Message);
    }

    [Fact]
    public async Task An_update_only_key_refreshes_existing_issues_and_creates_none()
    {
        await SeedKeysAsync(UpdateOnly("PAY"));
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1");
        InJira("PAY-2");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1" }, _writer.KeysWritten());
        Assert.Equal(0, history.Created);
        Assert.Equal(1, history.Updated);
    }

    [Fact]
    public async Task A_create_only_key_creates_new_issues_and_leaves_existing_ones_alone()
    {
        var pay = Syncing("PAY");
        pay.UpdateExisted = false;
        await SeedKeysAsync(pay);
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1");
        InJira("PAY-2");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-2" }, _writer.KeysWritten());
        Assert.Equal(1, history.Created);
        Assert.Equal(0, history.Updated);
    }

    [Fact]
    public async Task Inactive_keys_and_art_keys_that_are_not_configured_are_not_fetched()
    {
        await SeedArtsAsync(Art(1, "Payments", Key("PAY"), Key("CARD"), Key("LOAN")));
        var card = Syncing("CARD");
        card.CreateNotExisted = false;
        card.UpdateExisted = false;
        await SeedKeysAsync(Syncing("PAY"), card);
        InJira("CARD-1");
        InJira("LOAN-1");

        var history = await _service.RunSyncAsync("test");

        Assert.StartsWith("project = \"PAY\"", Assert.Single(_jira.Queries));
        Assert.Empty(_writer.Calls);
        Assert.Equal(OldWatermark, await WatermarkAsync("CARD"));
        Assert.StartsWith("1 key(s) processed", history.Message);
    }

    [Fact]
    public async Task A_key_without_issue_types_is_skipped_with_a_line()
    {
        var pay = Syncing("PAY");
        pay.IssueTypesCsv = null;
        await SeedKeysAsync(pay);

        var history = await _service.RunSyncAsync("test");

        Assert.Empty(_jira.Queries);
        Assert.Equal("Success", history.Status);
        Assert.Contains("  • PAY: skipped — no issue types selected", history.Message);
        Assert.Equal(OldWatermark, await WatermarkAsync("PAY"));
    }

    [Fact]
    public async Task A_key_whose_fetch_fails_does_not_stop_the_other_keys()
    {
        await SeedKeysAsync(Syncing("BAD", "Strategic Objective,Feature"), Syncing("PAY"));
        _jira.FailingKeys.Add("BAD");
        InJira("BAD-1");
        InJira("PAY-1");

        var before = DateTime.UtcNow;
        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "Feature: PAY-1" }, _writer.CallLog());
        Assert.Single(_jira.Queries, q => q.StartsWith("project = \"BAD\"", StringComparison.Ordinal));
        Assert.Equal(OldWatermark, await WatermarkAsync("BAD"));
        Assert.True(await WatermarkAsync("PAY") >= before);
        Assert.Equal("Success", history.Status);
        Assert.Equal(1, history.ProjectsProcessed);
        Assert.Equal(1, history.Failed);
        Assert.Contains("1 key(s) processed", history.Message);
        Assert.Contains("1 key(s) failed.", history.Message);
        Assert.Contains("  • BAD: FAILED — Jira is down", history.Message);
    }

    [Fact]
    public async Task A_key_whose_write_fails_keeps_its_watermark_and_skips_its_later_passes()
    {
        await SeedKeysAsync(Syncing("BAD", "Strategic Objective,Feature"), Syncing("PAY"));
        _writer.FailingKeys.Add("BAD");
        InJira("BAD-1", type: "Strategic Objective");
        InJira("BAD-2");
        InJira("PAY-1");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "Feature: PAY-1" }, _writer.CallLog());
        Assert.Single(_jira.Queries, q => q.StartsWith("project = \"BAD\"", StringComparison.Ordinal));
        Assert.Equal(OldWatermark, await WatermarkAsync("BAD"));
        Assert.True(await WatermarkAsync("PAY") > OldWatermark);
        Assert.Contains("  • BAD: FAILED — The database is locked", history.Message);
    }

    [Fact]
    public async Task A_key_failing_in_a_later_pass_still_reports_what_its_earlier_passes_wrote()
    {
        await SeedKeysAsync(Syncing("BAD", "Strategic Objective,Feature"));
        _jira.FailWhen = jql => jql.StartsWith("project = \"BAD\"", StringComparison.Ordinal) && jql.Contains("\"Feature\"");
        InJira("BAD-1", type: "Strategic Objective");
        InJira("BAD-2");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "StrategicObjective: BAD-1" }, _writer.CallLog());
        Assert.Equal(OldWatermark, await WatermarkAsync("BAD"));
        Assert.Equal("Failed", history.Status);
        Assert.Equal(1, history.Created);
        Assert.Contains("  • BAD: FAILED after creating 1, updating 0 — Jira is down", history.Message);
        Assert.Contains("Created (1): BAD-1", history.Message);
    }

    [Fact]
    public async Task An_issue_returned_twice_by_jira_is_written_once()
    {
        await SeedKeysAsync(Syncing("PAY"));
        InJira("PAY-1");
        InJira("PAY-1");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "Feature: PAY-1" }, _writer.CallLog());
        Assert.Equal(1, history.IssuesFetched);
        Assert.Equal(1, history.Created);
    }

    [Fact]
    public async Task A_run_where_every_key_fails_is_marked_failed()
    {
        await SeedKeysAsync(Syncing("BAD"));
        _jira.FailingKeys.Add("BAD");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal("Failed", history.Status);
        Assert.Equal(0, history.ProjectsProcessed);
        Assert.Equal(1, history.Failed);
        Assert.Equal(OldWatermark, await WatermarkAsync("BAD"));
    }

    [Fact]
    public async Task A_run_without_a_connected_service_account_fails_without_fetching()
    {
        await _db.SeedAsync(db => db.JiraSyncKeys.Add(Syncing("PAY")));

        var history = await _service.RunSyncAsync("test");

        Assert.Equal("Failed", history.Status);
        Assert.Contains("is not connected", history.Message);
        Assert.Empty(_jira.Queries);
    }

    [Fact]
    public async Task A_synced_key_gets_the_run_start_as_its_watermark()
    {
        await SeedKeysAsync(Syncing("PAY"));
        var searchedAt = DateTime.MinValue;
        _jira.DuringSearch = () =>
        {
            searchedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        };

        var before = DateTime.UtcNow;
        await _service.RunSyncAsync("test");

        var watermark = await WatermarkAsync("PAY");
        Assert.InRange(watermark.GetValueOrDefault(), before, searchedAt);
    }

    [Fact]
    public async Task The_watermark_is_the_date_floor_of_the_next_sync()
    {
        await SeedKeysAsync(Syncing("PAY"));

        await _service.RunSyncAsync("test");
        var watermark = (await WatermarkAsync("PAY")).GetValueOrDefault();
        await _service.RunSyncAsync("test");

        Assert.Equal(2, _jira.Queries.Count);
        Assert.Equal(
            $"project = \"PAY\" AND issuetype in (\"Feature\") AND updated >= \"{JiraDate(watermark)}\" ORDER BY created ASC, key ASC",
            _jira.Queries[1]);
    }

    [Fact]
    public async Task A_watermark_reset_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        await SeedKeysAsync(Syncing("PAY"));
        _jira.DuringSearch = () => _service.ResetWatermarkAsync("PAY");

        var history = await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync("PAY"));
        Assert.Contains(
            "PAY: its settings, ARTs or watermark changed during the sync, so the next sync starts over",
            history.Message);
    }

    [Fact]
    public async Task A_settings_change_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        var pay = Syncing("PAY");
        pay.LastSyncedWatermarkUtc = null;
        await SeedKeysAsync(pay);
        _jira.DuringSearch = () => _service.SaveKeyAsync(Syncing("PAY", "Feature,Story"));

        var history = await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync("PAY"));
        Assert.Contains("PAY: its settings, ARTs or watermark changed during the sync", history.Message);
    }

    [Fact]
    public async Task An_art_starting_to_use_the_key_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        await SeedArtsAsync(Art(1, "Payments", Key("PAY")), Art(2, "Cards"));
        await SeedKeysAsync(Syncing("PAY"));
        _jira.DuringSearch = () => _db.SeedAsync(db =>
            db.CapitalProjectJiraKeys.Add(new ArtJiraKey { CapitalProjectId = 2, JiraKey = "PAY", Components = "Cards" }));

        await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync("PAY"));
    }

    [Fact]
    public async Task An_art_leaving_the_key_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        await SeedArtsAsync(Art(1, "Payments", Key("PAY")), Art(2, "Cards", Key("PAY", components: "Cards")));
        await SeedKeysAsync(Syncing("PAY"));
        _jira.DuringSearch = () => _db.SeedAsync(db =>
            db.CapitalProjectJiraKeys.RemoveRange(db.CapitalProjectJiraKeys.Where(k => k.CapitalProjectId == 2)));

        await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync("PAY"));
    }

    [Fact]
    public async Task A_components_edit_while_the_sync_runs_keeps_the_new_watermark()
    {
        await SeedArtsAsync(Art(1, "Payments", Key("PAY")));
        await SeedKeysAsync(Syncing("PAY"));
        _jira.DuringSearch = () => _db.SeedAsync(db =>
            db.CapitalProjectJiraKeys.Single(k => k.JiraKey == "PAY").Components = "Cards");

        var history = await _service.RunSyncAsync("test");

        Assert.True(await WatermarkAsync("PAY") > OldWatermark);
        Assert.DoesNotContain("changed during the sync", history.Message);
    }

    [Fact]
    public async Task A_key_removed_while_the_sync_runs_does_not_fail_the_run()
    {
        await SeedKeysAsync(Syncing("LOAN"), Syncing("PAY"));
        _jira.DuringSearch = () => _service.RemoveKeyAsync("LOAN");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal("Success", history.Status);
        Assert.Equal(new[] { "PAY" }, await RowKeysAsync());
        Assert.True(await WatermarkAsync("PAY") > OldWatermark);
        Assert.Contains("  • LOAN: the key was removed during the sync", history.Message);
    }

    [Fact]
    public async Task The_key_list_holds_every_art_key_and_every_configured_key()
    {
        await SeedArtsAsync(
            Art(1, "Payments", Key("PAY")),
            Art(2, "Cards", Key("PAY", components: "Cards"), Key("CARD")));
        await _db.SeedAsync(db =>
        {
            db.JiraSyncKeys.Add(Syncing("PAY"));
            db.JiraSyncKeys.Add(new JiraSyncKey { JiraKey = "LOAN" });
        });

        var keys = await _service.GetKeysAsync();

        Assert.Equal(new[] { "CARD", "LOAN", "PAY" }, keys.Select(k => k.JiraKey));

        var card = keys[0];
        Assert.Null(card.Settings);
        Assert.False(card.IsConfigured);
        Assert.Equal(new[] { "Cards" }, card.Arts.Select(a => a.ArtName));

        var loan = keys[1];
        Assert.NotNull(loan.Settings);
        Assert.False(loan.IsUsedByArt);

        var pay = keys[2];
        Assert.True(pay.Settings!.CreateNotExisted);
        Assert.Equal(new[] { "Cards", "Payments" }, pay.Arts.Select(a => a.ArtName));
        Assert.Equal("Cards", pay.Arts[0].Components);
        Assert.Empty(pay.Conflicts);
    }

    [Fact]
    public async Task Two_unfiltered_arts_on_one_key_are_listed_as_a_conflict()
    {
        await SeedArtsAsync(Art(1, "Alpha", Key("PAY")), Art(2, "Beta", Key("PAY")));

        var key = Assert.Single(await _service.GetKeysAsync());

        var conflict = Assert.Single(key.Conflicts);
        Assert.Equal(new[] { 1, 2 }, conflict.ArtIds);
        Assert.True(conflict.Unfiltered);
    }

    [Fact]
    public async Task Saving_an_art_key_that_is_not_configured_creates_its_row()
    {
        await SeedArtsAsync(Art(1, "Payments", Key("PAY")));

        var result = await _service.SaveKeyAsync(
            new JiraSyncKey { JiraKey = " pay ", CreateNotExisted = true, IssueTypesCsv = "Feature" });

        Assert.False(result.WatermarkReset);
        var row = await RowAsync("PAY");
        Assert.True(row.CreateNotExisted);
        Assert.False(row.UpdateExisted);
        Assert.Equal("Feature", row.IssueTypesCsv);
    }

    [Fact]
    public async Task Saving_a_key_that_no_art_uses_and_that_has_no_row_is_refused()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SaveKeyAsync(new JiraSyncKey { JiraKey = "PAY", UpdateExisted = true }));

        Assert.Contains("no longer on the list", ex.Message);
        Assert.Empty(await RowKeysAsync());
    }

    [Theory]
    [InlineData("issue types widened", true)]
    [InlineData("create switched on", true)]
    [InlineData("floor changed", true)]
    [InlineData("fetch labels changed", true)]
    [InlineData("date mode changed", true)]
    [InlineData("update switched off", false)]
    [InlineData("same issue types in another case", false)]
    [InlineData("nothing changed", false)]
    public async Task Saving_a_key_resets_its_watermark_only_when_the_fetch_must_start_over(string change, bool reset)
    {
        await _db.SeedAsync(db => db.JiraSyncKeys.Add(UpdateOnly("PAY")));
        var settings = UpdateOnly("PAY");
        switch (change)
        {
            case "issue types widened":
                settings.IssueTypesCsv = "Feature,Story";
                break;
            case "create switched on":
                settings.CreateNotExisted = true;
                break;
            case "floor changed":
                settings.SinceFloorUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
                break;
            case "fetch labels changed":
                settings.LabelsCsv = "web";
                break;
            case "date mode changed":
                settings.DateFilterMode = JiraSyncDateFilterMode.Created;
                break;
            case "update switched off":
                settings.UpdateExisted = false;
                break;
            case "same issue types in another case":
                settings.IssueTypesCsv = " feature ";
                break;
        }

        var result = await _service.SaveKeyAsync(settings);

        Assert.Equal(reset, result.WatermarkReset);
        Assert.Equal(reset ? (DateTime?)null : OldWatermark, await WatermarkAsync("PAY"));
    }

    [Fact]
    public async Task Saving_a_key_that_never_synced_reports_no_watermark_reset()
    {
        var pay = UpdateOnly("PAY");
        pay.LastSyncedWatermarkUtc = null;
        await _db.SeedAsync(db => db.JiraSyncKeys.Add(pay));

        var result = await _service.SaveKeyAsync(Syncing("PAY", "Feature,Story"));

        Assert.False(result.WatermarkReset);
        Assert.Equal("Feature,Story", (await RowAsync("PAY")).IssueTypesCsv);
    }

    [Fact]
    public async Task Adding_a_key_normalizes_it_and_leaves_its_sync_off()
    {
        var added = await _service.AddKeyAsync("  loan ");

        Assert.Equal("LOAN", added.JiraKey);
        var row = await RowAsync("LOAN");
        Assert.False(row.CreateNotExisted);
        Assert.False(row.UpdateExisted);
        Assert.False(row.IsActive);
        Assert.Null(row.LastSyncedWatermarkUtc);
    }

    [Theory]
    [InlineData("1LOAN")]
    [InlineData("LO-AN")]
    [InlineData("ABCDEFGHIJK")]
    public async Task Adding_a_key_with_an_invalid_format_is_refused(string key)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddKeyAsync(key));

        Assert.StartsWith("Not a valid Jira project key", ex.Message);
        Assert.Empty(await RowKeysAsync());
    }

    [Fact]
    public async Task Adding_a_blank_key_is_refused()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddKeyAsync("   "));

        Assert.Equal("Enter a Jira project key.", ex.Message);
    }

    [Fact]
    public async Task Adding_a_key_already_on_the_list_is_refused()
    {
        await _db.SeedAsync(db => db.JiraSyncKeys.Add(new JiraSyncKey { JiraKey = "LOAN" }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddKeyAsync("loan"));

        Assert.Equal("Jira key LOAN is already on the list.", ex.Message);
        Assert.Equal(new[] { "LOAN" }, await RowKeysAsync());
    }

    [Fact]
    public async Task Adding_a_key_an_art_uses_is_refused_with_the_art_names()
    {
        await SeedArtsAsync(Art(1, "Payments", Key("PAY")), Art(2, "Cards", Key("PAY", components: "Cards")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddKeyAsync("PAY"));

        Assert.Equal("Jira key PAY is already on the list: Cards, Payments use it.", ex.Message);
        Assert.Empty(await RowKeysAsync());
    }

    [Fact]
    public async Task Removing_a_key_an_art_uses_is_refused()
    {
        await SeedArtsAsync(Art(1, "Payments", Key("PAY")));
        await _db.SeedAsync(db => db.JiraSyncKeys.Add(Syncing("PAY")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RemoveKeyAsync("pay"));

        Assert.Equal("Jira key PAY can't be removed: Payments uses it. Remove it from the ART first.", ex.Message);
        Assert.Equal(new[] { "PAY" }, await RowKeysAsync());
    }

    [Fact]
    public async Task Removing_a_key_deletes_its_row_and_label_cache_and_keeps_its_synced_items()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraSyncKeys.Add(Syncing("LOAN"));
            db.JiraSyncKeys.Add(Syncing("PAY"));
            db.JiraLabelCaches.Add(new JiraLabelCache { CacheKey = "LOAN", LabelsJson = "[]", UpdatedAt = OldWatermark });
            db.JiraLabelCaches.Add(new JiraLabelCache { CacheKey = "PAY", LabelsJson = "[]", UpdatedAt = OldWatermark });
        });
        await SeedFeaturesAsync("LOAN-1");

        await _service.RemoveKeyAsync("loan");

        Assert.Equal(new[] { "PAY" }, await RowKeysAsync());
        Assert.Equal(new[] { "PAY" }, await _db.ReadAsync(db => db.JiraLabelCaches.Select(c => c.CacheKey).ToListAsync()));
        Assert.True(await _db.ReadAsync(db => db.Features.AnyAsync(f => f.JiraId == "LOAN-1")));
    }

    [Fact]
    public async Task Removing_a_key_that_is_not_on_the_list_does_nothing()
    {
        await _db.SeedAsync(db => db.JiraSyncKeys.Add(Syncing("PAY")));

        await _service.RemoveKeyAsync("LOAN");

        Assert.Equal(new[] { "PAY" }, await RowKeysAsync());
    }

    [Fact]
    public async Task Synced_items_are_counted_per_type_for_the_key()
    {
        await _db.SeedAsync(db =>
        {
            db.Features.Add(new Feature { JiraId = "PAY-1", ProjectKey = "PAY", Summary = "One" });
            db.Features.Add(new Feature { JiraId = "PAY-2", ProjectKey = "PAY", Summary = "Two" });
            db.Features.Add(new Feature { JiraId = "LOAN-1", ProjectKey = "LOAN", Summary = "Loan" });
            db.BusinessOutcomes.Add(new BusinessOutcome { JiraId = "PAY-3", ProjectKey = "PAY", Summary = "Outcome" });
            db.PortfolioEpics.Add(new PortfolioEpic { JiraId = "PAY-4", ProjectKey = "PAY", Summary = "Epic" });
            db.StrategicObjectives.Add(new StrategicObjective { JiraId = "PAY-5", ProjectKey = "PAY", Summary = "Objective" });
            db.StrategicObjectives.Add(new StrategicObjective { JiraId = "LOAN-2", ProjectKey = "LOAN", Summary = "Loan objective" });
        });

        var counts = await _service.CountSyncedItemsAsync("pay");

        Assert.Equal(new JiraSyncKeyItemCounts(2, 1, 1, 1), counts);
        Assert.Equal(5, counts.Total);
    }

    [Fact]
    public async Task Resetting_a_watermark_clears_only_that_key()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraSyncKeys.Add(Syncing("LOAN"));
            db.JiraSyncKeys.Add(Syncing("PAY"));
        });

        await _service.ResetWatermarkAsync("pay");

        Assert.Null(await WatermarkAsync("PAY"));
        Assert.Equal(OldWatermark, await WatermarkAsync("LOAN"));
    }

    [Fact]
    public async Task Labels_sync_is_on_until_an_admin_switches_it_off()
    {
        Assert.True((await _service.GetSettingsAsync()).LabelsSyncEnabled);

        await _service.SetLabelsSyncEnabledAsync(false);

        Assert.False((await _service.GetSettingsAsync()).LabelsSyncEnabled);
    }

    [Fact]
    public async Task Switching_labels_sync_leaves_the_background_sync_settings_alone()
    {
        await _db.SeedAsync(db => db.JiraSyncSettings.Add(new JiraSyncSettings
        {
            Enabled = true,
            CycleCooldownMinutes = 30,
            JiraTimeZoneId = "UTC",
        }));

        await _service.SetLabelsSyncEnabledAsync(false);
        await _service.SetLabelsSyncEnabledAsync(true);

        var settings = await _db.ReadAsync(db => db.JiraSyncSettings.AsNoTracking().ToListAsync());
        var single = Assert.Single(settings);
        Assert.True(single.LabelsSyncEnabled);
        Assert.True(single.Enabled);
        Assert.Equal(30, single.CycleCooldownMinutes);
        Assert.Equal("UTC", single.JiraTimeZoneId);
    }
}
