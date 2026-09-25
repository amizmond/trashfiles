using System.Net;
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

        public HashSet<string> HiddenProjects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql)
        {
            Queries.Add(jql);
            var hidden = HiddenProjects.Where(p => jql.Contains($"\"{p}\"")).ToList();
            if (hidden.Count > 0)
            {
                var messages = string.Join(",", hidden.Select(p => $"\"The value '{p}' does not exist for the field 'project'.\""));
                throw new JiraSearchException(HttpStatusCode.BadRequest, $"{{\"errorMessages\":[{messages}],\"errors\":{{}}}}");
            }
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

    private static JiraSyncKey SyncKey(string key, bool active = false) =>
        new() { JiraKey = key, CreateNotExisted = active, UpdateExisted = active };

    private async Task<string> SearchedJqlAsync()
    {
        var result = await _service.GetSprintIssuesAsync("DOMAIN\\tester", "Sprint 1");
        Assert.Equal(SprintLookupStatus.Found, result.Status);
        return Assert.Single(_jira.Queries);
    }

    [Fact]
    public async Task An_art_key_without_a_sync_row_restricts_the_search_to_its_project()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments ART", "PAY")));

        var jql = await SearchedJqlAsync();

        Assert.Equal("sprint = \"Sprint 1\" AND project in (\"PAY\") ORDER BY assignee ASC, status ASC", jql);
    }

    [Fact]
    public async Task Every_key_of_every_art_is_searched_in_key_order()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY", "CARD"));
            db.CapitalProjects.Add(Art(2, "Core Banking ART", "CORE"));
            db.JiraSyncKeys.Add(SyncKey("PAY", active: true));
        });

        var jql = await SearchedJqlAsync();

        Assert.Contains("project in (\"CARD\", \"CORE\", \"PAY\")", jql);
    }

    [Fact]
    public async Task A_sync_key_no_art_lists_is_searched_even_with_sync_off()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY"));
            db.JiraSyncKeys.Add(SyncKey("ATM"));
        });

        var jql = await SearchedJqlAsync();

        Assert.Contains("project in (\"ATM\", \"PAY\")", jql);
    }

    [Fact]
    public async Task A_key_listed_by_two_arts_and_a_sync_row_is_searched_once()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY"));
            db.CapitalProjects.Add(Art(2, "Cards ART", "PAY", "CARD"));
            db.JiraSyncKeys.Add(SyncKey("PAY", active: true));
            db.JiraSyncKeys.Add(SyncKey("CARD"));
        });

        var jql = await SearchedJqlAsync();

        Assert.Contains("project in (\"CARD\", \"PAY\")", jql);
        Assert.Equal(2, jql.Split("\"PAY\"").Length);
    }

    [Fact]
    public async Task Keys_that_differ_only_in_case_are_searched_once()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "pay"));
            db.JiraSyncKeys.Add(SyncKey("PAY"));
        });

        var jql = await SearchedJqlAsync();

        Assert.Contains("project in (\"PAY\")", jql);
        Assert.DoesNotContain("\"pay\"", jql);
    }

    [Fact]
    public async Task Without_any_key_the_search_is_not_restricted_by_project()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments ART")));

        var jql = await SearchedJqlAsync();

        Assert.Equal("sprint = \"Sprint 1\" ORDER BY assignee ASC, status ASC", jql);
    }

    [Fact]
    public async Task Projects_the_user_cannot_see_are_dropped_and_the_search_is_retried()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY"));
            db.JiraSyncKeys.Add(SyncKey("PORT"));
            db.JiraSyncKeys.Add(SyncKey("SECRET"));
        });
        _jira.HiddenProjects.Add("PORT");
        _jira.HiddenProjects.Add("SECRET");

        var result = await _service.GetSprintIssuesAsync("DOMAIN\\tester", "Sprint 1");

        Assert.Equal(SprintLookupStatus.Found, result.Status);
        Assert.Equal(2, _jira.Queries.Count);
        Assert.Equal("sprint = \"Sprint 1\" AND project in (\"PAY\") ORDER BY assignee ASC, status ASC", _jira.Queries[1]);
    }

    [Fact]
    public async Task When_the_user_can_see_none_of_the_projects_the_sprint_is_not_found()
    {
        await _db.SeedAsync(db => db.JiraSyncKeys.Add(SyncKey("PORT")));
        _jira.HiddenProjects.Add("PORT");

        var result = await _service.GetSprintIssuesAsync("DOMAIN\\tester", "Sprint 1");

        Assert.Equal(SprintLookupStatus.NotFound, result.Status);
        Assert.Single(_jira.Queries);
    }
}
