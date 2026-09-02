using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Hygiene.Data;
using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Hygiene.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Features.Hygiene;

public class FeatureHygieneRuleServiceTests
{
    private const string Editor = "DOMAIN\\tester";

    private readonly InMemoryDatabase _db = new();
    private readonly FeatureHygieneRuleService _service;

    public FeatureHygieneRuleServiceTests()
    {
        _service = new FeatureHygieneRuleService(_db, new StubAuditUser(Editor));
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        private readonly string? _userName;

        public StubAuditUser(string? userName) => _userName = userName;

        public string? GetCurrentUserName() => _userName;
    }

    private static FeatureHygieneRule Rule(HygieneField field, HygieneCheck check, HygieneRuleParameters? parameters = null, int id = 0, bool enabled = true) =>
        new()
        {
            Id = id,
            Field = field,
            Check = check,
            ParametersJson = (parameters ?? new HygieneRuleParameters()).ToJson(),
            IsEnabled = enabled
        };

    [Fact]
    public async Task Saving_replaces_the_art_rule_set_and_keeps_the_order()
    {
        var first = await _service.SaveForArtAsync(1,
        [
            Rule(HygieneField.Summary, HygieneCheck.NotEmpty),
            Rule(HygieneField.Description, HygieneCheck.ContainsWords, new HygieneRuleParameters { Words = ["Task Description", " Result "] })
        ]);

        Assert.Equal(2, first.Count);
        Assert.Equal([0, 1], first.Select(r => r.SortOrder));
        Assert.All(first, r => Assert.Equal(Editor, r.CreatedBy));
        Assert.Equal(["Task Description", "Result"], first[1].Parameters.Words);

        var summaryId = first[0].Id;
        var descriptionId = first[1].Id;

        var second = await _service.SaveForArtAsync(1,
        [
            Rule(HygieneField.Description, HygieneCheck.ContainsWords, new HygieneRuleParameters { Words = ["Result"] }, id: descriptionId, enabled: false),
            Rule(HygieneField.StoryPoints, HygieneCheck.NotGreaterThan, new HygieneRuleParameters { Number = 21 })
        ]);

        Assert.Equal(2, second.Count);
        Assert.Equal(descriptionId, second[0].Id);
        Assert.False(second[0].IsEnabled);
        Assert.Equal(["Result"], second[0].Parameters.Words);
        Assert.Equal(Editor, second[0].ModifiedBy);
        Assert.NotNull(second[0].ModifiedAt);
        Assert.Equal(HygieneField.StoryPoints, second[1].Field);
        Assert.Equal(21, second[1].Parameters.Number);

        var stored = await _db.ReadAsync(db => db.FeatureHygieneRules().Select(r => r.Id).ToListAsync());
        Assert.DoesNotContain(summaryId, stored);
    }

    [Fact]
    public async Task Saving_keeps_only_the_parameters_the_check_uses()
    {
        var parameters = new HygieneRuleParameters
        {
            Words = ["x"],
            Number = 5,
            Date = new DateOnly(2026, 1, 1),
            Values = ["Approved"]
        };

        var saved = await _service.SaveForArtAsync(1, [Rule(HygieneField.TechnicalApproval, HygieneCheck.InValues, parameters)]);

        var stored = saved.Single().Parameters;
        Assert.Equal(["Approved"], stored.Values);
        Assert.Empty(stored.Words);
        Assert.Null(stored.Number);
        Assert.Null(stored.Date);
    }

