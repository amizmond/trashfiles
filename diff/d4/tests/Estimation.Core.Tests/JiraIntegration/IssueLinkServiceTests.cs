using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class IssueLinkServiceTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly FakeJira _jira = new();
    private readonly IssueLinkService _service;

    public IssueLinkServiceTests()
    {
        _service = new IssueLinkService(_db, _jira);
    }

    private sealed class FakeJira : IJiraIssueService
    {
        public List<string> Calls { get; } = [];

        public Dictionary<string, Exception> Failures { get; } = [];

        public Task CreateIssueLinkAsync(string userName, string linkTypeName, string inwardIssueKey, string outwardIssueKey) =>
            RecordAsync($"link {inwardIssueKey} {linkTypeName} {outwardIssueKey}");

        public Task DeleteIssueLinkAsync(string userName, string linkId) => RecordAsync($"unlink {linkId}");

        private Task RecordAsync(string call)
        {
            Calls.Add(call);
            return Failures.TryGetValue(call, out var failure) ? Task.FromException(failure) : Task.CompletedTask;
        }

        public Task<string> CreateIssueAsync(string userName, JiraCreateIssueRequest request) => throw new NotSupportedException();
        public Task UpdateIssueAsync(string userName, string issueKey, JiraUpdateIssueRequest request) => throw new NotSupportedException();
        public Task UpdateIssueWithStatusAsync(string userName, string issueKey, JiraUpdateIssueRequest request, string? targetStatusName) => throw new NotSupportedException();
        public Task<JiraIssueResponse?> GetIssueAsync(string userName, string issueKey) => throw new NotSupportedException();
        public Task<List<JiraIssueResponse>> SearchIssuesAsync(string userName, string jql, bool includeLinks = false) => throw new NotSupportedException();
        public Task<JiraSearchPage> SearchIssuesPageAsync(string userName, string jql, int maxResults) => throw new NotSupportedException();
        public Task<List<JiraIssueResponse>> GetIssuesByKeysAsync(string userName, List<string> issueKeys) => throw new NotSupportedException();
        public Task<List<JiraTransition>> GetTransitionsAsync(string userName, string issueKey) => throw new NotSupportedException();
        public Task<bool> TransitionToStatusAsync(string userName, string issueKey, string targetStatusName) => throw new NotSupportedException();
    }

    private static IssueLink Blocks(string id, string from, string to) => new()
    {
        JiraLinkId = id,
        TypeName = "Blocks",
        OutwardLabel = "blocks",
        InwardLabel = "is blocked by",
        FromKey = from,
        ToKey = to,
    };

    private static Feature Feature(string jiraId, string? name, string summary, string? status = null) =>
        new() { JiraId = jiraId, Name = name, Summary = summary, Status = status };

    private Task SeedAsync(params object[] rows) => _db.SeedAsync(db => db.AddRange(rows));

    private async Task<IReadOnlyList<LinkedIssue>> LinksOfAsync(string key) =>
        (await _service.GetForIssuesAsync([key])).TryGetValue(key, out var links) ? links : [];

    private static string Describe(LinkedIssue link) => $"{link.Relation} {link.Key}";

    [Fact]
    public async Task The_issue_a_link_starts_from_reads_the_outward_phrase()
    {
        await SeedAsync(Blocks("501", "PAY-1", "PAY-2"));

        var link = Assert.Single(await LinksOfAsync("PAY-1"));

        Assert.Equal("blocks PAY-2", Describe(link));
        Assert.True(link.IsOutward);
    }

    [Fact]
    public async Task The_issue_a_link_points_to_reads_the_inward_phrase()
    {
        await SeedAsync(Blocks("501", "PAY-1", "PAY-2"));

        var link = Assert.Single(await LinksOfAsync("PAY-2"));

        Assert.Equal("is blocked by PAY-1", Describe(link));
        Assert.False(link.IsOutward);
    }

    [Fact]
    public async Task One_row_serves_both_issues_of_a_board()
    {
        await SeedAsync(Blocks("501", "PAY-1", "PAY-2"));

        var byKey = await _service.GetForIssuesAsync(["PAY-1", "PAY-2", "PAY-3"]);

        Assert.Equal("blocks PAY-2", Describe(Assert.Single(byKey["PAY-1"])));
        Assert.Equal("is blocked by PAY-1", Describe(Assert.Single(byKey["PAY-2"])));
        Assert.False(byKey.ContainsKey("PAY-3"));
    }

    [Fact]
    public async Task A_link_without_a_phrase_falls_back_to_its_type_name()
    {
        var row = Blocks("501", "PAY-1", "PAY-2");
        row.OutwardLabel = null;
        await SeedAsync(row);

        Assert.Equal("Blocks PAY-2", Describe(Assert.Single(await LinksOfAsync("PAY-1"))));
    }

    [Fact]
    public async Task A_linked_feature_of_the_tool_brings_its_name_and_status()
    {
        await SeedAsync(
            Blocks("501", "PAY-1", "PAY-2"),
            Feature("PAY-2", "Card limits", "Raise the card limits", "In Progress"));

        var link = Assert.Single(await LinksOfAsync("PAY-1"));

        Assert.True(link.InTool);
        Assert.Equal("Card limits", link.Name);
        Assert.Equal("In Progress", link.Status);
    }

    [Fact]
    public async Task A_linked_feature_without_a_name_shows_its_summary()
    {
        await SeedAsync(Blocks("501", "PAY-1", "PAY-2"), Feature("PAY-2", null, "Raise the card limits"));

        Assert.Equal("Raise the card limits", Assert.Single(await LinksOfAsync("PAY-1")).Name);
    }

    [Fact]
    public async Task A_linked_issue_outside_the_tool_is_marked_as_such()
    {
        await SeedAsync(Blocks("501", "PAY-1", "OPS-9"));

        var link = Assert.Single(await LinksOfAsync("PAY-1"));

        Assert.False(link.InTool);
        Assert.Null(link.Name);
    }

    [Fact]
    public async Task Links_are_listed_by_phrase_then_by_issue_number()
    {
        await SeedAsync(
            Blocks("501", "PAY-1", "PAY-10"),
            Blocks("502", "PAY-1", "PAY-2"),
            Blocks("503", "PAY-3", "PAY-1"));

        Assert.Equal(
            new[] { "blocks PAY-2", "blocks PAY-10", "is blocked by PAY-3" },
            (await LinksOfAsync("PAY-1")).Select(Describe));
    }

    [Fact]
    public async Task Loading_an_issue_fills_its_linked_issues()
    {
        await SeedAsync(Blocks("501", "PAY-1", "PAY-2"));
        var feature = Feature("PAY-1", "First", "First");

        await _service.LoadAsync(feature);

        Assert.Equal("blocks PAY-2", Describe(Assert.Single(feature.LinkedIssues!)));
    }

    [Fact]
    public async Task Loading_an_issue_without_a_jira_key_gives_an_empty_list()
    {
        var feature = new Feature { Summary = "Local only" };

        await _service.LoadAsync(feature);

        Assert.Empty(feature.LinkedIssues!);
    }

    [Fact]
    public async Task Links_read_from_jira_keep_jiras_name_for_issues_outside_the_tool()
    {
        var links = await _service.FromJiraAsync(
        [
            new JiraIssueLink
            {
                Id = "501", TypeName = "Blocks", OutwardLabel = "blocks", InwardLabel = "is blocked by",
                IsOutward = false, OtherKey = "OPS-9", OtherSummary = "Open the firewall", OtherStatus = "Open",
            },
        ]);

        var link = Assert.Single(links);
        Assert.Equal("is blocked by OPS-9", Describe(link));
        Assert.False(link.InTool);
        Assert.Equal("Open the firewall", link.Name);
        Assert.Equal("Open", link.Status);
    }

    [Fact]
    public async Task A_link_jira_returns_twice_is_shown_once()
    {
        var links = await _service.FromJiraAsync(
        [
            new JiraIssueLink { Id = "501", TypeName = "Relates", OutwardLabel = "relates to", IsOutward = true, OtherKey = "PAY-1" },
            new JiraIssueLink { Id = "501", TypeName = "Relates", InwardLabel = "relates to", IsOutward = false, OtherKey = "PAY-1" },
        ]);

        Assert.Equal("relates to PAY-1", Describe(Assert.Single(links)));
    }

    [Fact]
    public async Task Links_read_from_jira_show_the_tools_name_for_its_own_features()
    {
        await SeedAsync(Feature("PAY-2", "Card limits", "Raise the card limits", "In Progress"));

        var links = await _service.FromJiraAsync(
        [
            new JiraIssueLink
            {
                Id = "501", TypeName = "Blocks", OutwardLabel = "blocks", InwardLabel = "is blocked by",
                IsOutward = true, OtherKey = "PAY-2", OtherSummary = "Jira summary", OtherStatus = "Backlog",
            },
        ]);

        var link = Assert.Single(links);
        Assert.True(link.InTool);
        Assert.Equal("Card limits", link.Name);
        Assert.Equal("In Progress", link.Status);
    }

    private async Task<List<string>> SearchAsync(string? term, string? exclude = null, int take = 20) =>
        (await _service.SearchTargetsAsync(term, exclude, take)).Select(t => t.Key).ToList();

    [Fact]
    public async Task The_picker_finds_features_by_the_start_of_their_key()
    {
        await SeedAsync(
            Feature("PAY-1", "Card limits", "Raise the card limits"),
            Feature("PAY-2", "Fees", "Fee table"),
            Feature("LOAN-1", "Rates", "Rate table"));

        Assert.Equal(new[] { "PAY-1", "PAY-2" }, await SearchAsync("PAY"));
    }

    [Fact]
    public async Task The_picker_finds_features_by_a_part_of_their_name_or_summary()
    {
        await SeedAsync(
            Feature("PAY-1", "Card limits", "Raise the limits"),
            Feature("PAY-2", null, "New card design"),
            Feature("LOAN-1", "Rates", "Rate table"));

        Assert.Equal(new[] { "PAY-1", "PAY-2" }, await SearchAsync("ard"));
    }

    [Fact]
    public async Task Key_matches_come_before_text_matches_and_nothing_is_listed_twice()
    {
        await SeedAsync(
            Feature("AB-1", "ZZ", "ZZ"),
            Feature("PAY-9", "Works with AB", "x"),
            Feature("AB-2", "AB migration", "x"));

        Assert.Equal(new[] { "AB-1", "AB-2", "PAY-9" }, await SearchAsync("AB"));
    }

    [Fact]
    public async Task The_picker_leaves_out_the_feature_itself_and_features_without_a_jira_key()
    {
        await _db.SeedAsync(db => db.AddRange(
            Feature("PAY-1", "Card limits", "x"),
            Feature("PAY-2", "Card fees", "x"),
            new Feature { Name = "Card local", Summary = "x" }));

        Assert.Equal(new[] { "PAY-2" }, await SearchAsync("Card", exclude: "PAY-1"));
    }

    [Fact]
    public async Task The_picker_returns_no_more_than_it_is_asked_for()
    {
        await SeedAsync(Enumerable.Range(1, 9).Select(n => (object)Feature($"PAY-{n}", "Card", "x")).ToArray());

        Assert.Equal(4, (await SearchAsync("PAY", take: 4)).Count);
        Assert.Equal(4, (await SearchAsync("Card", take: 4)).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" P ")]
    public async Task The_picker_needs_at_least_two_characters(string? term)
    {
        await SeedAsync(Feature("PAY-1", "Card limits", "x"));

        Assert.Empty(await SearchAsync(term));
    }

    [Fact]
    public async Task A_picked_feature_carries_its_display_name_and_status()
    {
        await SeedAsync(Feature("PAY-2", null, "Fee table", "To Do"));

        var target = Assert.Single(await _service.SearchTargetsAsync("PAY", null));

        Assert.Equal("Fee table", target.Name);
        Assert.Equal("To Do", target.Status);
    }

    private static PendingLink Add(string key, bool outward = true, string type = "Blocks") =>
        new(type, outward, outward ? "blocks" : "is blocked by", key);

    private static JiraRequestException Refused(System.Net.HttpStatusCode status, string body = "") =>
        new("POST", status, body);

    [Fact]
    public async Task An_outward_link_is_created_from_this_issue_to_the_other()
    {
        await _service.PushAsync("anna", "PAY-1", new LinkChanges([], [Add("PAY-2")]));

        Assert.Equal(new[] { "link PAY-1 Blocks PAY-2" }, _jira.Calls);
    }

    [Fact]
    public async Task An_inward_link_is_created_from_the_other_issue_to_this_one()
    {
        await _service.PushAsync("anna", "PAY-1", new LinkChanges([], [Add("PAY-2", outward: false)]));

        Assert.Equal(new[] { "link PAY-2 Blocks PAY-1" }, _jira.Calls);
    }

    [Fact]
    public async Task Removals_are_sent_before_additions()
    {
        var result = await _service.PushAsync("anna", "PAY-1", new LinkChanges(["501", "502"], [Add("PAY-2")]));

        Assert.Equal(new[] { "unlink 501", "unlink 502", "link PAY-1 Blocks PAY-2" }, _jira.Calls);
        Assert.Equal(new[] { "501", "502" }, result.Removed);
        Assert.Single(result.Added);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task A_refused_change_is_reported_and_the_rest_is_still_sent()
    {
        _jira.Failures["link PAY-1 Blocks PAY-2"] = Refused(
            System.Net.HttpStatusCode.Unauthorized, "{\"errorMessages\":[\"No Link Issue Permission for issue 'PAY-1'\"],\"errors\":{}}");

        var result = await _service.PushAsync("anna", "PAY-1", new LinkChanges([], [Add("PAY-2"), Add("PAY-3")]));

        Assert.Equal(2, _jira.Calls.Count);
        Assert.Equal("PAY-3", Assert.Single(result.Added).Key);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("PAY-2", failure.Add!.Key);
        Assert.Equal("No Link Issue Permission for issue 'PAY-1'", failure.Message);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData(System.Net.HttpStatusCode.RequestTimeout)]
    [InlineData((System.Net.HttpStatusCode)429)]
    public async Task When_jira_stops_answering_the_remaining_changes_are_not_sent(System.Net.HttpStatusCode status)
    {
        _jira.Failures["unlink 501"] = Refused(status);

        var result = await _service.PushAsync("anna", "PAY-1", new LinkChanges(["501", "502"], [Add("PAY-2")]));

        Assert.Equal(new[] { "unlink 501" }, _jira.Calls);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Added);
        Assert.Equal(3, result.Failures.Count);
        Assert.All(result.Failures.Skip(1), f => Assert.Equal(IssueLinkService.NotSentJiraNotResponding, f.Message));
    }

    [Fact]
    public async Task A_refused_removal_is_reported_and_the_rest_is_still_sent()
    {
        _jira.Failures["unlink 501"] = Refused(System.Net.HttpStatusCode.NotFound);

        var result = await _service.PushAsync("anna", "PAY-1", new LinkChanges(["501", "502"], [Add("PAY-2")]));

        Assert.Equal(new[] { "unlink 501", "unlink 502", "link PAY-1 Blocks PAY-2" }, _jira.Calls);
        Assert.Equal(new[] { "502" }, result.Removed);
        Assert.Single(result.Added);
        Assert.Equal("501", Assert.Single(result.Failures).LinkId);
    }

    [Fact]
    public async Task A_timed_out_call_stops_the_remaining_changes()
    {
        _jira.Failures["unlink 501"] = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout");

        var result = await _service.PushAsync("anna", "PAY-1", new LinkChanges(["501"], [Add("PAY-2")]));

        Assert.Equal(new[] { "unlink 501" }, _jira.Calls);
        Assert.Equal(
            new[] { IssueLinkService.JiraNotResponding, IssueLinkService.NotSentJiraNotResponding },
            result.Failures.Select(f => f.Message));
    }

    [Fact]
    public async Task An_unexpected_error_stops_the_remaining_changes_and_names_itself()
    {
        _jira.Failures["unlink 501"] = new InvalidOperationException("Not authenticated to Jira. Please log in first.");

        var result = await _service.PushAsync("anna", "PAY-1", new LinkChanges(["501"], [Add("PAY-2")]));

        Assert.Equal(new[] { "unlink 501" }, _jira.Calls);
        Assert.Equal(
            new[] { "Not authenticated to Jira. Please log in first.", "Not sent: Not authenticated to Jira. Please log in first." },
            result.Failures.Select(f => f.Message));
    }

    [Fact]
    public async Task When_jira_stops_answering_during_the_additions_the_later_ones_are_not_sent()
    {
        _jira.Failures["link PAY-1 Blocks PAY-2"] = Refused(System.Net.HttpStatusCode.BadGateway);

        var result = await _service.PushAsync("anna", "PAY-1", new LinkChanges(["501"], [Add("PAY-2"), Add("PAY-3")]));

        Assert.Equal(new[] { "unlink 501", "link PAY-1 Blocks PAY-2" }, _jira.Calls);
        Assert.Equal(new[] { "501" }, result.Removed);
        Assert.Equal(new[] { "PAY-2", "PAY-3" }, result.Failures.Select(f => f.Add!.Key));
    }

    [Fact]
    public async Task A_connection_failure_also_stops_the_remaining_changes()
    {
        _jira.Failures["unlink 501"] = new HttpRequestException("No such host");

        var result = await _service.PushAsync("anna", "PAY-1", new LinkChanges(["501"], [Add("PAY-2")]));

        Assert.Equal(new[] { "unlink 501" }, _jira.Calls);
        Assert.Equal(
            new[] { IssueLinkService.JiraNotResponding, IssueLinkService.NotSentJiraNotResponding },
            result.Failures.Select(f => f.Message));
    }

    [Fact]
    public async Task A_removed_link_can_be_forgotten_by_its_id()
    {
        await SeedAsync(Blocks("501", "PAY-1", "PAY-2"), Blocks("502", "PAY-1", "PAY-3"));

        Assert.True(await _service.ForgetAsync(["501", "999"]));

        Assert.Equal("blocks PAY-3", Describe(Assert.Single(await LinksOfAsync("PAY-1"))));
    }
}
