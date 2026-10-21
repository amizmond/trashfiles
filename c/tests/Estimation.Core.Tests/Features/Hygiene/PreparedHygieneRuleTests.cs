using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Hygiene.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.Resources.Models;
using Xunit;

namespace Estimation.Core.Tests.Features.Hygiene;

public class PreparedHygieneRuleTests
{
    private static readonly DateOnly CheckDay = new(2026, 10, 14);

    private static FeatureHygieneRule Rule(
        int id,
        HygieneCondition own,
        HygieneRuleLogic? logic = null,
        string? message = null,
        bool enabled = true) =>
        new()
        {
            Id = id,
            CapitalProjectId = 1,
            Field = own.Field,
            Check = own.Check,
            ParametersJson = own.Parameters.ToJson(),
            LogicJson = logic?.ToJsonOrNull(),
            Message = message,
            IsEnabled = enabled
        };

    private static List<FeatureHygieneRule> Rules() =>
    [
        Rule(1, HygieneCondition.Of(HygieneField.Description, HygieneCheck.NotEmpty)),
        Rule(2, HygieneCondition.Of(HygieneField.Description, HygieneCheck.ContainsWords,
            new HygieneRuleParameters { Words = ["Acceptance criteria", "Result"] })),
        Rule(3, HygieneCondition.Of(HygieneField.AcceptanceCriteria, HygieneCheck.NotOnlyWords,
                new HygieneRuleParameters { Words = ["Acceptance criteria"], MinOtherWords = 3 }),
            new HygieneRuleLogic
            {
                When = [HygieneCondition.Of(HygieneField.Status, HygieneCheck.InValues, new HygieneRuleParameters { Values = ["In Progress"] })]
            }),
        Rule(4, HygieneCondition.Of(HygieneField.Description, HygieneCheck.NotEmpty),
            new HygieneRuleLogic
            {
                ThenLogic = HygieneLogic.Any,
                MoreThen = [HygieneCondition.Of(HygieneField.AcceptanceCriteria, HygieneCheck.NotEmpty)]
            },
            message: "  Describe the work somewhere  "),
        Rule(5, HygieneCondition.Of(HygieneField.TargetEnd, HygieneCheck.NotLessThan, new HygieneRuleParameters { Today = true })),
        Rule(6, HygieneCondition.Of(HygieneField.StoryPoints, HygieneCheck.NotGreaterThan, new HygieneRuleParameters { Number = 21 }), enabled: false),
        Rule(7, HygieneCondition.Of(HygieneField.Teams, HygieneCheck.PrimaryTeamMarked))
    ];

    private static Feature Feature(int id, string? description = null, string? criteria = null, string? status = null) =>
        new()
        {
            Id = id,
            JiraId = $"PAY-{id}",
            Summary = "A feature",
            Description = description,
            AcceptanceCriteria = criteria,
            Status = status
        };

    private static List<Feature> Features()
    {
        var shared = Feature(7, "h2. Result\nThe outcome", status: "In Progress");
        shared.AcceptanceCriteria = shared.Description;

        var teams = Feature(8, "Deliver X");
        teams.StoryPoints = 34;
        teams.FeatureTeams.Add(new FeatureTeam { TeamId = 1, Team = new Team { Id = 1, Name = "Gold" } });
        teams.FeatureTeams.Add(new FeatureTeam { TeamId = 2, Team = new Team { Id = 2, Name = "Hydrogen" } });

        return
        [
            Feature(1),
            Feature(2, "h2. Result", "{panel:title=Acceptance criteria}"),
            Feature(3, "h2. Acceptance criteria\nGiven a user\nh2. Result\nDone", "*Acceptance criteria*\nGiven, when, then", "In Progress"),
            Feature(4, "{panel:title=Acceptance criteria}Given, when, then{panel}", "Acceptance criteria only", "In Progress"),
            Feature(5, new string('x', 400), "   ", "Done"),
            Feature(6, null, "Given a user when they log in then they see it", "In Progress"),
            shared,
            teams
        ];
    }