    [Fact]
    public async Task An_incomplete_rule_is_rejected_before_anything_is_written()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveForArtAsync(1,
        [
            Rule(HygieneField.Summary, HygieneCheck.NotEmpty),
            Rule(HygieneField.Description, HygieneCheck.ContainsWords)
        ]));

        Assert.Contains("Rule 2", error.Message);
        Assert.Empty(await _service.GetForArtAsync(1));
    }

    [Fact]
    public async Task Rules_of_one_art_do_not_leak_into_another()
    {
        await _service.SaveForArtAsync(1, [Rule(HygieneField.Summary, HygieneCheck.NotEmpty)]);
        await _service.SaveForArtAsync(2, [Rule(HygieneField.Description, HygieneCheck.NotEmpty), Rule(HygieneField.Teams, HygieneCheck.NotEmpty)]);

        Assert.Single(await _service.GetForArtAsync(1));
        Assert.Equal(2, (await _service.GetForArtAsync(2)).Count);

        var byArt = await _service.GetAllByArtAsync();
        Assert.Equal(2, byArt.Count);
        Assert.Equal([1, 2], (await _service.GetArtIdsWithRulesAsync()).OrderBy(id => id));
    }

    [Fact]
    public async Task Copying_from_another_art_replaces_the_target_rules()
    {
        await _service.SaveForArtAsync(1,
        [
            Rule(HygieneField.Summary, HygieneCheck.NotEmpty),
            Rule(HygieneField.StoryPoints, HygieneCheck.NotGreaterThan, new HygieneRuleParameters { Number = 13 })
        ]);
        await _service.SaveForArtAsync(2, [Rule(HygieneField.Teams, HygieneCheck.NotEmpty)]);

        var copied = await _service.CopyFromArtAsync(1, 2);

        Assert.Equal(2, copied);
        var target = await _service.GetForArtAsync(2);
        Assert.Equal([HygieneField.Summary, HygieneField.StoryPoints], target.Select(r => r.Field));
        Assert.Equal(13, target[1].Parameters.Number);
        Assert.All(target, r => Assert.Equal(2, r.CapitalProjectId));
        Assert.Equal(0, await _service.CopyFromArtAsync(2, 2));
    }

    [Fact]
    public void Recommended_defaults_are_not_empty_checks_on_the_planning_fields()
    {
        var defaults = _service.RecommendedDefaults(5);

        Assert.All(defaults, r => Assert.Equal(HygieneCheck.NotEmpty, r.Check));
        Assert.All(defaults, r => Assert.Equal(5, r.CapitalProjectId));
        Assert.Contains(defaults, r => r.Field == HygieneField.BusinessOutcome);
        Assert.Contains(defaults, r => r.Field == HygieneField.Teams);
        Assert.Empty(defaults.SelectMany(HygieneRuleValidation.Problems));
    }

    [Fact]
    public async Task Choice_values_come_from_the_lookups_and_the_statuses_in_use()
    {
        await _db.SeedAsync(db =>
        {
            db.RequirementStatuses.Add(new RequirementStatus { Id = 1, Name = "Draft" });
            db.RequirementStatuses.Add(new RequirementStatus { Id = 2, Name = "Approved" });
            db.TechnicalApprovals.Add(new TechnicalApproval { Id = 2, Name = "Required approve", SortOrder = 20 });
            db.TechnicalApprovals.Add(new TechnicalApproval { Id = 1, Name = "Approved", SortOrder = 10 });
            db.UnfundedOptions.Add(new UnfundedOption { Id = 1, Name = "Funded", Order = 1 });
            db.Features.Add(new Feature { Id = 1, Summary = "a", Status = "To Do" });
            db.Features.Add(new Feature { Id = 2, Summary = "b", Status = "Backlog" });
            db.Features.Add(new Feature { Id = 3, Summary = "c", Status = "To Do " });
            db.Features.Add(new Feature { Id = 4, Summary = "d", Status = null });
        });

        var values = await _service.GetChoiceValuesAsync();

        Assert.Equal(["Approved", "Draft"], values.RequirementStatuses);
        Assert.Equal(["Approved", "Required approve"], values.TechnicalApprovals);
        Assert.Equal(["Funded"], values.FundingStatuses);
        Assert.Equal(["Backlog", "To Do"], values.JiraStatuses);
        Assert.Equal(values.JiraStatuses, values.For(HygieneField.Status));
        Assert.Empty(values.For(HygieneField.Summary));
    }
}
