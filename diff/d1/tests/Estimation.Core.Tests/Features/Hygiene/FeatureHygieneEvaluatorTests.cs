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

    private static FeatureTeam Team(int id, string name, int? storyPoints = null, bool? primary = null, params int?[] stackPoints) =>
        new()
        {
            TeamId = id,
            Team = new Team { Id = id, Name = name },
            StoryPoints = storyPoints,
            IsPrimary = primary,
            TechnologyStacks = stackPoints.Select(sp => new FeatureTeamTechnologyStack { StoryPoints = sp }).ToList()
        };

    private static Feature Feature() => new() { Id = 1, JiraId = "PAY-1", Summary = "A feature" };

    private static HygieneFailure? Check(Feature feature, FeatureHygieneRule rule) =>
        FeatureHygieneEvaluator.Check(feature, rule);

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("   ", "empty")]
    [InlineData("h2. \n{panel}{panel}", "empty")]
    [InlineData("h2. Result", "only a heading")]
    [InlineData("{panel:title=Acceptance criteria}", "only a heading")]
    [InlineData("h2. Result\nThe outcome", null)]
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
    public void A_failure_carries_the_rule_text_the_actual_value_and_shows_the_label_without_a_message()
    {
        var feature = Feature();
        feature.StoryPoints = 34;
        var rule = Rule(HygieneField.StoryPoints, HygieneCheck.NotGreaterThan, new HygieneRuleParameters { Number = 21 }, id: 9);

        var failure = Check(feature, rule);

        Assert.NotNull(failure);
        Assert.Equal(9, failure.RuleId);
        Assert.Equal("Story points is not greater than 21", failure.RuleText);
        Assert.Equal("34", failure.ActualValue);
        Assert.Equal("Story points: 34 > 21", failure.Label);
        Assert.Null(failure.Message);
        Assert.Equal(failure.Label, failure.Display);
    }

    [Fact]
    public void A_rule_with_a_message_shows_the_message_and_keeps_the_technical_label()
    {
        var feature = Feature();
        feature.StoryPoints = 34;
        var rule = Rule(HygieneField.StoryPoints, HygieneCheck.NotGreaterThan, new HygieneRuleParameters { Number = 21 },
            message: "  Split the feature, 21 points is the most a PI takes  ");

        var failure = Check(feature, rule);

        Assert.NotNull(failure);
        Assert.Equal("Split the feature, 21 points is the most a PI takes", failure.Message);
        Assert.Equal(failure.Message, failure.Display);
        Assert.Equal("Story points: 34 > 21", failure.Label);
    }

    [Fact]
    public void Shared_features_need_a_primary_team()
    {
        var feature = Feature();
        var rule = Rule(HygieneField.Teams, HygieneCheck.PrimaryTeamMarked, id: 11);

        Assert.Null(Check(feature, rule));

        feature.FeatureTeams.Add(Team(1, "Gold"));

        Assert.Null(Check(feature, rule));

        feature.FeatureTeams.Add(Team(2, "Hydrogen"));

        var failure = Check(feature, rule);
        Assert.NotNull(failure);
        Assert.Equal("2 teams, none marked primary", failure.Reason);
        Assert.Equal("Shared features have a primary team", failure.RuleText);
        Assert.Equal("Gold, Hydrogen", failure.ActualValue);

        feature.FeatureTeams[1].IsPrimary = true;

        Assert.Null(Check(feature, rule));
    }

    [Fact]
    public void Story_points_must_match_the_teams_total_the_way_the_feature_page_computes_it()
    {
        var feature = Feature();
        var rule = Rule(HygieneField.StoryPoints, HygieneCheck.StoryPointsMatchTeams, id: 12);

        Assert.Null(Check(feature, rule));

        feature.StoryPoints = 8;
        feature.FeatureTeams.Add(Team(1, "Gold", storyPoints: 5));
        feature.FeatureTeams.Add(Team(2, "Hydrogen", storyPoints: 3));

        Assert.Null(Check(feature, rule));

        feature.StoryPoints = 13;

        var failure = Check(feature, rule);
        Assert.NotNull(failure);
        Assert.Equal("13 ≠ teams total 8", failure.Reason);
        Assert.Equal("Story points match the teams total", failure.RuleText);

        feature.FeatureTeams[0] = Team(1, "Gold", storyPoints: 5, primary: null, 6, 4);

        Assert.Null(Check(feature, rule));

        feature.StoryPoints = null;

        Assert.Equal("0 ≠ teams total 13", Check(feature, rule)?.Reason);
    }

    [Fact]
    public void Specific_rules_are_not_offered_in_the_table_but_are_allowed_on_their_field()
    {
        Assert.DoesNotContain(HygieneCheck.PrimaryTeamMarked, HygieneChecks.AllowedFor(HygieneFieldKind.Reference));
        Assert.DoesNotContain(HygieneCheck.StoryPointsMatchTeams, HygieneChecks.AllowedFor(HygieneFieldKind.Number));
        Assert.True(HygieneChecks.IsAllowed(HygieneField.Teams, HygieneCheck.PrimaryTeamMarked));
        Assert.True(HygieneChecks.IsAllowed(HygieneField.StoryPoints, HygieneCheck.StoryPointsMatchTeams));
        Assert.False(HygieneChecks.IsAllowed(HygieneField.Summary, HygieneCheck.PrimaryTeamMarked));
        Assert.Empty(HygieneRuleValidation.Problems(HygieneField.Teams, HygieneCheck.PrimaryTeamMarked, new HygieneRuleParameters()));
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
    public void ContainsWords_ignores_letter_case_unless_the_rule_asks_otherwise()
    {
        var feature = Feature();
        feature.Description = "h2. result\nDone.";

        var insensitive = new HygieneRuleParameters { Words = ["Result"] };
        var sensitive = new HygieneRuleParameters { Words = ["Result"], CaseSensitive = true };

        Assert.Null(Check(feature, Rule(HygieneField.Description, HygieneCheck.ContainsWords, insensitive)));
        Assert.Equal("missing Result", Check(feature, Rule(HygieneField.Description, HygieneCheck.ContainsWords, sensitive))?.Reason);

        feature.Description = "h2. Result\nDone.";

        Assert.Null(Check(feature, Rule(HygieneField.Description, HygieneCheck.ContainsWords, sensitive)));
    }

    [Fact]
    public void NotOnlyWords_counts_the_headings_it_was_told_to_ignore_by_case_when_asked()
    {
        var feature = Feature();
        feature.Description = "h2. result";

        var insensitive = new HygieneRuleParameters { Words = ["Result"], MinOtherWords = 1 };
        var sensitive = new HygieneRuleParameters { Words = ["Result"], MinOtherWords = 1, CaseSensitive = true };

        Assert.Equal("no other words", Check(feature, Rule(HygieneField.Description, HygieneCheck.NotOnlyWords, insensitive))?.Reason);

        Assert.Null(Check(feature, Rule(HygieneField.Description, HygieneCheck.NotOnlyWords, sensitive)));
    }

    [Fact]
    public void A_case_sensitive_rule_says_so_and_keeps_phrases_that_differ_only_in_case()
    {
        var parameters = new HygieneRuleParameters { Words = ["Result", "result"], CaseSensitive = true };

        Assert.Equal(["Result", "result"], parameters.CleanWords);
        Assert.Equal(
            "Description contains Result, result (all, case-sensitive)",
            HygieneRuleText.Describe(HygieneField.Description, HygieneCheck.ContainsWords, parameters));

        var insensitive = new HygieneRuleParameters { Words = ["Result", "result"] };

        Assert.Equal(["Result"], insensitive.CleanWords);
        Assert.Equal(
            "Description contains Result",
            HygieneRuleText.Describe(HygieneField.Description, HygieneCheck.ContainsWords, insensitive));
    }

    [Fact]
    public void Case_sensitivity_survives_the_json_and_only_where_it_is_read()
    {
        var parameters = new HygieneRuleParameters { Words = ["Result"], CaseSensitive = true, Number = 3 };

        var contains = parameters.ForCheck(HygieneCheck.ContainsWords, HygieneFieldKind.Text);
        var notOnly = parameters.ForCheck(HygieneCheck.NotOnlyWords, HygieneFieldKind.Text);
        var notEmpty = parameters.ForCheck(HygieneCheck.NotEmpty, HygieneFieldKind.Text);

        Assert.True(contains.CaseSensitive);
        Assert.True(notOnly.CaseSensitive);
        Assert.False(notEmpty.CaseSensitive);
        Assert.True(HygieneRuleParameters.Parse(contains.ToJson()).CaseSensitive);
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

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("R26.3,R26.4", null)]
    public void Fix_versions_can_be_required(string? fixVersions, string? expectedReason)
    {
        var feature = Feature();
        feature.FixVersions = fixVersions;

        var failure = Check(feature, Rule(HygieneField.FixVersions, HygieneCheck.NotEmpty));

        Assert.Equal(expectedReason, failure?.Reason);
    }

    [Fact]
    public void Fix_versions_can_be_checked_for_a_release()
    {
        var feature = Feature();
        feature.FixVersions = "R26.3,R26.4";
        var parameters = new HygieneRuleParameters { Words = ["R26.4"], Mode = HygieneWordMode.Or };

        Assert.Null(Check(feature, Rule(HygieneField.FixVersions, HygieneCheck.ContainsWords, parameters)));
        Assert.Equal("Fix versions", HygieneFieldCatalog.DisplayName(HygieneField.FixVersions));
        Assert.Equal(HygieneFieldKind.Text, HygieneFieldCatalog.KindOf(HygieneField.FixVersions));
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("Green", null)]
    public void The_rag_status_can_be_required(string? ragStatus, string? expectedReason)
    {
        var feature = Feature();
        feature.RagStatus = ragStatus;

        var failure = Check(feature, Rule(HygieneField.RagStatus, HygieneCheck.NotEmpty));

        Assert.Equal(expectedReason, failure?.Reason);
    }

    [Fact]
    public void The_rag_status_can_be_limited_to_some_colours()
    {
        var feature = Feature();
        var greenOrAmber = new HygieneRuleParameters { Values = ["Green", "Amber"] };

        feature.RagStatus = "Red";
        Assert.Equal("Red", Check(feature, Rule(HygieneField.RagStatus, HygieneCheck.InValues, greenOrAmber))?.Reason);
        Assert.Null(Check(feature, Rule(HygieneField.RagStatus, HygieneCheck.NotInValues, greenOrAmber)));

        feature.RagStatus = "Amber";
        Assert.Null(Check(feature, Rule(HygieneField.RagStatus, HygieneCheck.InValues, greenOrAmber)));
        Assert.Equal("Amber", HygieneFieldReader.Describe(feature, HygieneField.RagStatus));

        Assert.Equal("RAG status", HygieneFieldCatalog.DisplayName(HygieneField.RagStatus));
        Assert.Equal(HygieneFieldKind.Choice, HygieneFieldCatalog.KindOf(HygieneField.RagStatus));
        Assert.Equal(new[] { "Green", "Amber", "Red" }, HygieneChoiceValues.Empty.For(HygieneField.RagStatus));
    }
}