    private static List<string> Lines(IReadOnlyList<HygieneFailure> failures) =>
        failures
            .Select(f => string.Join(" | ",
                f.RuleId,
                f.Field,
                f.Check,
                f.RuleText,
                f.Reason,
                f.Message ?? "<none>",
                f.ActualValue ?? "<none>",
                f.Display,
                string.Join(" ; ", f.Parts.Select(p => $"{p.Field} {p.Check} {p.Reason} {p.ActualValue ?? "<none>"}"))))
            .ToList();

    [Fact]
    public void Prepared_rules_give_the_same_failures_as_the_rules_they_were_prepared_from()
    {
        var rules = Rules();
        var prepared = FeatureHygieneEvaluator.Prepare(rules);

        foreach (var feature in Features())
        {
            foreach (var day in new[] { CheckDay.AddDays(-1), CheckDay, CheckDay.AddDays(1) })
            {
                feature.TargetEnd = CheckDay.ToDateTime(TimeOnly.MinValue);

                Assert.Equal(
                    Lines(FeatureHygieneEvaluator.Evaluate(feature, rules, day)),
                    Lines(FeatureHygieneEvaluator.Evaluate(feature, prepared, day)));

                foreach (var rule in rules)
                {
                    var single = PreparedHygieneRule.From(rule);

                    Assert.Equal(
                        Lines(FeatureHygieneEvaluator.Check(feature, rule, day) is { } a ? [a] : []),
                        Lines(FeatureHygieneEvaluator.Check(feature, single, day) is { } b ? [b] : []));
                    Assert.Equal(
                        FeatureHygieneEvaluator.Applies(feature, rule, day),
                        FeatureHygieneEvaluator.Applies(feature, single, day));
                }
            }
        }
    }

    [Fact]
    public void A_prepared_rule_carries_its_text_message_and_shape()
    {
        var prepared = FeatureHygieneEvaluator.Prepare(Rules());

        Assert.Equal([1, 2, 3, 4, 5, 6, 7], prepared.Select(r => r.Id));
        Assert.Equal("Description is not empty or Acceptance criteria is not empty", prepared[3].RuleText);
        Assert.Equal("Describe the work somewhere", prepared[3].Message);
        Assert.Equal(HygieneLogic.Any, prepared[3].ThenLogic);
        Assert.False(prepared[3].IsConditional);
        Assert.True(prepared[2].IsConditional);
        Assert.Equal(
            "If Jira status is one of In Progress, then Acceptance criteria has at least 3 other words besides Acceptance criteria",
            prepared[2].RuleText);
        Assert.Null(prepared[0].Message);
        Assert.False(prepared[5].IsEnabled);
        Assert.Equal(HygieneField.Teams, prepared[6].Field);
        Assert.Equal(HygieneCheck.PrimaryTeamMarked, prepared[6].Check);
    }

    [Fact]
    public void Disabled_prepared_rules_are_skipped_but_can_still_be_checked_on_their_own()
    {
        var feature = Feature(1, "Deliver X");
        feature.StoryPoints = 34;
        var disabled = PreparedHygieneRule.From(Rules()[5]);

        Assert.Empty(FeatureHygieneEvaluator.Evaluate(feature, [disabled], CheckDay));
        Assert.Equal("34 > 21", FeatureHygieneEvaluator.Check(feature, disabled, CheckDay)?.Reason);
    }

    [Fact]
    public void A_prepared_rule_set_is_a_snapshot_that_leaves_the_rules_untouched()
    {
        var rules = Rules();
        var parametersBefore = rules.Select(r => r.ParametersJson).ToList();
        var logicBefore = rules.Select(r => r.LogicJson).ToList();
        var prepared = FeatureHygieneEvaluator.Prepare(rules);
        var feature = Feature(1, "h2. Result\nThe outcome");

        Assert.Equal(parametersBefore, rules.Select(r => r.ParametersJson));
        Assert.Equal(logicBefore, rules.Select(r => r.LogicJson));

        rules[1].ParametersJson = new HygieneRuleParameters { Words = ["Result"] }.ToJson();

        Assert.DoesNotContain(FeatureHygieneEvaluator.Evaluate(feature, rules, CheckDay), f => f.RuleId == 2);
        Assert.Equal(
            "missing Acceptance criteria",
            Assert.Single(FeatureHygieneEvaluator.Evaluate(feature, prepared, CheckDay), f => f.RuleId == 2).Reason);
    }

