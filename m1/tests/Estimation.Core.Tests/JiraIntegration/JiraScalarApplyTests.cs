using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.JiraIntegration.Client;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraScalarApplyTests
{
    private static JiraFeatureSyncItem Item(HashSet<string>? mask = null, string? summary = "From Jira") =>
        new(
            JiraKey: "PROJ-42",
            Summary: summary,
            Description: "Jira description",
            AcceptanceCriteria: "Jira criteria",
            NavigatorId: "NAV-9",
            IssueType: "Feature",
            Labels: "alpha,beta",
            Components: "Cards,Wallet",
            FeatureName: "Jira name",
            RagExplain: "Jira rag",
            ParentLink: "PROJ-100",
            Status: "In Progress",
            JiraUpdated: new DateTime(2026, 7, 30),
            TargetStart: new DateTime(2026, 7, 1),
            TargetEnd: new DateTime(2026, 9, 30),
            StoryPoints: 8,
            GfedTeam: "CFT-Neon",
            PlanningIncrement: "PI 2026.1")
        {
            PropertyMask = mask
        };

    private static Feature Existing() => new()
    {
        Id = 1,
        JiraId = "PROJ-42",
        Summary = "Local summary",
        Description = "Local description",
        AcceptanceCriteria = "Local criteria",
        NavigatorId = "NAV-1",
        Labels = "local",
        Components = "Local component",
        Status = "To Do",
        TargetStart = new DateTime(2026, 1, 1),
        TargetEnd = new DateTime(2026, 3, 31),
        StoryPoints = 3,
        Ranking = 7
    };

    [Fact]
    public void Without_a_mask_every_property_may_be_written()
    {
        Assert.True(JiraScalarApply.ShouldWrite(null, JiraSyncProperties.Summary));
    }

    [Fact]
    public void A_property_named_in_the_mask_may_be_written()
    {
        var mask = new HashSet<string> { JiraSyncProperties.Summary };

        Assert.True(JiraScalarApply.ShouldWrite(mask, JiraSyncProperties.Summary));
    }

    [Fact]
    public void A_property_absent_from_the_mask_is_left_alone()
    {
        var mask = new HashSet<string> { JiraSyncProperties.Summary };

        Assert.False(JiraScalarApply.ShouldWrite(mask, JiraSyncProperties.Status));
    }

    [Fact]
    public void An_empty_mask_blocks_everything()
    {
        Assert.False(JiraScalarApply.ShouldWrite(new HashSet<string>(), JiraSyncProperties.Summary));
    }

    [Fact]
    public void An_unmasked_update_overwrites_every_scalar()
    {
        var feature = Existing();

        JiraScalarApply.ApplyToExisting(feature, Item());

        Assert.Equal("From Jira", feature.Summary);
        Assert.Equal("Jira description", feature.Description);
        Assert.Equal("Jira criteria", feature.AcceptanceCriteria);
        Assert.Equal("NAV-9", feature.NavigatorId);
        Assert.Equal("alpha,beta", feature.Labels);
        Assert.Equal("Cards,Wallet", feature.Components);
        Assert.Equal("In Progress", feature.Status);
        Assert.Equal(new DateTime(2026, 7, 1), feature.TargetStart);
        Assert.Equal(new DateTime(2026, 9, 30), feature.TargetEnd);
        Assert.Equal(8, feature.StoryPoints);
    }

    [Fact]
    public void An_update_never_touches_local_only_planning_data()
    {
        var feature = Existing();

        JiraScalarApply.ApplyToExisting(feature, Item());

        Assert.Equal(7, feature.Ranking);
    }

    [Fact]
    public void A_missing_summary_from_jira_never_clears_the_local_one()
    {
        var feature = Existing();

        JiraScalarApply.ApplyToExisting(feature, Item(summary: null));

        Assert.Equal("Local summary", feature.Summary);
    }

    [Fact]
    public void A_missing_optional_value_from_jira_does_clear_the_local_one()
    {
        var feature = Existing();
        var item = Item() with { Description = null, Status = null };

        JiraScalarApply.ApplyToExisting(feature, item);

        Assert.Null(feature.Description);
        Assert.Null(feature.Status);
    }

    [Theory]
    [InlineData(JiraSyncProperties.Summary)]
    [InlineData(JiraSyncProperties.Description)]
    [InlineData(JiraSyncProperties.AcceptanceCriteria)]
    [InlineData(JiraSyncProperties.NavigatorId)]
    [InlineData(JiraSyncProperties.Labels)]
    [InlineData(JiraSyncProperties.Components)]
    [InlineData(JiraSyncProperties.Status)]
    [InlineData(JiraSyncProperties.TargetStart)]
    [InlineData(JiraSyncProperties.TargetEnd)]
    [InlineData(JiraSyncProperties.StoryPoints)]
    public void A_mask_of_one_property_writes_only_that_property(string property)
    {
        var feature = Existing();
        var before = Existing();

        JiraScalarApply.ApplyToExisting(feature, Item(new HashSet<string> { property }));

        var changed = new List<string>();
        if (feature.Summary != before.Summary) changed.Add(JiraSyncProperties.Summary);
        if (feature.Description != before.Description) changed.Add(JiraSyncProperties.Description);
        if (feature.AcceptanceCriteria != before.AcceptanceCriteria) changed.Add(JiraSyncProperties.AcceptanceCriteria);
        if (feature.NavigatorId != before.NavigatorId) changed.Add(JiraSyncProperties.NavigatorId);
        if (feature.Labels != before.Labels) changed.Add(JiraSyncProperties.Labels);
        if (feature.Components != before.Components) changed.Add(JiraSyncProperties.Components);
        if (feature.Status != before.Status) changed.Add(JiraSyncProperties.Status);
        if (feature.TargetStart != before.TargetStart) changed.Add(JiraSyncProperties.TargetStart);
        if (feature.TargetEnd != before.TargetEnd) changed.Add(JiraSyncProperties.TargetEnd);
        if (feature.StoryPoints != before.StoryPoints) changed.Add(JiraSyncProperties.StoryPoints);

        Assert.Equal(new[] { property }, changed);
    }

    [Fact]
    public void The_jira_timestamp_is_written_even_when_the_mask_excludes_everything()
    {
        var feature = Existing();

        JiraScalarApply.ApplyToExisting(feature, Item(new HashSet<string>()));

        Assert.Equal(new DateTime(2026, 7, 30), feature.JiraUpdated);
    }

    [Fact]
    public void An_insert_fills_every_scalar_from_jira()
    {
        var feature = new Feature();

        JiraScalarApply.ApplyToNew(feature, Item(), "PROJ");

        Assert.Equal("PROJ-42", feature.JiraId);
        Assert.Equal("PROJ", feature.ProjectKey);
        Assert.Equal("Feature", feature.IssueType);
        Assert.Equal("From Jira", feature.Summary);
        Assert.Equal("Jira description", feature.Description);
        Assert.Equal("Jira criteria", feature.AcceptanceCriteria);
        Assert.Equal("NAV-9", feature.NavigatorId);
        Assert.Equal("alpha,beta", feature.Labels);
        Assert.Equal("Cards,Wallet", feature.Components);
        Assert.Equal("In Progress", feature.Status);
        Assert.Equal(new DateTime(2026, 7, 30), feature.JiraUpdated);
        Assert.Equal(8, feature.StoryPoints);
    }

    [Fact]
    public void Components_removed_in_jira_are_cleared_locally()
    {
        var feature = Existing();

        JiraScalarApply.ApplyToExisting(feature, Item() with { Components = null });

        Assert.Null(feature.Components);
    }

    [Fact]
    public void A_mask_without_components_keeps_the_local_components()
    {
        var feature = Existing();

        JiraScalarApply.ApplyToExisting(feature, Item(new HashSet<string> { JiraSyncProperties.Labels }));

        Assert.Equal("alpha,beta", feature.Labels);
        Assert.Equal("Local component", feature.Components);
    }

    [Fact]
    public void An_insert_without_a_summary_falls_back_to_the_issue_key()
    {
        var feature = new Feature();

        JiraScalarApply.ApplyToNew(feature, Item(summary: null), "PROJ");

        Assert.Equal("PROJ-42", feature.Summary);
    }

    [Fact]
    public void An_insert_ignores_the_mask()
    {
        var feature = new Feature();

        JiraScalarApply.ApplyToNew(feature, Item(new HashSet<string>()), "PROJ");

        Assert.Equal("From Jira", feature.Summary);
        Assert.Equal("In Progress", feature.Status);
        Assert.Equal(8, feature.StoryPoints);
    }

    [Fact]
    public void An_insert_takes_the_project_key_from_the_issue_key()
    {
        var feature = new Feature();

        JiraScalarApply.ApplyToNew(feature, Item() with { JiraKey = "PAY-7" }, "PROJ");

        Assert.Equal("PAY", feature.ProjectKey);
    }

    [Fact]
    public void An_insert_derives_the_project_key_even_without_a_fallback()
    {
        var feature = new Feature();

        JiraScalarApply.ApplyToNew(feature, Item(), null);

        Assert.Equal("PROJ", feature.ProjectKey);
    }

    [Fact]
    public void An_insert_falls_back_to_the_given_project_key_when_the_issue_key_has_no_prefix()
    {
        var feature = new Feature();

        JiraScalarApply.ApplyToNew(feature, Item() with { JiraKey = "42" }, "PROJ");

        Assert.Equal("PROJ", feature.ProjectKey);
    }

    [Fact]
    public void An_insert_can_be_left_without_a_project_key()
    {
        var feature = new Feature();

        JiraScalarApply.ApplyToNew(feature, Item() with { JiraKey = "42" }, null);

        Assert.Null(feature.ProjectKey);
    }
}
