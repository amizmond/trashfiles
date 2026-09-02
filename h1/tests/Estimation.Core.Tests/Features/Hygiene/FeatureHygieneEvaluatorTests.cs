using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Hygiene.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Features.Hygiene;

public class FeatureHygieneEvaluatorTests
{
    private static FeatureHygieneRule Rule(
        HygieneField field,
        HygieneCheck check,
        HygieneRuleParameters? parameters = null,
        int id = 1,
        bool enabled = true,
        string? message = null) =>
        new()
        {
            Id = id,
            CapitalProjectId = 1,
            Field = field,
            Check = check,
            ParametersJson = (parameters ?? new HygieneRuleParameters()).ToJson(),
            IsEnabled = enabled,
            Message = message
        };

    private static Feature Feature() => new() { Id = 1, JiraId = "PAY-1", Summary = "A feature" };

    private static HygieneFailure? Check(Feature feature, FeatureHygieneRule rule) =>
        FeatureHygieneEvaluator.Check(feature, rule);

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("   ", "empty")]
    [InlineData("h2. \n{panel}{panel}", "empty")]
    [InlineData("h2. Result", null)]
    public void NotEmpty_on_text_fails_for_missing_or_markup_only_text(string? description, string? expectedReason)
    {
        var feature = Feature();
        feature.Description = description;

        var failure = Check(feature, Rule(HygieneField.Description, HygieneCheck.NotEmpty));

        Assert.Equal(expectedReason, failure?.Reason);
    }

    [Fact]
    public void NotEmpty_covers_numbers_dates_references_and_choices()
    {
        var feature = Feature();

        Assert.Equal("empty", Check(feature, Rule(HygieneField.StoryPoints, HygieneCheck.NotEmpty))?.Reason);
        Assert.Equal("empty", Check(feature, Rule(HygieneField.TargetEnd, HygieneCheck.NotEmpty))?.Reason);
        Assert.Equal("empty", Check(feature, Rule(HygieneField.BusinessOutcome, HygieneCheck.NotEmpty))?.Reason);
        Assert.Equal("empty", Check(feature, Rule(HygieneField.Teams, HygieneCheck.NotEmpty))?.Reason);
        Assert.Equal("empty", Check(feature, Rule(HygieneField.TechnicalApproval, HygieneCheck.NotEmpty))?.Reason);
        Assert.Equal("empty", Check(feature, Rule(HygieneField.Status, HygieneCheck.NotEmpty))?.Reason);

        feature.StoryPoints = 5;
        feature.TargetEnd = new DateTime(2026, 12, 18);
        feature.BusinessOutcomeId = 7;
        feature.FeatureTeams.Add(new FeatureTeam { TeamId = 3, Team = new Team { Id = 3, Name = "Risk Engines" } });
        feature.TechnicalApproval = new TechnicalApproval { Id = 1, Name = "Approved" };
        feature.Status = "To Do";

        Assert.Null(Check(feature, Rule(HygieneField.StoryPoints, HygieneCheck.NotEmpty)));
        Assert.Null(Check(feature, Rule(HygieneField.TargetEnd, HygieneCheck.NotEmpty)));
        Assert.Null(Check(feature, Rule(HygieneField.BusinessOutcome, HygieneCheck.NotEmpty)));
        Assert.Null(Check(feature, Rule(HygieneField.Teams, HygieneCheck.NotEmpty)));
        Assert.Null(Check(feature, Rule(HygieneField.TechnicalApproval, HygieneCheck.NotEmpty)));
        Assert.Null(Check(feature, Rule(HygieneField.Status, HygieneCheck.NotEmpty)));
    }

    [Fact]
    public void ContainsWords_with_all_names_the_missing_phrases()
    {
        var feature = Feature();
        feature.Description = "h2. Task Description\nBuild it.";
        var parameters = new HygieneRuleParameters { Words = ["Task Description", "Result"], Mode = HygieneWordMode.And };

        var failure = Check(feature, Rule(HygieneField.Description, HygieneCheck.ContainsWords, parameters));

        Assert.NotNull(failure);
        Assert.Equal("missing Result", failure.Reason);
        Assert.Equal("Description: missing Result", failure.Label);
        Assert.Equal("Description contains Task Description, Result (all)", failure.RuleText);
    }

    [Fact]
    public void ContainsWords_with_any_passes_when_one_phrase_is_present()
    {
        var feature = Feature();
        feature.Description = "h2. Result\nDone.";
        var parameters = new HygieneRuleParameters { Words = ["Task Description", "Result"], Mode = HygieneWordMode.Or };

        Assert.Null(Check(feature, Rule(HygieneField.Description, HygieneCheck.ContainsWords, parameters)));

        feature.Description = "Nothing relevant";

        var failure = Check(feature, Rule(HygieneField.Description, HygieneCheck.ContainsWords, parameters));

        Assert.Equal("none of Task Description, Result", failure?.Reason);
    }

    [Fact]
    public void ContainsWords_fails_on_empty_text()
    {
        var feature = Feature();
        var parameters = new HygieneRuleParameters { Words = ["Result"] };

        var failure = Check(feature, Rule(HygieneField.Description, HygieneCheck.ContainsWords, parameters));

        Assert.Equal("missing Result", failure?.Reason);
    }

    [Fact]
    public void NotOnlyWords_fails_for_an_unfilled_template_and_passes_once_it_is_written()
    {
        var feature = Feature();
        feature.Description = "h2. Task Description\n\nh2. Result\n-";
        var parameters = new HygieneRuleParameters { Words = ["Task Description", "Result"], MinOtherWords = 3 };
        var rule = Rule(HygieneField.Description, HygieneCheck.NotOnlyWords, parameters);

        Assert.Equal("no other words", Check(feature, rule)?.Reason);

        feature.Description = "h2. Task Description\nBuild the\nh2. Result\n-";

        Assert.Equal("only 2 other words, 3 needed", Check(feature, rule)?.Reason);

        feature.Description = "h2. Task Description\nAggregate exposures per *netting set* before the EAD run.\nh2. Result\nEAD per netting set is available.";

        Assert.Null(Check(feature, rule));
    }

    [Fact]
    public void NotOnlyWords_does_not_require_the_phrases_themselves()
    {
        var feature = Feature();
        feature.Description = "A real description without the template headings.";
        var parameters = new HygieneRuleParameters { Words = ["Task Description"], MinOtherWords = 1 };

        Assert.Null(Check(feature, Rule(HygieneField.Description, HygieneCheck.NotOnlyWords, parameters)));
    }

    [Theory]
    [InlineData(34, "34 > 21")]
    [InlineData(21, null)]
    [InlineData(null, null)]
    public void NotGreaterThan_on_a_number_lets_empty_values_pass(int? storyPoints, string? expectedReason)
    {
        var feature = Feature();
        feature.StoryPoints = storyPoints;
        var parameters = new HygieneRuleParameters { Number = 21 };

        var failure = Check(feature, Rule(HygieneField.StoryPoints, HygieneCheck.NotGreaterThan, parameters));

        Assert.Equal(expectedReason, failure?.Reason);
    }

    [Theory]
    [InlineData(0, "0 < 1")]
    [InlineData(1, null)]
    [InlineData(null, null)]
    public void NotLessThan_on_a_number_lets_empty_values_pass(int? ranking, string? expectedReason)
    {
        var feature = Feature();
        feature.Ranking = ranking;
        var parameters = new HygieneRuleParameters { Number = 1 };

        var failure = Check(feature, Rule(HygieneField.Ranking, HygieneCheck.NotLessThan, parameters));

        Assert.Equal(expectedReason, failure?.Reason);
    }

    [Fact]
    public void Date_limits_compare_on_the_date_only()
    {
        var feature = Feature();
        feature.TargetEnd = new DateTime(2026, 12, 18, 23, 30, 0);
        var limit = new HygieneRuleParameters { Date = new DateOnly(2026, 12, 18) };

        Assert.Null(Check(feature, Rule(HygieneField.TargetEnd, HygieneCheck.NotGreaterThan, limit)));
        Assert.Null(Check(feature, Rule(HygieneField.TargetEnd, HygieneCheck.NotLessThan, limit)));

        feature.TargetEnd = new DateTime(2026, 12, 20);

        Assert.Equal("2026-12-20 is after 2026-12-18", Check(feature, Rule(HygieneField.TargetEnd, HygieneCheck.NotGreaterThan, limit))?.Reason);

        feature.TargetEnd = new DateTime(2026, 12, 1);

        Assert.Equal("2026-12-01 is before 2026-12-18", Check(feature, Rule(HygieneField.TargetEnd, HygieneCheck.NotLessThan, limit))?.Reason);

        feature.TargetEnd = null;

        Assert.Null(Check(feature, Rule(HygieneField.TargetEnd, HygieneCheck.NotGreaterThan, limit)));
    }

    [Fact]
    public void InValues_compares_by_name_ignoring_case_and_treats_empty_as_a_listed_value()
    {
        var feature = Feature();
        feature.TechnicalApproval = new TechnicalApproval { Id = 2, Name = "Required approve" };
        var approvedOnly = new HygieneRuleParameters { Values = ["approved"] };

        Assert.Equal("Required approve", Check(feature, Rule(HygieneField.TechnicalApproval, HygieneCheck.InValues, approvedOnly))?.Reason);

        feature.TechnicalApproval = new TechnicalApproval { Id = 1, Name = "Approved" };

        Assert.Null(Check(feature, Rule(HygieneField.TechnicalApproval, HygieneCheck.InValues, approvedOnly)));

        feature.TechnicalApproval = null;

        Assert.Equal("empty", Check(feature, Rule(HygieneField.TechnicalApproval, HygieneCheck.InValues, approvedOnly))?.Reason);

        var approvedOrEmpty = new HygieneRuleParameters { Values = ["Approved", HygieneRuleParameters.EmptyValue] };

        Assert.Null(Check(feature, Rule(HygieneField.TechnicalApproval, HygieneCheck.InValues, approvedOrEmpty)));
    }

    [Fact]
    public void NotInValues_fails_on_listed_values_including_empty()
    {
        var feature = Feature();
        feature.RequirementStatus = new RequirementStatus { Id = 1, Name = "test1" };
        var parameters = new HygieneRuleParameters { Values = [HygieneRuleParameters.EmptyValue, "test1"] };
        var rule = Rule(HygieneField.RequirementStatus, HygieneCheck.NotInValues, parameters);

        Assert.Equal("test1", Check(feature, rule)?.Reason);

        feature.RequirementStatus = null;

        Assert.Equal("empty", Check(feature, rule)?.Reason);

        feature.RequirementStatus = new RequirementStatus { Id = 2, Name = "Approved" };

        Assert.Null(Check(feature, rule));
    }

    [Fact]
    public void Flags_are_checked_with_IsTrue_and_IsFalse()
    {
        var feature = Feature();
        feature.ExternalDependencies = false;

        Assert.Equal("no", Check(feature, Rule(HygieneField.ExternalDependencies, HygieneCheck.IsTrue))?.Reason);
        Assert.Null(Check(feature, Rule(HygieneField.ExternalDependencies, HygieneCheck.IsFalse)));

        feature.ExternalDependencies = true;

        Assert.Null(Check(feature, Rule(HygieneField.ExternalDependencies, HygieneCheck.IsTrue)));
        Assert.Equal("yes", Check(feature, Rule(HygieneField.ExternalDependencies, HygieneCheck.IsFalse))?.Reason);
    }

    [Fact]
    public void Disabled_rules_and_checks_that_do_not_fit_the_field_are_skipped()
    {
        var feature = Feature();

        var failures = FeatureHygieneEvaluator.Evaluate(feature,
        [
            Rule(HygieneField.Description, HygieneCheck.NotEmpty, id: 1, enabled: false),
            Rule(HygieneField.StoryPoints, HygieneCheck.ContainsWords, new HygieneRuleParameters { Words = ["x"] }, id: 2),
            Rule(HygieneField.Summary, HygieneCheck.NotEmpty, id: 3)
        ]);

        Assert.Empty(failures);
    }

    [Fact]
    public void A_failure_carries_the_rule_text_the_message_and_the_actual_value()
    {
        var feature = Feature();
        feature.StoryPoints = 34;
        var rule = Rule(HygieneField.StoryPoints, HygieneCheck.NotGreaterThan, new HygieneRuleParameters { Number = 21 }, id: 9, message: "Split it");

        var failure = Check(feature, rule);

        Assert.NotNull(failure);
        Assert.Equal(9, failure.RuleId);
        Assert.Equal("Story points is not greater than 21", failure.RuleText);
        Assert.Equal("Split it", failure.Message);
        Assert.Equal("34", failure.ActualValue);
        Assert.Equal("Story points: 34 > 21", failure.Label);
    }

    [Fact]
    public void Evaluate_returns_one_failure_per_failed_rule_in_rule_order()
    {
        var feature = Feature();
        feature.Pi = new Pi { Id = 1, Name = "PI 26.2" };
        feature.PiId = 1;

        var failures = FeatureHygieneEvaluator.Evaluate(feature,
        [
            Rule(HygieneField.Description, HygieneCheck.NotEmpty, id: 1),
            Rule(HygieneField.Pi, HygieneCheck.NotEmpty, id: 2),
            Rule(HygieneField.Teams, HygieneCheck.NotEmpty, id: 3)
        ]);

        Assert.Equal([1, 3], failures.Select(f => f.RuleId));
    }

    [Fact]
    public void Business_outcome_actual_value_shows_jira_id_and_name()
    {
        var feature = Feature();
        feature.BusinessOutcomeId = 4;
        feature.BusinessOutcome = new BusinessOutcome { Id = 4, JiraId = "BO-4", Summary = "Capital adequacy" };

        Assert.Equal("BO-4 — Capital adequacy", HygieneFieldReader.Describe(feature, HygieneField.BusinessOutcome));
        Assert.Null(HygieneFieldReader.Describe(Feature(), HygieneField.BusinessOutcome));
    }
}