    [Fact]
    public void A_prepared_today_limit_still_compares_with_the_day_each_evaluation_runs()
    {
        var prepared = FeatureHygieneEvaluator.Prepare([Rules()[4]]);
        var feature = Feature(1);
        feature.TargetEnd = new DateTime(2026, 10, 14, 23, 30, 0);

        Assert.Empty(FeatureHygieneEvaluator.Evaluate(feature, prepared, CheckDay));
        Assert.Equal(
            "2026-10-14 is before today",
            Assert.Single(FeatureHygieneEvaluator.Evaluate(feature, prepared, CheckDay.AddDays(1))).Reason);

        feature.TargetEnd = DateTime.Today.AddDays(-30);
        Assert.Single(FeatureHygieneEvaluator.Evaluate(feature, prepared));

        feature.TargetEnd = DateTime.Today.AddDays(30);
        Assert.Empty(FeatureHygieneEvaluator.Evaluate(feature, prepared));
    }

    [Fact]
    public void A_text_shared_by_two_fields_is_judged_like_two_copies_of_it()
    {
        var rules = FeatureHygieneEvaluator.Prepare(
        [
            Rule(1, HygieneCondition.Of(HygieneField.Description, HygieneCheck.NotEmpty)),
            Rule(2, HygieneCondition.Of(HygieneField.AcceptanceCriteria, HygieneCheck.NotEmpty)),
            Rule(3, HygieneCondition.Of(HygieneField.AcceptanceCriteria, HygieneCheck.ContainsWords, new HygieneRuleParameters { Words = ["Given"] })),
            Rule(4, HygieneCondition.Of(HygieneField.Description, HygieneCheck.IsEmpty))
        ]);

        const string headingOnly = "h2. Result";
        var shared = Feature(1, headingOnly);
        shared.AcceptanceCriteria = shared.Description;
        var copies = Feature(2, headingOnly, new string(headingOnly.AsSpan()));

        var sharedFailures = Lines(FeatureHygieneEvaluator.Evaluate(shared, rules, CheckDay));

        Assert.Equal(Lines(FeatureHygieneEvaluator.Evaluate(copies, rules, CheckDay)), sharedFailures);
        Assert.Equal(3, sharedFailures.Count);
        Assert.All(sharedFailures.Take(2), line => Assert.Contains("only a heading", line));
        Assert.Contains("missing Given", sharedFailures[2]);
    }

    [Fact]
    public void Prepared_rules_can_be_shared_by_concurrent_evaluations()
    {
        var prepared = FeatureHygieneEvaluator.Prepare(Rules());
        var features = Enumerable.Range(0, 40).SelectMany(_ => Features()).ToList();
        var expected = features.Select(f => Lines(FeatureHygieneEvaluator.Evaluate(f, prepared, CheckDay))).ToList();
        var actual = new List<string>[features.Count];

        Parallel.For(0, features.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            actual[i] = Lines(FeatureHygieneEvaluator.Evaluate(features[i], prepared, CheckDay)));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Only_an_enabled_all_parents_rule_needs_the_parent_chain_loaded()
    {
        var parents = Rule(8, HygieneCondition.Of(HygieneField.BusinessOutcome, HygieneCheck.ParentsExist));
        var switchedOff = Rule(9, HygieneCondition.Of(HygieneField.BusinessOutcome, HygieneCheck.ParentsExist), enabled: false);

        Assert.True(PreparedHygieneRule.From(parents).NeedsParents);
        Assert.All(FeatureHygieneEvaluator.Prepare(Rules()), r => Assert.False(r.NeedsParents));
        Assert.False(FeatureHygieneEvaluator.NeedsParents(FeatureHygieneEvaluator.Prepare(Rules())));
        Assert.False(FeatureHygieneEvaluator.NeedsParents(FeatureHygieneEvaluator.Prepare([.. Rules(), switchedOff])));
        Assert.True(FeatureHygieneEvaluator.NeedsParents(FeatureHygieneEvaluator.Prepare([.. Rules(), parents])));
    }
}
