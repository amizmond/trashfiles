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

    private static Art Art(int id, string name, params string[] keys) =>
        new()
        {
            Id = id,
            Name = name,
            JiraKeys = keys.Select(k => new ArtJiraKey { JiraKey = k }).ToList()
        };

    private static Art Payments() => Art(1, "Payments ART", "PAY");

    private static Feature Feature(
        int id,
        string projectKey,
        string jiraId,
        Pi? pi = null,
        string? labels = null,
        string summary = "A feature",
        string? components = null) =>
        new()
        {
            Id = id,
            ProjectKey = projectKey,
            JiraId = jiraId,
            Summary = summary,
            Labels = labels,
            Components = components,
            Pi = pi
        };

    [Fact]
    public async Task Only_features_of_the_selected_art_are_captured()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Payments());
            db.CapitalProjects.Add(Art(2, "Core ART", "CORE"));
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
            db.CapitalProjects.Add(Art(2, "Core Banking ART", "CORE"));
            db.CapitalProjects.Add(Art(3, "Idle ART", "IDLE"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(Feature(2, "PAY", "PAY-2", pi));
            db.Features.Add(Feature(3, "CORE", "CORE-1", pi));
        });

        var created = await _service.CreateForLockedPiAsync("PI 26.1");

        Assert.Equal(2, created.Count);
        Assert.All(created, s => Assert.True(s.IsAutomatic));
        Assert.Equal(2, created.Single(s => s.ArtName == "Payments ART").FeatureCount);
        Assert.Equal(1, created.Single(s => s.ArtName == "Core Banking ART").FeatureCount);
        Assert.Equal("CORE", created.Single(s => s.ArtName == "Core Banking ART").ArtJiraKeys);
        Assert.DoesNotContain(created, s => s.ArtName == "Idle ART");
    }

    [Fact]
    public async Task Locking_a_pi_captures_features_matched_by_its_label_rules()
    {
        await _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = 1, Name = "PI 26.1", FeatureLabels = "pi26.1", LabelMatchMode = PiLabelMatchMode.Any });
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Payments());
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
            db.CapitalProjects.Add(Art(1, "Payments ART"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        var items = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget("Payments ART", null, 1, "PI 26.1"),
            includePiLabelMatches: true);

        Assert.Empty(items);
    }

    [Fact]
    public async Task A_target_without_an_art_or_keys_captures_nothing()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
        });

        var items = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget("Payments ART", null, null, "PI 26.1"),
            includePiLabelMatches: true);

        Assert.Empty(items);
    }

    [Fact]
    public async Task A_snapshot_of_a_multi_key_art_captures_the_features_of_every_key()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY", "CARD"));
            db.CapitalProjects.Add(Art(2, "Core ART", "CORE"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(new Feature { Id = 2, JiraId = "card-2", Summary = "No project key", Pi = pi });
            db.Features.Add(Feature(3, "CORE", "CORE-3", pi));
        });

        var snapshot = await _service.CreateAsync(
            new FeatureSnapshotTarget("Payments ART", "PAY,CARD", 1, "PI 26.1"),
            includePiLabelMatches: true);

        var items = await _service.GetItemsAsync(snapshot.Id);

        Assert.Equal(["card-2", "PAY-1"], items.Select(i => i.JiraId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
        Assert.Equal("PAY,CARD", snapshot.ArtJiraKeys);
    }

    [Fact]
    public async Task Locking_a_pi_captures_every_key_of_a_multi_key_art_in_one_snapshot()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY", "CARD"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(Feature(2, "CARD", "CARD-2", pi));
        });

        var created = Assert.Single(await _service.CreateForLockedPiAsync("PI 26.1"));

        Assert.Equal(2, created.FeatureCount);
        Assert.Equal(1, created.CapitalProjectId);
        Assert.Equal("PAY,CARD", created.ArtJiraKeys);
    }

    [Fact]
    public async Task Locking_a_pi_gives_the_features_of_a_shared_key_to_neither_art()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Art(1, "Payments ART", "PAY", "CARD"));
            db.CapitalProjects.Add(Art(2, "Cards ART", "CARD"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(Feature(2, "CARD", "CARD-1", pi));
            db.Features.Add(Feature(3, "CARD", "CARD-2", pi));
        });

        var created = Assert.Single(await _service.CreateForLockedPiAsync("PI 26.1"));
        var items = await _service.GetItemsAsync(created.Id);

        Assert.Equal("Payments ART", created.ArtName);
        Assert.Equal("PAY-1", Assert.Single(items).JiraId);
    }

    [Fact]
    public async Task A_snapshot_of_an_art_that_takes_a_shared_key_by_components_captures_only_their_features()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Payments());
            db.CapitalProjects.Add(new Art
            {
                Id = 2,
                Name = "Cards ART",
                JiraKeys = [new ArtJiraKey { JiraKey = "PAY", Components = "Cards, Debit" }]
            });
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi, components: "cards"));
            db.Features.Add(Feature(2, "PAY", "PAY-2", pi, components: "Ledger"));
            db.Features.Add(Feature(3, "PAY", "PAY-3", pi));
        });

        var cards = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget("Cards ART", "PAY", 2, "PI 26.1"),
            includePiLabelMatches: true);
        var payments = await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: true);
        var locked = await _service.CreateForLockedPiAsync("PI 26.1");

        Assert.Equal(["PAY-1"], cards.Select(i => i.JiraId));
        Assert.Equal(["PAY-2", "PAY-3"], payments.Select(i => i.JiraId));
        Assert.Equal(2, locked.Single(s => s.CapitalProjectId == 1).FeatureCount);
        Assert.Equal(1, locked.Single(s => s.CapitalProjectId == 2).FeatureCount);
    }

    [Fact]
    public async Task A_snapshot_of_an_existing_art_follows_the_current_keys_of_the_art()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Payments());
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(Feature(2, "CARD", "CARD-2", pi));
        });

        var snapshot = await _service.CreateAsync(PaymentsPi1, includePiLabelMatches: true);

        await _db.SeedAsync(db => db.CapitalProjectJiraKeys.Add(new ArtJiraKey { CapitalProjectId = 1, JiraKey = "CARD" }));

        var now = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget(snapshot.ArtName, snapshot.ArtJiraKeys, snapshot.CapitalProjectId, snapshot.PiName),
            snapshot.IncludedPiLabelMatches);

        Assert.Equal(1, snapshot.FeatureCount);
        Assert.Equal(["CARD-2", "PAY-1"], now.Select(i => i.JiraId));
    }

    [Fact]
    public async Task A_snapshot_of_an_existing_art_ignores_stored_keys_the_art_no_longer_has()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Art(1, "Payments ART", "CARD"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(Feature(2, "CARD", "CARD-2", pi));
        });

        var items = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget("Payments ART", "PAY", 1, "PI 26.1"),
            includePiLabelMatches: true);

        Assert.Equal("CARD-2", Assert.Single(items).JiraId);
    }

    [Fact]
    public async Task A_snapshot_whose_art_was_deleted_falls_back_to_its_stored_keys()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Art(2, "Core ART", "CORE"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi));
            db.Features.Add(new Feature { Id = 2, JiraId = "card-2", Summary = "No project key", Pi = pi });
            db.Features.Add(Feature(3, "CORE", "CORE-3", pi));
        });

        var items = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget("Payments ART", "PAY,CARD", 1, "PI 26.1"),
            includePiLabelMatches: true);

        Assert.Equal(["card-2", "PAY-1"], items.Select(i => i.JiraId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_snapshot_of_a_deleted_single_key_art_keeps_every_feature_of_its_now_unused_key()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Art(2, "Core ART", "CORE"));
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi, components: "Cards"));
            db.Features.Add(Feature(2, "PAY", "PAY-2", pi, labels: "retail"));
            db.Features.Add(Feature(3, "PAY", "PAY-3", pi));
            db.Features.Add(Feature(4, "CORE", "CORE-4", pi));
        });

        var items = await _service.CaptureCurrentAsync(PaymentsPi1, includePiLabelMatches: true);

        Assert.Equal(["PAY-1", "PAY-2", "PAY-3"], items.Select(i => i.JiraId));
    }

    [Fact]
    public async Task A_snapshot_whose_art_was_deleted_leaves_out_the_features_other_arts_own_on_a_shared_key()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(new Art { Id = 1, Name = "Payments ART", JiraKeys = [new ArtJiraKey { JiraKey = "PAY", Labels = "payments" }] });
            db.CapitalProjects.Add(new Art { Id = 3, Name = "Retail ART", JiraKeys = [new ArtJiraKey { JiraKey = "PAY", Labels = "retail" }] });
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi, labels: "payments"));
            db.Features.Add(Feature(2, "PAY", "PAY-2", pi, components: "Cards"));
            db.Features.Add(Feature(3, "PAY", "PAY-3", pi, labels: "payments, retail"));
            db.Features.Add(Feature(4, "PAY", "PAY-4", pi, labels: "retail"));
        });

        var items = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget("Cards ART", "PAY", 2, "PI 26.1"),
            includePiLabelMatches: true);

        Assert.Equal(["PAY-2", "PAY-3"], items.Select(i => i.JiraId));
    }

    [Fact]
    public async Task A_snapshot_whose_art_was_deleted_captures_nothing_on_a_key_another_art_takes_whole()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.CapitalProjects.Add(Payments());
            db.Features.Add(Feature(1, "PAY", "PAY-1", pi, components: "Cards"));
            db.Features.Add(Feature(2, "PAY", "PAY-2", pi));
        });

        var items = await _service.CaptureCurrentAsync(
            new FeatureSnapshotTarget("Cards ART", "PAY", 2, "PI 26.1"),
            includePiLabelMatches: true);

        Assert.Empty(items);
    }

    [Fact]
    public void Keys_are_joined_with_commas_and_split_without_padding()
    {
        Assert.Null(FeatureSnapshotService.JoinKeys([]));
        Assert.Equal("PAY,CARD", FeatureSnapshotService.JoinKeys(["PAY", "CARD"]));
        Assert.Empty(FeatureSnapshotService.SplitKeys(null));
        Assert.Empty(FeatureSnapshotService.SplitKeys("  "));
        Assert.Equal(["PAY", "CARD"], FeatureSnapshotService.SplitKeys(" PAY , ,CARD "));
    }

    [Fact]
    public void Joined_keys_are_cut_to_whole_keys_that_fit_the_column()
    {
        var keys = Enumerable.Range(0, 30).Select(i => $"KEY{i:D7}").ToList();

        var joined = FeatureSnapshotService.JoinKeys(keys)!;

        Assert.True(joined.Length <= FeatureSnapshot.ArtJiraKeysMaxLength);
        Assert.Equal(keys.Take(18), FeatureSnapshotService.SplitKeys(joined));
    }
}
