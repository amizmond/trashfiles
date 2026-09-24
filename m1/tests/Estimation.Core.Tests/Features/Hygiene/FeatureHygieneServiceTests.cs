using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Hygiene.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Features.Hygiene;

public class FeatureHygieneServiceTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly FeatureHygieneRuleService _rules;
    private readonly FeatureHygieneService _service;

    public FeatureHygieneServiceTests()
    {
        _rules = new FeatureHygieneRuleService(_db, new StubAuditUser("DOMAIN\\tester"));
        _service = new FeatureHygieneService(_db, _rules);
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        private readonly string? _userName;

        public StubAuditUser(string? userName) => _userName = userName;

        public string? GetCurrentUserName() => _userName;
    }

    private static readonly Pi Pi1 = new() { Id = 1, Name = "PI 26.1" };
    private static readonly Pi Pi2 = new() { Id = 2, Name = "PI 26.2", FeatureLabels = "pi-26-2", LabelMatchMode = PiLabelMatchMode.Any };

    private async Task SeedArtsAndPisAsync()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY"));
            db.CapitalProjects.Add(Art(2, "Core ART", "CORE"));
            db.CapitalProjects.Add(Art(3, "No key ART"));
            db.Pis.Add(Pi1);
            db.Pis.Add(Pi2);
        });
    }

    private static Art Art(int id, string name, params string[] keys) =>
        new()
        {
            Id = id,
            Name = name,
            JiraKeys = keys.Select(k => new ArtJiraKey { JiraKey = k }).ToList()
        };

    private static Feature Feature(int id, string? projectKey, string jiraId, int? piId, string? description = null, string? labels = null, string? components = null) =>
        new()
        {
            Id = id,
            ProjectKey = projectKey,
            JiraId = jiraId,
            Summary = $"Feature {id}",
            PiId = piId,
            Description = description,
            Labels = labels,
            Components = components
        };

    private static FeatureHygieneRule NotEmpty(HygieneField field, int id = 0) => new()
    {
        Id = id,
        Field = field,
        Check = HygieneCheck.NotEmpty,
        ParametersJson = new HygieneRuleParameters().ToJson(),
        IsEnabled = true
    };

    [Fact]
    public async Task Only_features_of_the_art_and_the_pi_are_evaluated()
    {
        await SeedArtsAndPisAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY", "PAY-1", 2, description: "written"));
            db.Features.Add(Feature(2, null, "pay-2", 2));
            db.Features.Add(Feature(3, "CORE", "CORE-1", 2));
            db.Features.Add(Feature(4, "PAY", "PAY-4", 1));
            db.Features.Add(Feature(5, "PAY", "PAY-5", null, labels: "pi-26-2"));
            db.Features.Add(Feature(6, "PAY", "PAY-6", null));
        });
        await _rules.SaveForArtAsync(1, [NotEmpty(HygieneField.Description)]);

        var report = await _service.EvaluateAsync(1, 2);

        Assert.NotNull(report);
        Assert.Equal("Payments ART", report.ArtName);
        Assert.Equal("PAY", report.ArtJiraKeys);
        Assert.Equal("PI 26.2", report.PiName);
        Assert.Equal(["PAY-1", "pay-2", "PAY-5"], report.Rows.Select(r => r.JiraId));
        Assert.Equal(1, report.Healthy);
        Assert.Equal(2, report.Unhealthy);
        Assert.True(report.HasRules);

        var withoutLabels = await _service.EvaluateAsync(1, 2, includePiLabelMatches: false);

        Assert.Equal(["PAY-1", "pay-2"], withoutLabels!.Rows.Select(r => r.JiraId));
    }

    [Fact]
    public async Task Failure_counts_per_rule_and_per_feature_add_up()
    {
        await SeedArtsAndPisAsync();
        await _db.SeedAsync(db =>
        {
            db.Features.Add(Feature(1, "PAY", "PAY-1", 1, description: "written"));
            db.Features.Add(Feature(2, "PAY", "PAY-2", 1));
        });
        var saved = await _rules.SaveForArtAsync(1, [NotEmpty(HygieneField.Description), NotEmpty(HygieneField.StoryPoints), NotEmpty(HygieneField.Teams)]);

        var report = await _service.EvaluateAsync(1, 1);

        Assert.NotNull(report);
        Assert.Equal(1, report.FailureCount(saved[0].Id));
        Assert.Equal(2, report.FailureCount(saved[1].Id));
        Assert.Equal(2, report.FailureCount(saved[2].Id));
        Assert.Equal(2, report.Rows.Single(r => r.JiraId == "PAY-1").Failures.Count);
        Assert.Equal(3, report.Rows.Single(r => r.JiraId == "PAY-2").Failures.Count);
        Assert.Equal(0, report.Healthy);
    }

    [Fact]
    public async Task An_unknown_art_or_pi_gives_no_report_and_an_art_without_rules_has_no_rules()
    {
        await SeedArtsAndPisAsync();
        await _db.SeedAsync(db => db.Features.Add(Feature(1, "PAY", "PAY-1", 1)));

        Assert.Null(await _service.EvaluateAsync(99, 1));
        Assert.Null(await _service.EvaluateAsync(1, 99));

        var report = await _service.EvaluateAsync(1, 1);

        Assert.NotNull(report);
        Assert.False(report.HasRules);
        Assert.Single(report.Rows);
        Assert.True(report.Rows[0].IsHealthy);
    }

    [Fact]
    public async Task An_art_without_a_jira_key_has_no_features()
    {
        await SeedArtsAndPisAsync();
        await _db.SeedAsync(db => db.Features.Add(Feature(1, "PAY", "PAY-1", 1)));

        var report = await _service.EvaluateAsync(3, 1);

        Assert.NotNull(report);
        Assert.Empty(report.Rows);
    }

    [Fact]
    public async Task Rules_by_art_id_hold_only_enabled_rules_of_arts_that_have_some()
    {
        await SeedArtsAndPisAsync();
        await _rules.SaveForArtAsync(1, [NotEmpty(HygieneField.Description), new FeatureHygieneRule
        {
            Field = HygieneField.Summary,
            Check = HygieneCheck.NotEmpty,
            ParametersJson = "{}",
            IsEnabled = false
        }]);
        await _rules.SaveForArtAsync(2, [new FeatureHygieneRule
        {
            Field = HygieneField.Summary,
            Check = HygieneCheck.NotEmpty,
            ParametersJson = "{}",
            IsEnabled = false
        }]);
        await _rules.SaveForArtAsync(3, [NotEmpty(HygieneField.Description)]);

        var byArt = await _service.GetRulesByArtIdAsync();

        Assert.Equal([1, 3], byArt.Keys.Order());
        Assert.Equal(HygieneField.Description, Assert.Single(byArt[1]).Field);
    }

    [Fact]
    public async Task A_feature_of_a_multi_key_art_gets_the_rules_of_its_art()
    {
        await SeedArtsAndPisAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(4, "Cards ART", "CARD", "DEBIT"));
            db.Features.Add(Feature(1, "CARD", "CARD-1", 1));
            db.Features.Add(Feature(2, null, "debit-2", 1, description: "written"));
            db.Features.Add(Feature(3, "PAY", "PAY-3", 1));
        });
        await _rules.SaveForArtAsync(4, [NotEmpty(HygieneField.Description)]);

        var failures = await _service.GetFailuresByFeatureAsync([1, 2, 3]);
        var report = await _service.EvaluateAsync(4, 1);

        Assert.Single(failures[1]);
        Assert.Empty(failures[2]);
        Assert.False(failures.ContainsKey(3));
        Assert.NotNull(report);
        Assert.Equal("CARD, DEBIT", report.ArtJiraKeys);
        Assert.Equal(["CARD-1", "debit-2"], report.Rows.Select(r => r.JiraId));
        Assert.Equal(1, report.Unhealthy);
    }

    [Fact]
    public async Task A_feature_on_a_key_shared_by_two_arts_gets_no_rules()
    {
        await SeedArtsAndPisAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(5, "Payments copy ART", "PAY"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", 1));
            db.Features.Add(Feature(2, "CORE", "CORE-2", 1));
        });
        await _rules.SaveForArtAsync(1, [NotEmpty(HygieneField.Description)]);
        await _rules.SaveForArtAsync(2, [NotEmpty(HygieneField.Description)]);
        await _rules.SaveForArtAsync(5, [NotEmpty(HygieneField.Description)]);

        var failures = await _service.GetFailuresByFeatureAsync([1, 2]);

        Assert.False(failures.ContainsKey(1));
        Assert.Single(failures[2]);
        Assert.Empty((await _service.EvaluateAsync(1, 1))!.Rows);
        Assert.Empty((await _service.EvaluateAsync(5, 1))!.Rows);
    }

    [Fact]
    public async Task A_feature_on_a_shared_key_gets_the_rules_of_the_art_whose_components_and_labels_accept_it()
    {
        await SeedArtsAndPisAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art
            {
                Id = 6,
                Name = "Web checkout ART",
                JiraKeys = [new ArtJiraKey { JiraKey = "PAY", Components = "Checkout", Labels = "web, mobile" }]
            });
            db.Features.Add(Feature(1, "PAY", "PAY-1", 1, labels: "WEB", components: "checkout"));
            db.Features.Add(Feature(2, "PAY", "PAY-2", 1, labels: "web"));
            db.Features.Add(Feature(3, "PAY", "PAY-3", 1, description: "written", components: "Checkout"));
        });
        await _rules.SaveForArtAsync(1, [NotEmpty(HygieneField.Description)]);
        await _rules.SaveForArtAsync(6, [NotEmpty(HygieneField.Description), NotEmpty(HygieneField.StoryPoints)]);

        var failures = await _service.GetFailuresByFeatureAsync([1, 2, 3]);
        var checkout = await _service.EvaluateAsync(6, 1);
        var payments = await _service.EvaluateAsync(1, 1);

        Assert.Equal(2, failures[1].Count);
        Assert.Single(failures[2]);
        Assert.Empty(failures[3]);
        Assert.Equal(["PAY-1"], checkout!.Rows.Select(r => r.JiraId));
        Assert.Equal(["PAY-2", "PAY-3"], payments!.Rows.Select(r => r.JiraId));
    }
}
