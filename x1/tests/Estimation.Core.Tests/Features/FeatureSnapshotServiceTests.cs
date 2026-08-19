using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureSnapshotServiceTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly FeatureSnapshotService _service;

    public FeatureSnapshotServiceTests()
    {
        _service = new FeatureSnapshotService(_db, new StubAuditUser("DOMAIN\\tester"));
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        private readonly string? _userName;

        public StubAuditUser(string? userName) => _userName = userName;

        public string? GetCurrentUserName() => _userName;
    }

    private static readonly FeatureSnapshotTarget PaymentsPi1 = new("Payments ART", "PAY", 1, "PI 26.1");

    private static Feature Feature(
        int id,
        string projectKey,
        string jiraId,
        Pi? pi = null,
        string? labels = null,
        string summary = "A feature") =>
        new()
        {
            Id = id,
            ProjectKey = projectKey,
            JiraId = jiraId,
            Summary = summary,
            Labels = labels,
            Pi = pi
        };

    [Fact]
    public async Task Only_features_of_the_selected_art_are_captured()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(Feature(2, "CORE", "CORE-1", pi));
        });

        var items = await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: true);

        Assert.Equal("PAY-1", Assert.Single(items).JiraId);
    }

    [Fact]
    public async Task The_art_is_resolved_from_the_jira_id_when_the_project_key_is_missing()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(new Feature { Id = 1, JiraId = "PAY-7", Summary = "No project key", Pi = pi });
        });

        var items = await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: true);

        Assert.Equal("PAY-7", Assert.Single(items).JiraId);
    }

    [Fact]
    public async Task Only_features_of_the_selected_pi_are_captured()
    {
        var pi1 = new Pi { Id = 1, Name = "PI 26.1" };
        var pi2 = new Pi { Id = 2, Name = "PI 26.2" };

        await _db.SeedAsync(db =>
        {
            db.Pis.AddRange(pi1, pi2);
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi1));
            db.Features.Add(Feature(2, "PAY", "PAY-2", pi2));
            db.Features.Add(Feature(3, "PAY", "PAY-3"));
        });

        var items = await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: true);

        Assert.Equal("PAY-1", Assert.Single(items).JiraId);
    }

    [Fact]
    public async Task Features_matched_by_a_pi_label_rule_are_captured()
    {
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1", FeatureLabels = "pi26.1", LabelMatchMode = PiLabelMatchMode.Any });
            db.Features.Add(Feature(1, "PAY", "PAY-1", labels: "pi26.1, payments"));
        });

        var items = await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: true);

        Assert.Equal("PAY-1", Assert.Single(items).JiraId);
    }

    [Fact]
    public async Task Label_matches_are_skipped_when_label_matching_is_off()
    {
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1", FeatureLabels = "pi26.1", LabelMatchMode = PiLabelMatchMode.Any });
            db.Features.Add(Feature(1, "PAY", "PAY-1", labels: "pi26.1"));
        });

        var items = await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: false);

        Assert.Empty(items);
    }

    [Fact]
    public async Task The_all_label_mode_requires_every_label()
    {
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1", FeatureLabels = "pi26.1, funded", LabelMatchMode = PiLabelMatchMode.All });
            db.Features.Add(Feature(1, "PAY", "PAY-1", labels: "pi26.1"));
            db.Features.Add(Feature(2, "PAY", "PAY-2", labels: "pi26.1, funded"));
        });

        var items = await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: true);

        Assert.Equal("PAY-2", Assert.Single(items).JiraId);
    }

    [Fact]
    public async Task Captured_values_are_stored_as_text_not_as_references()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };
        var team = new Team { Id = 1, Name = "Ledger" };
        var otherTeam = new Team { Id = 2, Name = "Consent" };
        var status = new RequirementStatus { Id = 1, Name = "Committed" };
        var funding = new UnfundedOption { Id = 1, Name = "Funded" };
        var outcome = new BusinessOutcome { Id = 1, JiraId = "BO-3", Summary = "Cost to serve" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Teams.AddRange(team, otherTeam);
            db.RequirementStatuses.Add(status);
            db.UnfundedOptions.Add(funding);
            db.BusinessOutcomes.Add(outcome);

            var feature = Feature(1, "PAY", "PAY-1", pi, labels: "alpha");
            feature.Name = "Ledger rework";
            feature.AcceptanceCriteria = "Given a payment, it settles";
            feature.StoryPoints = 8;
            feature.TargetStart = new DateTime(2026, 8, 17);
            feature.TargetEnd = new DateTime(2026, 9, 14);
            feature.RequirementStatus = status;
            feature.UnfundedOption = funding;
            feature.BusinessOutcome = outcome;
            feature.RagExplain = "Amber: vendor dependency";
            feature.PiObjective = new PiObjective { Id = 1, Name = "Reduce cost to serve" };
            feature.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = 1, Team = team });
            feature.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = 2, Team = otherTeam });

            db.Features.Add(feature);
        });

        var item = Assert.Single(await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: true));

        Assert.Equal("Payments ART", item.ArtName);
        Assert.Equal("PI 26.1", item.PiName);
        Assert.Equal("alpha", item.Labels);
        Assert.Equal("BO-3", item.BusinessOutcomeJiraId);
        Assert.Equal("Cost to serve", item.BusinessOutcomeName);
        Assert.Equal(new DateTime(2026, 8, 17), item.TargetStart);
        Assert.Equal(new DateTime(2026, 9, 14), item.TargetEnd);
        Assert.Equal(8, item.StoryPoints);
        Assert.Equal("Consent, Ledger", item.Teams);
        Assert.Equal("Committed", item.RequirementStatus);
        Assert.Equal("Funded", item.FundingStatus);
        Assert.Equal("A feature", item.Summary);
        Assert.Equal("Ledger rework", item.Name);
        Assert.Equal("Given a payment, it settles", item.AcceptanceCriteria);
        Assert.Equal("Reduce cost to serve", item.PiObjective);
        Assert.Equal("Amber: vendor dependency", item.RagExplain);
    }

    [Fact]
    public async Task A_created_snapshot_keeps_its_values_after_the_feature_changes()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi, summary: "Original summary"));
        });

        var snapshot = await _service.CreateAsync(PaymentsPi1, includePiLabelMatches: true);

        await using (var db = _db.CreateDbContext())
        {
            var feature = db.Features.Single(f => f.Id == 1);
            feature.Summary = "Renamed summary";
            await db.SaveChangesAsync();
        }

        var items = await _service.GetItemsAsync(snapshot.Id);

        Assert.Equal(1, snapshot.FeatureCount);
        Assert.Equal("DOMAIN\\tester", snapshot.CreatedBy);
        Assert.Equal("Original summary", Assert.Single(items).Summary);
    }

    [Fact]
    public async Task A_snapshot_is_timestamped_in_utc()
    {
        await _db.SeedAsync(db => db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1" }));

        var before = DateTime.UtcNow;
        var snapshot = await _service.CreateAsync(PaymentsPi1, includePiLabelMatches: true);
        var after = DateTime.UtcNow;

        Assert.InRange(snapshot.CreatedAt, before, after);
    }

    [Fact]
    public async Task A_pi_lock_baseline_is_timestamped_in_utc()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Payments ART", JiraKey = "PAY" });
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        var before = DateTime.UtcNow;
        var created = Assert.Single(await _service.CreateForLockedPiAsync("PI 26.1"));
        var after = DateTime.UtcNow;

        Assert.InRange(created.CreatedAt, before, after);
    }

    [Fact]
    public async Task The_snapshot_name_is_stored_when_it_is_given()
    {
        await _db.SeedAsync(db => db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1" }));

        var snapshot = await _service.CreateAsync(
            PaymentsPi1 with { Name = "  Before the scope cut  " },
            includePiLabelMatches: true);

        Assert.Equal("Before the scope cut", snapshot.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_snapshot_name_is_stored_as_null(string? name)
    {
        await _db.SeedAsync(db => db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1" }));

        var snapshot = await _service.CreateAsync(PaymentsPi1 with { Name = name }, includePiLabelMatches: true);

        Assert.Null(snapshot.Name);
    }

    [Fact]
    public async Task A_snapshot_survives_the_deletion_of_its_feature()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        var snapshot = await _service.CreateAsync(PaymentsPi1, includePiLabelMatches: true);

        await using (var db = _db.CreateDbContext())
        {
            db.Features.Remove(db.Features.Single(f => f.Id == 1));
            await db.SaveChangesAsync();
        }

        var items = await _service.GetItemsAsync(snapshot.Id);

        Assert.Equal("PAY-1", Assert.Single(items).JiraId);
    }

    [Fact]
    public async Task Deleting_a_snapshot_removes_its_items()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        var snapshot = await _service.CreateAsync(PaymentsPi1, includePiLabelMatches: true);

        Assert.True(await _service.DeleteAsync(snapshot.Id));
        Assert.Empty(await _service.GetItemsAsync(snapshot.Id));
        Assert.Empty(await _service.GetAllAsync());
    }

    [Fact]
    public async Task Locking_a_pi_captures_one_snapshot_per_art_that_has_features()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Payments ART", JiraKey = "PAY" });
            db.CapitalProjects.Add(new CapitalProject { Id = 2, Name = "Core Banking ART", JiraKey = "CORE" });
            db.CapitalProjects.Add(new CapitalProject { Id = 3, Name = "Idle ART", JiraKey = "IDLE" });
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(Feature(2, "PAY", "PAY-2", pi));
            db.Features.Add(Feature(3, "CORE", "CORE-1", pi));
        });

        var created = await _service.CreateForLockedPiAsync("PI 26.1");

        Assert.Equal(2, created.Count);
        Assert.All(created, s => Assert.True(s.IsAutomatic));
        Assert.Equal(2, created.Single(s => s.ArtName == "Payments ART").FeatureCount);
        Assert.Equal(1, created.Single(s => s.ArtName == "Core Banking ART").FeatureCount);
        Assert.DoesNotContain(created, s => s.ArtName == "Idle ART");
    }

    [Fact]
    public async Task Locking_a_pi_captures_features_matched_by_its_label_rules()
    {
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1", FeatureLabels = "pi26.1", LabelMatchMode = PiLabelMatchMode.Any });
            db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Payments ART", JiraKey = "PAY" });
            db.Features.Add(Feature(1, "PAY", "PAY-1", labels: "pi26.1"));
        });

        var created = await _service.CreateForLockedPiAsync("PI 26.1");

        Assert.Equal(1, Assert.Single(created).FeatureCount);
    }

    [Fact]
    public async Task Relocking_a_pi_does_not_capture_a_second_baseline()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Payments ART", JiraKey = "PAY" });
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        Assert.Single(await _service.CreateForLockedPiAsync("PI 26.1"));
        Assert.Empty(await _service.CreateForLockedPiAsync("PI 26.1"));
        Assert.Single(await _service.GetAllAsync());
    }

    [Fact]
    public async Task A_manual_snapshot_does_not_block_the_lock_baseline()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Payments ART", JiraKey = "PAY" });
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        await _service.CreateAsync(PaymentsPi1, includePiLabelMatches: true);

        var created = Assert.Single(await _service.CreateForLockedPiAsync("PI 26.1"));

        Assert.True(created.IsAutomatic);
        Assert.Equal(2, (await _service.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Locking_a_pi_with_no_matching_features_captures_nothing()
    {
        var otherPi = new Pi { Id = 2, Name = "PI 26.2" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1" });
            db.Pis.Add(otherPi);
            db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Payments ART", JiraKey = "PAY" });
            db.Features.Add(Feature(1, "PAY", "PAY-1", otherPi));
        });

        Assert.Empty(await _service.CreateForLockedPiAsync("PI 26.1"));
    }

    [Fact]
    public async Task A_manual_snapshot_is_not_marked_automatic()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        Assert.False((await _service.CreateAsync(PaymentsPi1, includePiLabelMatches: true)).IsAutomatic);
    }

    [Fact]
    public async Task An_art_without_a_jira_key_captures_nothing()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        var items = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget("Payments ART", null, 1, "PI 26.1"),
            includePiLabelMatches: true);

        Assert.Empty(items);
    }
}
