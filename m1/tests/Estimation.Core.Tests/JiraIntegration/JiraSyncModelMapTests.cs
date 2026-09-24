using Estimation.Core.Features.Models;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Client.JiraSync;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class BackgroundOptOutEntity : JiraIssue
{
    [JiraSync(JiraSyncFields.RagExplain, BackgroundSync = false)]
    public string? BackgroundOptOut { get; set; }

    [JiraSync(JiraSyncFields.FeatureName, ManualSync = false)]
    public string? ManualOptOut { get; set; }
}

public class UnknownFieldEntity : JiraIssue
{
    [JiraSync("NotAFieldAnyoneKnows")]
    public string? Mystery { get; set; }
}

public class ReadOnlyPropertyEntity : JiraIssue
{
    [JiraSync(JiraSyncFields.RagExplain)]
    public string? ReadOnlyValue => null;
}

public class JiraSyncFieldSelectorsTests
{
    [Theory]
    [InlineData(JiraSyncFields.Summary)]
    [InlineData(JiraSyncFields.Description)]
    [InlineData(JiraSyncFields.Status)]
    [InlineData(JiraSyncFields.StoryPoints)]
    [InlineData(JiraSyncFields.TargetStart)]
    [InlineData(JiraSyncFields.RagExplain)]
    public void Every_scalar_field_has_a_selector(string field)
    {
        Assert.True(JiraSyncFieldSelectors.IsKnownScalar(field));
        Assert.True(JiraSyncFieldSelectors.TryGet(field, out var selector));
        Assert.NotNull(selector);
    }

    [Theory]
    [InlineData(JiraSyncFields.ParentLink)]
    [InlineData(JiraSyncFields.GfedTeam)]
    [InlineData(JiraSyncFields.PlanningIncrement)]
    public void Relationship_fields_have_no_scalar_selector(string field)
    {
        Assert.False(JiraSyncFieldSelectors.IsKnownScalar(field));
    }

    [Fact]
    public void An_unknown_field_has_no_selector()
    {
        Assert.False(JiraSyncFieldSelectors.TryGet("NotAField", out var selector));
        Assert.Null(selector);
    }

    [Fact]
    public void Field_names_are_matched_without_regard_to_case()
    {
        Assert.True(JiraSyncFieldSelectors.IsKnownScalar("summary"));
    }

    [Fact]
    public void The_label_selector_returns_the_joined_string_the_column_expects()
    {
        JiraSyncFieldSelectors.TryGet(JiraSyncFields.Labels, out var selector);

        var value = selector!(new JiraIssueResponse { Labels = new List<string> { "alpha", "beta" } });

        Assert.Equal("alpha,beta", value);
    }
}

public class JiraSyncModelMapTests
{
    [Fact]
    public void A_feature_binds_the_properties_inherited_from_the_shared_issue()
    {
        var fields = JiraSyncModelMap.For(typeof(Feature)).Select(b => b.Field).ToList();

        Assert.Contains(JiraSyncFields.Summary, fields);
        Assert.Contains(JiraSyncFields.Status, fields);
        Assert.Contains(JiraSyncFields.StoryPoints, fields);
    }

    [Fact]
    public void A_feature_binds_its_own_properties_too()
    {
        var fields = JiraSyncModelMap.For(typeof(Feature)).Select(b => b.Field).ToList();

        Assert.Contains(JiraSyncFields.FeatureName, fields);
        Assert.Contains(JiraSyncFields.RagExplain, fields);
    }

    [Fact]
    public void Properties_without_the_attribute_are_not_bound()
    {
        var properties = JiraSyncModelMap.For(typeof(Feature)).Select(b => b.Property.Name).ToList();

        Assert.DoesNotContain(nameof(Feature.Ranking), properties);
        Assert.DoesNotContain(nameof(Feature.ConfidencePercentage), properties);
        Assert.DoesNotContain(nameof(Feature.ModifiedBy), properties);
    }

    [Fact]
    public void A_scalar_binding_carries_a_selector()
    {
        var binding = JiraSyncModelMap.For(typeof(Feature)).Single(b => b.Field == JiraSyncFields.Summary);

        Assert.True(binding.IsScalar);
        Assert.NotNull(binding.ScalarSelector);
        Assert.Null(binding.Converter);
    }

