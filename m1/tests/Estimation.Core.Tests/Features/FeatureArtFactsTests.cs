using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Services;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureArtFactsTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly FeatureService _features;

    public FeatureArtFactsTests()
    {
        _features = new FeatureService(_db, new StubAuditUser());
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        public string? GetCurrentUserName() => "tester";
    }

    private static IssueMatchFacts Facts(
        string? projectKey = "PAY", string? jiraId = "PAY-1", string? labels = "web,retail", string? components = "Cards") =>
        new(projectKey, jiraId, labels, components);

    [Fact]
    public void Order_spacing_and_key_case_are_not_a_change()
    {
        var loaded = Facts(labels: "web,retail", components: "Cards,Loans");
        var stored = Facts(projectKey: " pay ", labels: "retail, web", components: "Loans , Cards");

        Assert.False(FeatureArtFacts.Changed(loaded, stored));
    }

    [Fact]
    public void Empty_and_missing_lists_are_the_same()
    {
        Assert.False(FeatureArtFacts.Changed(Facts(labels: null, components: ""), Facts(labels: "", components: null)));
    }

    [Fact]
    public void A_component_added_by_a_sync_is_a_change()
    {
        Assert.True(FeatureArtFacts.Changed(Facts(components: "Cards"), Facts(components: "Cards,Loans")));
    }

    [Fact]
    public void Cleared_components_are_a_change()
    {
        Assert.True(FeatureArtFacts.Changed(Facts(components: "Cards"), Facts(components: null)));
    }

    [Fact]
    public void Changed_labels_are_a_change_even_in_case_only()
    {
        Assert.True(FeatureArtFacts.Changed(Facts(labels: "web"), Facts(labels: "web,cards")));
        Assert.True(FeatureArtFacts.Changed(Facts(labels: "Web"), Facts(labels: "web")));
    }

    [Fact]
    public void Another_project_key_is_a_change()
    {
        Assert.True(FeatureArtFacts.Changed(Facts(projectKey: "PAY"), Facts(projectKey: "CRD")));
        Assert.True(FeatureArtFacts.Changed(Facts(projectKey: null), Facts(projectKey: "PAY")));
    }

    [Fact]
    public void Without_a_project_key_the_jira_id_prefix_decides()
    {
        Assert.False(FeatureArtFacts.Changed(Facts(projectKey: null, jiraId: "PAY-1"), Facts(projectKey: null, jiraId: "PAY-1")));
        Assert.True(FeatureArtFacts.Changed(Facts(projectKey: null, jiraId: null), Facts(projectKey: null, jiraId: "PAY-1")));
        Assert.True(FeatureArtFacts.Changed(Facts(projectKey: null, jiraId: "PAY-1"), Facts(projectKey: null, jiraId: "CRD-1")));
    }

    [Fact]
    public void Of_reads_the_features_art_fields()
    {
        var facts = FeatureArtFacts.Of(new Feature { ProjectKey = "PAY", JiraId = "PAY-7", Labels = "web", Components = "Cards" });

        Assert.Equal(new IssueMatchFacts("PAY", "PAY-7", "web", "Cards"), facts);
    }

    private Task SeedFeatureAsync(string? labels, string? components) =>
        _db.SeedAsync(db => db.Features.Add(new Feature
        {
            Id = 700,
            ProjectKey = "PAY",
            JiraId = "PAY-7",
            Name = "Existing",
            Summary = "Existing summary",
            Labels = labels,
            Components = components
        }));

    private Task<Feature?> StoredAsync() => _db.ReadAsync(db => db.Features.FindAsync(700).AsTask());

    [Fact]
    public async Task Update_refuses_when_a_sync_changed_the_components_since_the_page_loaded()
    {
        await SeedFeatureAsync("web", "Loans");
        var loaded = new IssueMatchFacts("PAY", "PAY-7", "web", "Cards");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _features.UpdateAsync(
            new Feature { Id = 700, ProjectKey = "PAY", JiraId = "PAY-7", Name = "Edited", Summary = "Edited", Labels = "web", Components = "Cards" },
            loaded));

        Assert.Equal(FeatureArtFacts.ChangedMessage, ex.Message);
        var stored = await StoredAsync();
        Assert.Equal("Loans", stored!.Components);
        Assert.Equal("Existing", stored.Name);
    }

    [Fact]
    public async Task Update_refuses_when_a_sync_changed_the_labels_since_the_page_loaded()
    {
        await SeedFeatureAsync("web,cards", "Cards");
        var loaded = new IssueMatchFacts("PAY", "PAY-7", "web", "Cards");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _features.UpdateAsync(
            new Feature { Id = 700, ProjectKey = "PAY", JiraId = "PAY-7", Name = "Edited", Summary = "Edited", Labels = "web", Components = "Cards" },
            loaded));

        Assert.Equal("web,cards", (await StoredAsync())!.Labels);
    }

    [Fact]
    public async Task Update_saves_the_page_values_when_the_stored_ones_are_unchanged()
    {
        await SeedFeatureAsync("web", "Cards");
        var loaded = new IssueMatchFacts("PAY", "PAY-7", "web", "Cards");

        await _features.UpdateAsync(
            new Feature { Id = 700, ProjectKey = "PAY", JiraId = "PAY-7", Name = "Edited", Summary = "Edited", Labels = "web,retail", Components = "Loans" },
            loaded);

        var stored = await StoredAsync();
        Assert.Equal("Edited", stored!.Name);
        Assert.Equal("web,retail", stored.Labels);
        Assert.Equal("Loans", stored.Components);
    }

    [Fact]
    public async Task Update_saves_when_a_sync_already_stored_the_new_values()
    {
        await SeedFeatureAsync("web,retail", "Cards");
        var loaded = new IssueMatchFacts("PAY", "PAY-7", "web", "Cards");

        await _features.UpdateAsync(
            new Feature { Id = 700, ProjectKey = "PAY", JiraId = "PAY-7", Name = "Edited", Summary = "Edited", Labels = "retail,web", Components = "Cards" },
            loaded);

        var stored = await StoredAsync();
        Assert.Equal("Edited", stored!.Name);
        Assert.Equal("retail,web", stored.Labels);
    }

    [Fact]
    public async Task Update_refuses_when_the_stored_values_differ_from_both_the_loaded_and_the_new_ones()
    {
        await SeedFeatureAsync("web", "Loans");
        var loaded = new IssueMatchFacts("PAY", "PAY-7", "web", "Cards");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _features.UpdateAsync(
            new Feature { Id = 700, ProjectKey = "PAY", JiraId = "PAY-7", Name = "Edited", Summary = "Edited", Labels = "web,retail", Components = "Cards" },
            loaded));

        var stored = await StoredAsync();
        Assert.Equal("web", stored!.Labels);
        Assert.Equal("Loans", stored.Components);
    }

    [Fact]
    public async Task Update_without_expected_facts_copies_the_values_as_before()
    {
        await SeedFeatureAsync("web", "Loans");

        await _features.UpdateAsync(
            new Feature { Id = 700, ProjectKey = "PAY", JiraId = "PAY-7", Name = "Edited", Summary = "Edited", Labels = "web", Components = "Cards" });

        Assert.Equal("Cards", (await StoredAsync())!.Components);
    }
}
