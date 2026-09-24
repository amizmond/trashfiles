using System.Text.RegularExpressions;
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

    private sealed class FakeJira : IJiraIssueService
    {
        public List<string> Queries { get; } = [];

        public Func<Task>? DuringSearch { get; set; }

        public List<JiraIssueResponse> Issues { get; } = [];

        public async Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql)
        {
            Queries.Add(jql);
            if (DuringSearch is not null)
            {
                await DuringSearch();
            }

            return Issues.Where(i => jql.Contains($"\"{i.IssueType}\"")).ToList();
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

    private sealed class RecordingWriter : IJiraSyncWriter
    {
        private readonly List<(int ArtId, HierarchyEntityType Type, string Key)> _written = [];

        public IReadOnlyList<string> KeysWrittenBy(int artId, HierarchyEntityType type = HierarchyEntityType.Feature) =>
            _written.Where(w => w.ArtId == artId && w.Type == type).Select(w => w.Key).Order().ToList();

        public Task<JiraSyncWriteResult> WriteProjectAsync(
            int capitalProjectId,
            string projectKey,
            IReadOnlyDictionary<HierarchyEntityType, List<JiraIssueResponse>> issuesByType,
            bool background,
            CancellationToken cancellationToken)
        {
            foreach (var (type, issues) in issuesByType)
            {
                _written.AddRange(issues.Select(i => (capitalProjectId, type, i.Key)));
            }

            return Task.FromResult(new JiraSyncWriteResult(0, 0, []));
        }
    }

    private static JiraToken ServiceToken() => new()
    {
        UserName = JiraSyncSettings.DefaultServiceAccountUserName,
        AccessToken = "t",
        AccessTokenSecret = "s",
        Created = OldWatermark
    };

    private Task SeedAsync(params string[] keys) => _db.SeedAsync(db =>
    {
        db.JiraTokens.Add(ServiceToken());
        db.CapitalProjects.Add(new Art
        {
            Id = 1,
            Name = "Payments",
            JiraKeys = keys.Select(k => new ArtJiraKey { JiraKey = k }).ToList()
        });
        db.JiraSyncProjectSettings.Add(new JiraSyncProjectSettings
        {
            CapitalProjectId = 1,
            UpdateExisted = true,
            IssueTypesCsv = "Feature",
            LastSyncedWatermarkUtc = OldWatermark
        });
    });

    private static Art Art(int id, string name, params ArtJiraKey[] keys) =>
        new() { Id = id, Name = name, JiraKeys = keys.ToList() };

    private static ArtJiraKey Key(string key, string? components = null, string? labels = null) =>
        new() { JiraKey = key, Components = components, Labels = labels };

    private static JiraSyncProjectSettings Syncing(int artId, string issueTypes = "Feature") =>
        new()
        {
            CapitalProjectId = artId,
            CreateNotExisted = true,
            UpdateExisted = true,
            IssueTypesCsv = issueTypes
        };

    private Task SeedSharedKeyAsync(bool cardsSyncs = true, string cardsIssueTypes = "Feature") => _db.SeedAsync(db =>
    {
        db.JiraTokens.Add(ServiceToken());
        db.CapitalProjects.Add(Art(1, "Payments", Key("PAY")));
        db.CapitalProjects.Add(Art(2, "Cards", Key("PAY", components: "Cards")));
        db.JiraSyncProjectSettings.Add(Syncing(1));
        if (cardsSyncs)
        {
            db.JiraSyncProjectSettings.Add(Syncing(2, cardsIssueTypes));
        }
    });

    private Task SeedFeaturesAsync(params string[] jiraIds) => _db.SeedAsync(db =>
    {
        foreach (var jiraId in jiraIds)
        {
            db.Features.Add(new Feature { JiraId = jiraId, ProjectKey = "PAY", Summary = jiraId });
        }
    });

    private void InJira(string key, string type = "Feature", string[]? components = null, string[]? labels = null) =>
        _jira.Issues.Add(new JiraIssueResponse
        {
            Key = key,
            Summary = key,
            IssueType = type,
            Components = components?.ToList(),
            Labels = labels?.ToList()
        });

    private Task<DateTime?> WatermarkAsync() =>
        _db.ReadAsync(db => db.JiraSyncProjectSettings.Select(s => s.LastSyncedWatermarkUtc).SingleAsync());

    private Task<DateTime?> WatermarkOfAsync(int artId) =>
        _db.ReadAsync(db => db.JiraSyncProjectSettings
            .Where(s => s.CapitalProjectId == artId)
            .Select(s => s.LastSyncedWatermarkUtc)
            .SingleAsync());

    private Task EditKeyAsync(int artId, string key, string? components = null, string? labels = null) =>
        _db.SeedAsync(db =>
        {
            var row = db.CapitalProjectJiraKeys.Single(k => k.CapitalProjectId == artId && k.JiraKey == key);
            row.Components = components;
            row.Labels = labels;
        });

    [Fact]
    public async Task A_single_key_art_is_fetched_with_its_project()
    {
        await SeedAsync("PAY");

        await _service.RunSyncAsync("test");

        Assert.StartsWith("project = \"PAY\" AND issuetype in (\"Feature\")", Assert.Single(_jira.Queries));
        Assert.True(await WatermarkAsync() > OldWatermark);
    }

    [Fact]
    public async Task A_multi_key_art_is_fetched_across_all_its_projects()
    {
        await SeedAsync("PAY", "CARD");

        await _service.RunSyncAsync("test");

        Assert.StartsWith("project in (\"PAY\", \"CARD\") AND", Assert.Single(_jira.Queries));
    }

    [Fact]
    public async Task A_key_added_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        await SeedAsync("PAY");
        _jira.DuringSearch = () => _db.SeedAsync(db =>
            db.CapitalProjectJiraKeys.Add(new ArtJiraKey { CapitalProjectId = 1, JiraKey = "CARD" }));

        await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync());
    }

    [Fact]
    public async Task An_art_that_owns_its_key_outright_writes_every_fetched_feature()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraTokens.Add(ServiceToken());
            db.CapitalProjects.Add(Art(1, "Payments", Key("PAY")));
            db.JiraSyncProjectSettings.Add(Syncing(1));
        });
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1", components: ["Cards"]);
        InJira("PAY-2", labels: ["web"]);

        await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1", "PAY-2" }, _writer.KeysWrittenBy(1));
    }

    [Fact]
    public async Task The_components_and_labels_of_a_shared_key_do_not_narrow_the_jira_fetch()
    {
        await SeedSharedKeyAsync();

        await _service.RunSyncAsync("test");

        Assert.Equal(2, _jira.Queries.Count);
        Assert.All(_jira.Queries, q => Assert.StartsWith("project = \"PAY\" AND issuetype in (\"Feature\") ORDER BY", q));
    }

    [Fact]
    public async Task A_feature_on_a_shared_key_is_created_only_by_the_run_of_its_art_and_updated_by_every_run()
    {
        await SeedSharedKeyAsync();
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1", components: ["Cards"]);
        InJira("PAY-2", components: ["Loans"]);
        InJira("PAY-3", components: ["cards"]);

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1", "PAY-2" }, _writer.KeysWrittenBy(1));
        Assert.Equal(new[] { "PAY-1", "PAY-3" }, _writer.KeysWrittenBy(2));
        Assert.Equal(2, Regex.Matches(history.Message!, Regex.Escape("1 not created (left to other ARTs' syncs)")).Count);
    }

    [Fact]
    public async Task A_feature_of_another_art_is_refreshed_even_when_that_art_does_not_update_features()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraTokens.Add(ServiceToken());
            db.CapitalProjects.Add(Art(1, "Payments", Key("PAY")));
            db.CapitalProjects.Add(Art(2, "Cards", Key("PAY", components: "Cards")));
            db.JiraSyncProjectSettings.Add(Syncing(1));
            var cards = Syncing(2);
            cards.UpdateExisted = false;
            db.JiraSyncProjectSettings.Add(cards);
        });
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1", components: ["Cards"]);
        InJira("PAY-2", components: ["Cards"]);

        await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1" }, _writer.KeysWrittenBy(1));
        Assert.Equal(new[] { "PAY-2" }, _writer.KeysWrittenBy(2));
    }

    [Fact]
    public async Task A_filter_edit_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        await SeedAsync("PAY");
        _jira.DuringSearch = () => EditKeyAsync(1, "PAY", components: "Cards");

        var history = await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync());
        Assert.Contains("PAY: keys, components, labels or watermark changed during the sync", history.Message);
    }

    [Fact]
    public async Task Another_arts_edit_on_a_shared_key_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        await SeedSharedKeyAsync(cardsSyncs: false);
        _jira.DuringSearch = () => EditKeyAsync(2, "PAY", components: "Cards, Debit");

        await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync());
    }

    [Fact]
    public async Task Another_art_leaving_a_shared_key_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        await SeedSharedKeyAsync(cardsSyncs: false);
        _jira.DuringSearch = () => _db.SeedAsync(db =>
            db.CapitalProjectJiraKeys.RemoveRange(db.CapitalProjectJiraKeys.Where(k => k.CapitalProjectId == 2)));

        await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync());
    }

    [Fact]
    public async Task A_watermark_reset_while_the_sync_runs_makes_the_next_sync_start_over()
    {
        await SeedAsync("PAY");
        _jira.DuringSearch = () => _service.ResetWatermarkAsync(1);

        await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync());
    }

    [Fact]
    public async Task A_key_edit_while_the_sync_runs_clears_a_watermark_another_run_wrote_meanwhile()
    {
        await SeedSharedKeyAsync(cardsSyncs: false);
        _jira.DuringSearch = async () =>
        {
            await EditKeyAsync(2, "PAY", components: "Cards, Debit");
            await _db.SeedAsync(db =>
                db.JiraSyncProjectSettings.Single(s => s.CapitalProjectId == 1).LastSyncedWatermarkUtc = OldWatermark);
        };

        await _service.RunSyncAsync("test");

        Assert.Null(await WatermarkAsync());
    }

    [Fact]
    public async Task A_sync_whose_key_rows_stay_the_same_advances_every_watermark()
    {
        await SeedSharedKeyAsync();
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(3, "Loans", Key("LOAN"))));
        _jira.DuringSearch = () => EditKeyAsync(3, "LOAN", labels: "retail");

        var history = await _service.RunSyncAsync("test");

        Assert.NotNull(await WatermarkOfAsync(1));
        Assert.NotNull(await WatermarkOfAsync(2));
        Assert.DoesNotContain("changed during the sync", history.Message);
    }

    [Fact]
    public async Task An_art_deleted_while_the_sync_runs_does_not_fail_the_run()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraTokens.Add(ServiceToken());
            db.CapitalProjects.Add(Art(1, "Payments", Key("PAY")));
            db.CapitalProjects.Add(Art(2, "Loans", Key("LOAN")));
            db.JiraSyncProjectSettings.Add(Syncing(1));
            db.JiraSyncProjectSettings.Add(Syncing(2));
        });
        var deleted = false;
        _jira.DuringSearch = async () =>
        {
            if (deleted)
            {
                return;
            }

            deleted = true;
            await _db.SeedAsync(db =>
            {
                db.JiraSyncProjectSettings.RemoveRange(db.JiraSyncProjectSettings.Where(s => s.CapitalProjectId == 2));
                db.CapitalProjectJiraKeys.RemoveRange(db.CapitalProjectJiraKeys.Where(k => k.CapitalProjectId == 2));
                db.CapitalProjects.RemoveRange(db.CapitalProjects.Where(a => a.Id == 2));
            });
        };

        var history = await _service.RunSyncAsync("test");

        Assert.Equal("Success", history.Status);
        Assert.NotNull(await WatermarkOfAsync(1));
        Assert.Contains("LOAN: the ART was deleted during the sync", history.Message);
    }

    [Fact]
    public async Task A_feature_of_an_art_without_sync_settings_is_updated_but_never_created()
    {
        await SeedSharedKeyAsync(cardsSyncs: false);
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1", components: ["Cards"]);
        InJira("PAY-2", components: ["Cards"]);
        InJira("PAY-3");

        var history = await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1", "PAY-3" }, _writer.KeysWrittenBy(1));
        Assert.Contains("1 not created (not this ART's)", history.Message);
    }

    [Fact]
    public async Task A_feature_of_an_art_whose_sync_skips_features_is_updated_but_never_created()
    {
        await SeedSharedKeyAsync(cardsIssueTypes: "Business Outcome");
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1", components: ["Cards"]);
        InJira("PAY-2", components: ["Cards"]);

        await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1" }, _writer.KeysWrittenBy(1));
        Assert.Empty(_writer.KeysWrittenBy(2));
    }

    [Fact]
    public async Task A_feature_that_no_art_accepts_is_updated_but_never_created()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraTokens.Add(ServiceToken());
            db.CapitalProjects.Add(Art(1, "Web", Key("PAY", labels: "web")));
            db.CapitalProjects.Add(Art(2, "Mobile", Key("PAY", labels: "mobile")));
            db.JiraSyncProjectSettings.Add(Syncing(1));
            db.JiraSyncProjectSettings.Add(Syncing(2));
        });
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1");
        InJira("PAY-2", labels: ["desktop"]);
        InJira("PAY-3", labels: ["web"]);

        await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1", "PAY-3" }, _writer.KeysWrittenBy(1));
        Assert.Equal(new[] { "PAY-1" }, _writer.KeysWrittenBy(2));
    }

    [Fact]
    public async Task An_ambiguous_feature_is_updated_but_never_created()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraTokens.Add(ServiceToken());
            db.CapitalProjects.Add(Art(1, "Web", Key("PAY", labels: "web")));
            db.CapitalProjects.Add(Art(2, "Cards", Key("PAY", components: "Cards")));
            db.JiraSyncProjectSettings.Add(Syncing(1));
            db.JiraSyncProjectSettings.Add(Syncing(2));
        });
        await SeedFeaturesAsync("PAY-1");
        InJira("PAY-1", components: ["Cards"], labels: ["web"]);
        InJira("PAY-2", components: ["Cards"], labels: ["web"]);

        await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-1" }, _writer.KeysWrittenBy(1));
        Assert.Equal(new[] { "PAY-1" }, _writer.KeysWrittenBy(2));
    }

    [Fact]
    public async Task A_feature_on_an_unshared_key_of_a_sharing_art_stays_with_that_art()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraTokens.Add(ServiceToken());
            db.CapitalProjects.Add(Art(1, "Payments", Key("PAY"), Key("LOAN")));
            db.CapitalProjects.Add(Art(2, "Cards", Key("PAY", components: "Cards")));
            db.JiraSyncProjectSettings.Add(Syncing(1));
            db.JiraSyncProjectSettings.Add(Syncing(2));
        });
        InJira("LOAN-1", components: ["Cards"]);

        await _service.RunSyncAsync("test");

        Assert.Contains("LOAN-1", _writer.KeysWrittenBy(1));
        Assert.DoesNotContain("LOAN-1", _writer.KeysWrittenBy(2));
    }

    [Fact]
    public async Task Parents_on_a_shared_key_are_synced_by_every_art_as_before()
    {
        await _db.SeedAsync(db =>
        {
            db.JiraTokens.Add(ServiceToken());
            db.CapitalProjects.Add(Art(1, "Payments", Key("PAY")));
            db.CapitalProjects.Add(Art(2, "Cards", Key("PAY", components: "Cards")));
            db.JiraSyncProjectSettings.Add(Syncing(1, "Business Outcome"));
            db.JiraSyncProjectSettings.Add(Syncing(2, "Business Outcome"));
        });
        InJira("PAY-10", type: "Business Outcome", components: ["Cards"]);

        await _service.RunSyncAsync("test");

        Assert.Equal(new[] { "PAY-10" }, _writer.KeysWrittenBy(1, HierarchyEntityType.BusinessOutcome));
        Assert.Equal(new[] { "PAY-10" }, _writer.KeysWrittenBy(2, HierarchyEntityType.BusinessOutcome));
    }
}