    [Theory]
    [InlineData(JiraSyncFields.GfedTeam)]
    [InlineData(JiraSyncFields.PlanningIncrement)]
    [InlineData(JiraSyncFields.ParentLink)]
    public void A_relationship_binding_carries_a_converter_instead_of_a_selector(string field)
    {
        var binding = JiraSyncModelMap.For(typeof(Feature)).Single(b => b.Field == field);

        Assert.False(binding.IsScalar);
        Assert.NotNull(binding.Converter);
        Assert.Null(binding.ScalarSelector);
    }

    [Fact]
    public void The_summary_is_marked_required_so_a_jira_gap_cannot_null_it()
    {
        var binding = JiraSyncModelMap.For(typeof(Feature)).Single(b => b.Field == JiraSyncFields.Summary);

        Assert.True(binding.Required);
    }

    [Fact]
    public void Optional_scalars_are_not_marked_required()
    {
        var binding = JiraSyncModelMap.For(typeof(Feature)).Single(b => b.Field == JiraSyncFields.Description);

        Assert.False(binding.Required);
    }

    [Fact]
    public void The_planning_increment_takes_part_in_the_background_sync()
    {
        var binding = JiraSyncModelMap.For(typeof(Feature)).Single(b => b.Field == JiraSyncFields.PlanningIncrement);

        Assert.True(binding.BackgroundSync);
        Assert.True(binding.ManualSync);
    }

    [Fact]
    public void Bindings_default_to_participating_in_both_syncs()
    {
        var binding = JiraSyncModelMap.For(typeof(Feature)).Single(b => b.Field == JiraSyncFields.Status);

        Assert.True(binding.BackgroundSync);
        Assert.True(binding.ManualSync);
    }

    [Fact]
    public void The_bindings_for_a_type_are_built_once_and_cached()
    {
        Assert.Same(JiraSyncModelMap.For(typeof(Feature)), JiraSyncModelMap.For(typeof(Feature)));
    }

    [Fact]
    public void Each_entity_type_gets_its_own_bindings()
    {
        Assert.NotSame(JiraSyncModelMap.For(typeof(Feature)), JiraSyncModelMap.For(typeof(PortfolioEpic)));
    }

    [Fact]
    public void A_train_entity_binds_the_shared_scalars_and_its_parent_link()
    {
        var fields = JiraSyncModelMap.For(typeof(StrategicObjective)).Select(b => b.Field).ToList();

        Assert.Contains(JiraSyncFields.Summary, fields);
        Assert.Contains(JiraSyncFields.ParentLink, fields);
        Assert.DoesNotContain(JiraSyncFields.FeatureName, fields);
    }

    [Fact]
    public void An_attribute_naming_an_unknown_field_fails_loudly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JiraSyncModelMap.For(typeof(UnknownFieldEntity)));

        Assert.Contains("NotAFieldAnyoneKnows", ex.Message);
    }

    [Fact]
    public void An_attribute_on_a_read_only_property_fails_loudly()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JiraSyncModelMap.For(typeof(ReadOnlyPropertyEntity)));

        Assert.Contains("settable", ex.Message);
    }

    [Fact]
    public void Validating_a_good_entity_type_does_not_throw()
    {
        JiraSyncModelMap.Validate(typeof(Feature), typeof(PortfolioEpic), typeof(StrategicObjective));
    }

    [Fact]
    public void Validating_a_bad_entity_type_throws()
    {
        Assert.Throws<InvalidOperationException>(() => JiraSyncModelMap.Validate(typeof(UnknownFieldEntity)));
    }
}

