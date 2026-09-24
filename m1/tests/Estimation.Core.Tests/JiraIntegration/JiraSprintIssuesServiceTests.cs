using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraSprintIssuesServiceTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly RecordingJiraIssueService _jira = new();
    private readonly JiraSprintIssuesService _service;

    public JiraSprintIssuesServiceTests()
    {
        _service = new JiraSprintIssuesService(_db, _jira);
    }

    private sealed class RecordingJiraIssueService : IJiraIssueService
    {
        public List<string> Queries { get; } = [];

        public Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql)
        {
            Queries.Add(jql);
            return Task.FromResult(new List<JiraIssueResponse>());
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

    private static Art Art(int id, string name, params string[] keys) =>
        new()
        {
            Id = id,
            Name = name,
            JiraKeys = keys.Select(k => new ArtJiraKey { JiraKey = k }).ToList()
        };

    private static JiraSyncProjectSettings SyncSettings(int artId) =>
        new() { CapitalProjectId = artId, CreateNotExisted = true };

    private async Task<string> SearchedJqlAsync()
    {
        var result = await _service.GetSprintIssuesAsync("DOMAIN\\tester", "Sprint 1");
        Assert.Equal(SprintLookupStatus.Found, result.Status);
        return Assert.Single(_jira.Queries);
    }

    [Fact]
    public async Task A_single_key_art_restricts_the_search_to_its_project()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY"));
            db.JiraSyncProjectSettings.Add(SyncSettings(1));
        });

        var jql = await SearchedJqlAsync();

        Assert.Equal("sprint = \"Sprint 1\" AND project in (\"PAY\") ORDER BY assignee ASC, status ASC", jql);
    }

    [Fact]
    public async Task Every_key_of_a_synced_art_is_searched()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY", "CARD"));
            db.CapitalProjects.Add(Art(2, "Core Banking ART", "CORE"));
            db.JiraSyncProjectSettings.Add(SyncSettings(1));
        });

        var jql = await SearchedJqlAsync();

        Assert.Contains("\"PAY\"", jql);
        Assert.Contains("\"CARD\"", jql);
        Assert.DoesNotContain("\"CORE\"", jql);
    }

    [Fact]
    public async Task A_key_listed_by_two_synced_arts_is_searched_once()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY"));
            db.CapitalProjects.Add(Art(2, "Cards ART", "PAY", "CARD"));
            db.JiraSyncProjectSettings.Add(SyncSettings(1));
            db.JiraSyncProjectSettings.Add(SyncSettings(2));
        });

        var jql = await SearchedJqlAsync();

        Assert.Equal(2, jql.Split("\"PAY\"").Length);
        Assert.Contains("\"CARD\"", jql);
    }

    [Fact]
    public async Task Without_synced_arts_the_search_is_not_restricted_by_project()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments ART", "PAY")));

        var jql = await SearchedJqlAsync();

        Assert.DoesNotContain("project", jql);
    }
}
