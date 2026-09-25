using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Client.JiraSync;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraSyncWriterTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly JiraSyncWriter _writer;

    public JiraSyncWriterTests()
    {
        var services = new ServiceCollection()
            .AddTransient<FeatureTeamConverter>()
            .AddTransient<FeaturePiConverter>()
            .AddTransient<FeatureParentConverter>()
            .AddTransient<BusinessOutcomeParentConverter>()
            .AddTransient<PortfolioEpicParentConverter>()
            .AddTransient<StrategicObjectiveParentConverter>()
            .BuildServiceProvider();
        _writer = new JiraSyncWriter(_db, services);
    }

    private static JiraIssueResponse Issue(string key) => new() { Key = key, Summary = $"Summary of {key}" };

    private Task<JiraSyncWriteResult> WriteAsync(HierarchyEntityType type, params JiraIssueResponse[] issues) =>
        _writer.WriteAsync(type, issues, background: true, CancellationToken.None);

    private static JiraIssue NewItem(HierarchyEntityType type, string jiraId, string? projectKey)
    {
        JiraIssue item = type switch
        {
            HierarchyEntityType.Feature => new Feature(),
            HierarchyEntityType.BusinessOutcome => new BusinessOutcome(),
            HierarchyEntityType.PortfolioEpic => new PortfolioEpic(),
            _ => new StrategicObjective(),
        };
        item.JiraId = jiraId;
        item.ProjectKey = projectKey;
        item.Summary = "Stored summary";
        return item;
    }

    private Task SeedItemAsync(HierarchyEntityType type, string jiraId, string? projectKey) =>
        _db.SeedAsync(db => db.Add((object)NewItem(type, jiraId, projectKey)));

    private Task SeedArtAsync(int id, string name, string key) =>
        _db.SeedAsync(db => db.CapitalProjects.Add(new Art
        {
            Id = id,
            Name = name,
            JiraKeys = new List<ArtJiraKey> { new() { JiraKey = key } },
        }));

    private Task<JiraIssue> ReadItemAsync(HierarchyEntityType type, string jiraId) =>
        _db.ReadAsync<JiraIssue>(async db => type switch
        {
            HierarchyEntityType.Feature => await db.Features.SingleAsync(x => x.JiraId == jiraId),
            HierarchyEntityType.BusinessOutcome => await db.BusinessOutcomes.SingleAsync(x => x.JiraId == jiraId),
            HierarchyEntityType.PortfolioEpic => await db.PortfolioEpics.SingleAsync(x => x.JiraId == jiraId),
            _ => await db.StrategicObjectives.SingleAsync(x => x.JiraId == jiraId),
        });

    private Task<List<int>> LinkedArtIdsAsync(string jiraId) =>
        _db.ReadAsync(db => db.CapitalProjectStrategicObjectives
            .Where(l => l.StrategicObjective.JiraId == jiraId)
            .OrderBy(l => l.CapitalProjectId)
            .Select(l => l.CapitalProjectId)
            .ToListAsync());

    [Theory]
    [InlineData(HierarchyEntityType.Feature)]
    [InlineData(HierarchyEntityType.BusinessOutcome)]
    [InlineData(HierarchyEntityType.PortfolioEpic)]
    [InlineData(HierarchyEntityType.StrategicObjective)]
    public async Task Creating_an_item_sets_its_project_key_from_the_issue_key(HierarchyEntityType type)
    {
        var result = await WriteAsync(type, Issue("PAY-1"));

        var item = await ReadItemAsync(type, "PAY-1");
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Equal("PAY", item.ProjectKey);
        Assert.Equal("Summary of PAY-1", item.Summary);
    }

    [Fact]
    public async Task A_created_feature_is_marked_as_linked_to_jira()
    {
        await WriteAsync(HierarchyEntityType.Feature, Issue("PAY-1"));

        var feature = await _db.ReadAsync(db => db.Features.SingleAsync(f => f.JiraId == "PAY-1"));
        Assert.True(feature.IsLinkedToTheJira);
    }

    [Fact]
    public async Task Updating_a_feature_overwrites_its_project_key_from_the_issue_key()
    {
        await SeedItemAsync(HierarchyEntityType.Feature, "PAY-1", "OLD");

        var result = await WriteAsync(HierarchyEntityType.Feature, Issue("PAY-1"));

        var item = await ReadItemAsync(HierarchyEntityType.Feature, "PAY-1");
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal("PAY", item.ProjectKey);
        Assert.Equal("Summary of PAY-1", item.Summary);
    }

    [Theory]
    [InlineData(HierarchyEntityType.BusinessOutcome)]
    [InlineData(HierarchyEntityType.PortfolioEpic)]
    [InlineData(HierarchyEntityType.StrategicObjective)]
    public async Task Updating_a_hierarchy_item_keeps_its_stored_project_key(HierarchyEntityType type)
    {
        await SeedItemAsync(type, "STRAT-1", "PAY");

        var result = await WriteAsync(type, Issue("STRAT-1"));

        var item = await ReadItemAsync(type, "STRAT-1");
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal("PAY", item.ProjectKey);
        Assert.Equal("Summary of STRAT-1", item.Summary);
    }

    [Theory]
    [InlineData(HierarchyEntityType.BusinessOutcome, null)]
    [InlineData(HierarchyEntityType.PortfolioEpic, null)]
    [InlineData(HierarchyEntityType.StrategicObjective, null)]
    [InlineData(HierarchyEntityType.StrategicObjective, "  ")]
    public async Task An_existing_hierarchy_item_without_a_project_key_gets_it_from_the_issue_key(
        HierarchyEntityType type, string? storedProjectKey)
    {
        await SeedItemAsync(type, "STRAT-1", storedProjectKey);

        var result = await WriteAsync(type, Issue("STRAT-1"));

        var item = await ReadItemAsync(type, "STRAT-1");
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal("STRAT", item.ProjectKey);
    }

    [Fact]
    public async Task Created_and_updated_items_are_counted()
    {
        await SeedItemAsync(HierarchyEntityType.Feature, "PAY-1", "PAY");

        var result = await WriteAsync(HierarchyEntityType.Feature, Issue("PAY-1"), Issue("PAY-2"), Issue("PAY-3"));

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Empty(result.Warnings);
        Assert.Equal(3, await _db.ReadAsync(db => db.Features.CountAsync()));
    }

    [Fact]
    public async Task An_empty_batch_writes_nothing()
    {
        var result = await WriteAsync(HierarchyEntityType.Feature);

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Updated);
        Assert.Empty(result.Warnings);
        Assert.Equal(0, await _db.ReadAsync(db => db.Features.CountAsync()));
    }

    [Fact]
    public async Task A_written_objective_is_linked_to_the_only_art_on_its_key()
    {
        await SeedArtAsync(1, "Payments", "PAY");
        await SeedArtAsync(2, "Loans", "LOAN");

        await WriteAsync(HierarchyEntityType.StrategicObjective, Issue("PAY-7"));

        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync("PAY-7"));
    }

    [Fact]
    public async Task A_written_objective_on_a_key_two_arts_list_is_linked_to_no_art()
    {
        await SeedArtAsync(1, "Payments", "PAY");
        await SeedArtAsync(2, "Cards", "PAY");

        await WriteAsync(HierarchyEntityType.StrategicObjective, Issue("PAY-7"));

        Assert.Empty(await LinkedArtIdsAsync("PAY-7"));
    }

    [Fact]
    public async Task An_updated_objective_is_linked_through_its_stored_project_key()
    {
        await SeedArtAsync(1, "Payments", "PAY");
        await SeedArtAsync(2, "Strategy", "STRAT");
        await SeedItemAsync(HierarchyEntityType.StrategicObjective, "STRAT-1", "PAY");

        await WriteAsync(HierarchyEntityType.StrategicObjective, Issue("STRAT-1"));

        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync("STRAT-1"));
    }

    [Fact]
    public async Task Writing_an_objective_twice_links_it_to_its_art_once()
    {
        await SeedArtAsync(1, "Payments", "PAY");

        await WriteAsync(HierarchyEntityType.StrategicObjective, Issue("PAY-7"));
        await WriteAsync(HierarchyEntityType.StrategicObjective, Issue("PAY-7"));

        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync("PAY-7"));
    }
}