public class JiraSyncApplierTests
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
        Status = "In Progress",
        Updated = new DateTime(2026, 7, 30),
        TargetStart = new DateTime(2026, 7, 1),
        TargetEnd = new DateTime(2026, 9, 30),
        StoryPoints = 8
    };

    [Fact]
    public void The_bound_scalars_are_copied_onto_the_entity()
    {
        var feature = new Feature();

        JiraSyncApplier.ApplyScalars(feature, Response(), isNew: true, background: false);

        Assert.Equal("Ship the feature", feature.Summary);
        Assert.Equal("The long description", feature.Description);
        Assert.Equal("Given when then", feature.AcceptanceCriteria);
        Assert.Equal("NAV-7", feature.NavigatorId);
        Assert.Equal("Feature", feature.IssueType);
        Assert.Equal("alpha,beta", feature.Labels);
        Assert.Equal("Cards,Wallet", feature.Components);
        Assert.Equal("In Progress", feature.Status);
        Assert.Equal(new DateTime(2026, 7, 30), feature.JiraUpdated);
        Assert.Equal(new DateTime(2026, 7, 1), feature.TargetStart);
        Assert.Equal(new DateTime(2026, 9, 30), feature.TargetEnd);
        Assert.Equal(8, feature.StoryPoints);
        Assert.Equal("Short name", feature.Name);
        Assert.Equal("Amber because", feature.RagExplain);
    }

    [Fact]
    public void The_fields_actually_written_are_reported_back()
    {
        var written = JiraSyncApplier.ApplyScalars(new Feature(), Response(), isNew: true, background: false);

        Assert.Contains(JiraSyncFields.Summary, written);
        Assert.Contains(JiraSyncFields.StoryPoints, written);
        Assert.DoesNotContain(JiraSyncFields.GfedTeam, written);
        Assert.DoesNotContain(JiraSyncFields.PlanningIncrement, written);
    }

    [Fact]
    public void Relationship_bindings_are_left_to_their_converters()
    {
        var feature = new Feature { Pi = null, BusinessOutcomeId = 5 };

        JiraSyncApplier.ApplyScalars(feature, Response(), isNew: false, background: false);

        Assert.Null(feature.Pi);
        Assert.Equal(5, feature.BusinessOutcomeId);
    }

    [Fact]
    public void Local_only_properties_are_never_written()
    {
        var feature = new Feature { Ranking = 7, Dependencies = "local note", ExternalDependencies = true };

        JiraSyncApplier.ApplyScalars(feature, Response(), isNew: false, background: false);

        Assert.Equal(7, feature.Ranking);
        Assert.Equal("local note", feature.Dependencies);
        Assert.True(feature.ExternalDependencies);
    }

    [Fact]
    public void An_insert_without_a_summary_falls_back_to_the_issue_key()
    {
        var response = Response();
        response.Summary = null;

        var feature = new Feature();
        JiraSyncApplier.ApplyScalars(feature, response, isNew: true, background: false);

        Assert.Equal("PROJ-42", feature.Summary);
    }

    [Fact]
    public void An_update_without_a_summary_keeps_the_existing_one()
    {
        var response = Response();
        response.Summary = null;

        var feature = new Feature { Summary = "Local summary" };
        var written = JiraSyncApplier.ApplyScalars(feature, response, isNew: false, background: false);

        Assert.Equal("Local summary", feature.Summary);
        Assert.DoesNotContain(JiraSyncFields.Summary, written);
    }

    [Fact]
    public void An_optional_field_is_cleared_when_jira_has_no_value()
    {
        var response = Response();
        response.Description = null;

        var feature = new Feature { Description = "Local description" };
        JiraSyncApplier.ApplyScalars(feature, response, isNew: false, background: false);

        Assert.Null(feature.Description);
    }

    [Fact]
    public void The_background_sync_skips_properties_that_opted_out_of_it()
    {
        var entity = new BackgroundOptOutEntity();

        var written = JiraSyncApplier.ApplyScalars(entity, Response(), isNew: true, background: true);

        Assert.Null(entity.BackgroundOptOut);
        Assert.DoesNotContain(JiraSyncFields.RagExplain, written);
    }

    [Fact]
    public void The_manual_sync_still_writes_properties_that_only_opted_out_of_the_background_run()
    {
        var entity = new BackgroundOptOutEntity();

        JiraSyncApplier.ApplyScalars(entity, Response(), isNew: true, background: false);

        Assert.Equal("Amber because", entity.BackgroundOptOut);
    }

    [Fact]
    public void The_manual_sync_skips_properties_that_opted_out_of_it()
    {
        var entity = new BackgroundOptOutEntity();

        JiraSyncApplier.ApplyScalars(entity, Response(), isNew: true, background: false);

        Assert.Null(entity.ManualOptOut);
    }

    [Fact]
    public void The_background_sync_still_writes_properties_that_only_opted_out_of_the_manual_run()
    {
        var entity = new BackgroundOptOutEntity();

        JiraSyncApplier.ApplyScalars(entity, Response(), isNew: true, background: true);

        Assert.Equal("Short name", entity.ManualOptOut);
    }
}
