using Estimation.Core.Administration.Audit;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraHierarchySyncServiceTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly JiraHierarchySyncService _service;

    public JiraHierarchySyncServiceTests()
    {
        _service = new JiraHierarchySyncService(_db, new StubAuditUser());
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        public string? GetCurrentUserName() => "DOMAIN\\tester";
    }

    private static Art Art(int id, string name, params string[] keys) =>
        new()
        {
            Id = id,
            Name = name,
            JiraKeys = keys.Select(k => new ArtJiraKey { JiraKey = k }).ToList()
        };

    private static JiraIssueResponse StrategicObjective(string key = "STRAT-1") =>
        new()
        {
            Key = key,
            Summary = "Grow payments",
            IssueType = "Strategic Objective"
        };

    private Task<List<int>> LinkedArtIdsAsync(int soId) =>
        _db.ReadAsync(db => db.CapitalProjectStrategicObjectives
            .Where(l => l.StrategicObjectiveId == soId)
            .Select(l => l.CapitalProjectId)
            .ToListAsync());

    [Fact]
    public async Task A_strategic_objective_is_linked_to_the_art_that_owns_the_key()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments ART", "PAY")));

        var result = await _service.SyncSingleAsync(StrategicObjective(), "PAY", null);

        Assert.True(result.ParentLinked);
        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync(result.EntityId));
    }

    [Fact]
    public async Task Any_key_of_a_multi_key_art_links_to_that_art()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY", "CARD"));
            db.CapitalProjects.Add(Art(2, "Core Banking ART", "CORE"));
        });

        var result = await _service.SyncSingleAsync(StrategicObjective(), "CARD", null);

        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync(result.EntityId));
    }

    [Fact]
    public async Task The_key_is_matched_after_normalisation()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments ART", "PAY")));

        var result = await _service.SyncSingleAsync(StrategicObjective(), " pay ", null);

        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync(result.EntityId));
    }

    [Fact]
    public async Task A_key_shared_by_two_arts_links_to_neither()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY"));
            db.CapitalProjects.Add(Art(2, "Cards ART", "PAY"));
        });

        var result = await _service.SyncSingleAsync(StrategicObjective(), "PAY", null);

        Assert.False(result.ParentLinked);
        Assert.Empty(await LinkedArtIdsAsync(result.EntityId));
    }

    [Fact]
    public async Task A_key_shared_by_arts_told_apart_by_components_links_to_neither()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY"));
            db.CapitalProjects.Add(new Art
            {
                Id = 2,
                Name = "Cards ART",
                JiraKeys = [new ArtJiraKey { JiraKey = "PAY", Components = "Cards" }]
            });
        });

        var result = await _service.SyncSingleAsync(StrategicObjective(), "PAY", null);

        Assert.False(result.ParentLinked);
        Assert.Empty(await LinkedArtIdsAsync(result.EntityId));
    }

    [Fact]
    public async Task A_synced_feature_takes_its_labels_and_components_from_jira()
    {
        var issue = new JiraIssueResponse
        {
            Key = "PAY-1",
            Summary = "Card payments",
            IssueType = "Feature",
            Labels = ["web", "retail"],
            Components = ["Cards", "Wallet"]
        };

        var result = await _service.SyncSingleAsync(issue, "PAY", null);

        var feature = await _db.ReadAsync(db => db.Features.SingleAsync(f => f.Id == result.EntityId));
        Assert.Equal("web,retail", feature.Labels);
        Assert.Equal("Cards,Wallet", feature.Components);
    }

    [Fact]
    public async Task A_synced_parent_takes_its_components_from_jira()
    {
        var issue = new JiraIssueResponse
        {
            Key = "BO-1",
            Summary = "Faster checkout",
            IssueType = "Business Outcome",
            Components = ["Cards"]
        };

        var result = await _service.SyncSingleAsync(issue, "PAY", null);

        var outcome = await _db.ReadAsync(db => db.BusinessOutcomes.SingleAsync(b => b.Id == result.EntityId));
        Assert.Equal("Cards", outcome.Components);
    }

    [Fact]
    public async Task An_unknown_key_leaves_the_strategic_objective_unlinked()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments ART", "PAY")));

        var result = await _service.SyncSingleAsync(StrategicObjective(), "OTHER", null);

        Assert.False(result.ParentLinked);
        Assert.Empty(await LinkedArtIdsAsync(result.EntityId));
    }

    [Fact]
    public async Task Syncing_twice_does_not_duplicate_the_link()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments ART", "PAY")));

        await _service.SyncSingleAsync(StrategicObjective(), "PAY", null);
        var second = await _service.SyncSingleAsync(StrategicObjective(), "PAY", null);

        Assert.False(second.ParentLinked);
        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync(second.EntityId));
    }
}
