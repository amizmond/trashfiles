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
    private readonly IssueLinkService _service;

    public IssueLinkServiceTests()
    {
        _service = new IssueLinkService(_db);
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
}
