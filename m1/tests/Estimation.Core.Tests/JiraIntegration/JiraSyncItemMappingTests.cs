using Estimation.Core.JiraIntegration.Client;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraSyncItemMappingTests
{
    private static JiraIssueResponse Response() => new()
    {
        Key = "PROJ-42",
        Summary = "Ship the feature",
        Description = "The long description",
        AcceptanceCriteria = "Given when then",
        NavigatorId = "NAV-7",
        IssueType = "Feature",
        Labels = new List<string> { "alpha", "beta" },
        Components = new List<string> { "Cards", "Wallet" },
        FeatureName = "Short name",
        RagExplain = "Amber because",
        ParentLink = "PROJ-100",
        Status = "In Progress",
        Updated = new DateTime(2026, 7, 30, 10, 15, 0),
        TargetStart = new DateTime(2026, 7, 1),
        TargetEnd = new DateTime(2026, 9, 30),
        StoryPoints = 8,
        GfedTeam = "CFT-Neon, CFT-Argon",
        PlanningIncrement = "PI 2026.1"
    };

    [Fact]
    public void Labels_are_joined_with_commas()
    {
        Assert.Equal("alpha,beta", JiraSyncItemMapping.JoinLabels(new List<string> { "alpha", "beta" }));
    }

    [Fact]
    public void A_single_label_joins_to_itself()
    {
        Assert.Equal("alpha", JiraSyncItemMapping.JoinLabels(new List<string> { "alpha" }));
    }

    [Fact]
    public void An_empty_label_list_joins_to_nothing()
    {
        Assert.Null(JiraSyncItemMapping.JoinLabels(new List<string>()));
    }

    [Fact]
    public void A_missing_label_list_joins_to_nothing()
    {
        Assert.Null(JiraSyncItemMapping.JoinLabels(null));
    }

    [Theory]
    [InlineData("PROJ-123", "PROJ")]
    [InlineData("A-1", "A")]
    [InlineData("PROJ-123-456", "PROJ")]
    public void The_project_key_is_the_prefix_before_the_first_dash(string jiraKey, string expected)
    {
        Assert.Equal(expected, JiraSyncItemMapping.ProjectKeyFromJiraKey(jiraKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PROJ")]
    [InlineData("-123")]
    public void A_key_without_a_usable_prefix_has_no_project_key(string? jiraKey)
    {
        Assert.Null(JiraSyncItemMapping.ProjectKeyFromJiraKey(jiraKey));
    }

    [Fact]
    public void A_feature_sync_item_carries_every_field_the_feature_owns()
    {
        var item = Response().ToFeatureSyncItem();

        Assert.Equal("PROJ-42", item.JiraKey);
        Assert.Equal("Ship the feature", item.Summary);
        Assert.Equal("The long description", item.Description);
        Assert.Equal("Given when then", item.AcceptanceCriteria);
        Assert.Equal("NAV-7", item.NavigatorId);
        Assert.Equal("Feature", item.IssueType);
        Assert.Equal("alpha,beta", item.Labels);
        Assert.Equal("Cards,Wallet", item.Components);
        Assert.Equal("Short name", item.FeatureName);
        Assert.Equal("Amber because", item.RagExplain);
        Assert.Equal("PROJ-100", item.ParentLink);
        Assert.Equal("In Progress", item.Status);
        Assert.Equal(new DateTime(2026, 7, 30, 10, 15, 0), item.JiraUpdated);
        Assert.Equal(new DateTime(2026, 7, 1), item.TargetStart);
        Assert.Equal(new DateTime(2026, 9, 30), item.TargetEnd);
        Assert.Equal(8, item.StoryPoints);
        Assert.Equal("CFT-Neon, CFT-Argon", item.GfedTeam);
        Assert.Equal("PI 2026.1", item.PlanningIncrement);
    }

    [Fact]
    public void A_feature_sync_item_has_no_mask_by_default()
    {
        Assert.Null(Response().ToFeatureSyncItem().PropertyMask);
    }

    [Fact]
    public void A_feature_sync_item_carries_the_mask_it_was_given()
    {
        var mask = new HashSet<string> { JiraSyncProperties.Summary };

        Assert.Same(mask, Response().ToFeatureSyncItem(mask).PropertyMask);
    }

    [Fact]
    public void An_epic_sync_item_carries_the_shared_fields()
    {
        var item = Response().ToEpicSyncItem();

        Assert.Equal("PROJ-42", item.JiraKey);
        Assert.Equal("Ship the feature", item.Summary);
        Assert.Equal("alpha,beta", item.Labels);
        Assert.Equal("Cards,Wallet", item.Components);
        Assert.Equal("PROJ-100", item.ParentLink);
        Assert.Equal(8, item.StoryPoints);
    }

    [Fact]
    public void An_epic_sync_item_carries_the_mask_it_was_given()
    {
        var mask = new HashSet<string> { JiraSyncProperties.Status };

        Assert.Same(mask, Response().ToEpicSyncItem(mask).PropertyMask);
    }

    [Fact]
    public void A_strategic_objective_sync_item_carries_the_shared_fields()
    {
        var item = Response().ToStrategicObjectiveSyncItem();

        Assert.Equal("PROJ-42", item.JiraKey);
        Assert.Equal("Ship the feature", item.Summary);
        Assert.Equal("Given when then", item.AcceptanceCriteria);
        Assert.Equal("alpha,beta", item.Labels);
        Assert.Equal("Cards,Wallet", item.Components);
        Assert.Equal("In Progress", item.Status);
    }

    [Fact]
    public void A_strategic_objective_sync_item_carries_the_mask_it_was_given()
    {
        var mask = new HashSet<string> { JiraSyncProperties.Labels };

        Assert.Same(mask, Response().ToStrategicObjectiveSyncItem(mask).PropertyMask);
    }

    [Fact]
    public void An_issue_without_labels_maps_to_no_label_string()
    {
        var response = Response();
        response.Labels = null;

        Assert.Null(response.ToFeatureSyncItem().Labels);
        Assert.Null(response.ToEpicSyncItem().Labels);
        Assert.Null(response.ToStrategicObjectiveSyncItem().Labels);
    }

    [Fact]
    public void An_issue_without_components_maps_to_no_component_string()
    {
        var response = Response();
        response.Components = new List<string>();

        Assert.Null(response.ToFeatureSyncItem().Components);
        Assert.Null(response.ToEpicSyncItem().Components);
        Assert.Null(response.ToStrategicObjectiveSyncItem().Components);
    }

    [Fact]
    public void Match_facts_carry_the_project_key_labels_and_components()
    {
        var facts = Response().ToMatchFacts();

        Assert.Equal("PROJ", facts.ProjectKey);
        Assert.Equal("PROJ-42", facts.JiraId);
        Assert.Equal("alpha,beta", facts.Labels);
        Assert.Equal("Cards,Wallet", facts.Components);
    }

    [Fact]
    public void Components_are_joined_with_commas()
    {
        Assert.Equal("Cards,Wallet", JiraSyncItemMapping.JoinComponents(new List<string> { "Cards", "Wallet" }));
        Assert.Null(JiraSyncItemMapping.JoinComponents(null));
    }
}
