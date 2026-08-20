using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureChangeReviewServiceTests
{
    private const string Reviewer = "DOMAIN\\tester";

    private readonly InMemoryDatabase _db = new();
    private readonly FeatureSnapshotService _snapshots;
    private readonly FeatureChangeReviewService _service;

    public FeatureChangeReviewServiceTests()
    {
        var auditUser = new StubAuditUser(Reviewer);
        _snapshots = new FeatureSnapshotService(_db, auditUser);
        _service = new FeatureChangeReviewService(_db, _snapshots, new FeatureSnapshotDeltaService(), auditUser);
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        private readonly string? _userName;

        public StubAuditUser(string? userName) => _userName = userName;

        public string? GetCurrentUserName() => _userName;
    }

    private static readonly FeatureSnapshotTarget PaymentsPi1 = new("Payments ART", "PAY", 1, "PI 26.1");

    private static Feature Feature(int id, string jiraId, Pi pi, int? storyPoints = null) =>
        new()
        {
            Id = id,
            ProjectKey = "PAY",
            JiraId = jiraId,
            Summary = $"Feature {jiraId}",
            StoryPoints = storyPoints,
            Pi = pi
        };

    private async Task<FeatureSnapshot> SeedBaselineAsync()
    {
        var pi = new Pi { Id = 1, Name = "PI 26.1" };

        await _db.SeedAsync(db =>
        {
            db.Pis.Add(pi);
            db.Features.Add(Feature(1, "PAY-1", pi, storyPoints: 5));
            db.Features.Add(Feature(2, "PAY-2", pi));
            db.Features.Add(Feature(3, "PAY-3", pi));
        });

        return await _snapshots.CreateAsync(PaymentsPi1, includePiLabelMatches: true);
    }

    [Fact]
    public async Task Create_captures_a_review_snapshot_and_records_every_change()
    {
        var baseline = await SeedBaselineAsync();

        await _db.SeedAsync(db =>
        {
            // PAY-1 changes, PAY-2 leaves the PI, PAY-4 is added; PAY-3 stays unchanged.
            db.Features.Include(f => f.Pi).First(f => f.JiraId == "PAY-1").StoryPoints = 8;
            db.Features.Include(f => f.Pi).First(f => f.JiraId == "PAY-2").Pi = null;
            db.Features.Add(Feature(4, "PAY-4", db.Pis.First(p => p.Name == "PI 26.1")));
        });

        var review = await _service.CreateAsync(baseline.Id, "Weekly review");

        Assert.Equal("Weekly review", review.Name);
        Assert.Equal(FeatureChangeReviewStatus.Open, review.Status);
        Assert.Equal(Reviewer, review.CreatedBy);
        Assert.Equal(baseline.Id, review.BaselineSnapshotId);
        Assert.Equal(3, review.Items.Count);

        var changed = Assert.Single(review.Items, i => i.JiraId == "PAY-1");
        Assert.Equal(FeatureDeltaChangeKind.Changed, changed.ChangeKind);
        Assert.Equal(FeatureChangeDecision.Pending, changed.Decision);
        var change = Assert.Single(FeatureChangeReviewService.ParseChanges(changed.ChangesJson));
        Assert.Equal(FeatureDeltaFields.StoryPoints, change.Field);
        Assert.Equal("5", change.OldValue);
        Assert.Equal("8", change.NewValue);

        Assert.Equal(FeatureDeltaChangeKind.Removed, Assert.Single(review.Items, i => i.JiraId == "PAY-2").ChangeKind);
        Assert.Equal(FeatureDeltaChangeKind.Added, Assert.Single(review.Items, i => i.JiraId == "PAY-4").ChangeKind);

        var reviewSnapshot = await _db.ReadAsync(db => db.FeatureSnapshots.FirstAsync(s => s.Id == review.ReviewSnapshotId));
        Assert.Equal("Weekly review", reviewSnapshot.Name);
        Assert.Equal(3, reviewSnapshot.FeatureCount);
    }

    [Fact]
    public async Task Create_produces_an_empty_review_when_nothing_changed()
    {
        var baseline = await SeedBaselineAsync();

        var review = await _service.CreateAsync(baseline.Id, null);

        Assert.Empty(review.Items);
        Assert.Null(review.Name);
    }

    [Fact]
    public async Task Create_refuses_a_second_open_review_for_the_same_art_and_pi()
    {
        var baseline = await SeedBaselineAsync();
        await _service.CreateAsync(baseline.Id, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(baseline.Id, null));

        Assert.Contains("open change review already exists", ex.Message);
    }

    [Fact]
    public async Task Decide_stamps_the_decision_and_reset_clears_it()
    {
        var baseline = await SeedBaselineAsync();
        await _db.SeedAsync(db => db.Features.First(f => f.JiraId == "PAY-1").StoryPoints = 8);
        var review = await _service.CreateAsync(baseline.Id, null);
        var itemId = review.Items.Single().Id;

        var decided = await _service.DecideAsync(review.Id, [itemId], FeatureChangeDecision.Rejected, "  scope creep  ");
        Assert.Equal(1, decided);

        var item = (await _service.GetByIdAsync(review.Id))!.Items.Single();
        Assert.Equal(FeatureChangeDecision.Rejected, item.Decision);
        Assert.Equal("scope creep", item.Comment);
        Assert.Equal(Reviewer, item.DecidedBy);
        Assert.NotNull(item.DecidedAt);

        await _service.DecideAsync(review.Id, [itemId], FeatureChangeDecision.Pending, "ignored");

        item = (await _service.GetByIdAsync(review.Id))!.Items.Single();
        Assert.Equal(FeatureChangeDecision.Pending, item.Decision);
        Assert.Null(item.Comment);
        Assert.Null(item.DecidedBy);
        Assert.Null(item.DecidedAt);
    }

    [Fact]
    public async Task Decide_is_refused_on_a_completed_review()
    {
        var baseline = await SeedBaselineAsync();
        var review = await _service.CreateAsync(baseline.Id, null);
        await _service.CompleteAsync(review.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.DecideAsync(review.Id, [1], FeatureChangeDecision.Approved, null));
    }

    [Fact]
    public async Task Complete_requires_every_change_to_be_decided()
    {
        var baseline = await SeedBaselineAsync();
        await _db.SeedAsync(db => db.Features.First(f => f.JiraId == "PAY-1").StoryPoints = 8);
        var review = await _service.CreateAsync(baseline.Id, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CompleteAsync(review.Id));
        Assert.Contains("still pending", ex.Message);

        await _service.DecideAsync(review.Id, review.Items.Select(i => i.Id).ToList(), FeatureChangeDecision.Approved, null);
        var completed = await _service.CompleteAsync(review.Id);

        Assert.Equal(FeatureChangeReviewStatus.Completed, completed.Status);
        Assert.Equal(Reviewer, completed.CompletedBy);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task Reopen_makes_a_completed_review_editable_again()
    {
        var baseline = await SeedBaselineAsync();
        var review = await _service.CreateAsync(baseline.Id, null);
        await _service.CompleteAsync(review.Id);

        var reopened = await _service.ReopenAsync(review.Id);

        Assert.Equal(FeatureChangeReviewStatus.Open, reopened.Status);
        Assert.Null(reopened.CompletedAt);
        Assert.Null(reopened.CompletedBy);
    }

    [Fact]
    public async Task A_snapshot_used_by_a_review_cannot_be_deleted_until_the_review_is_gone()
    {
        var baseline = await SeedBaselineAsync();
        var review = await _service.CreateAsync(baseline.Id, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _snapshots.DeleteAsync(baseline.Id));
        Assert.Contains("used by change review", ex.Message);

        Assert.True(await _service.DeleteAsync(review.Id));
        Assert.True(await _snapshots.DeleteAsync(baseline.Id));
    }

    [Fact]
    public async Task Deleting_a_review_removes_its_items_but_keeps_the_snapshots()
    {
        var baseline = await SeedBaselineAsync();
        await _db.SeedAsync(db => db.Features.First(f => f.JiraId == "PAY-1").StoryPoints = 8);
        var review = await _service.CreateAsync(baseline.Id, null);

        Assert.True(await _service.DeleteAsync(review.Id));

        Assert.Equal(0, await _db.ReadAsync(db => db.FeatureChangeReviewItems.CountAsync()));
        Assert.Equal(2, await _db.ReadAsync(db => db.FeatureSnapshots.CountAsync()));
    }

    [Fact]
    public async Task Suggested_baseline_is_the_snapshot_of_the_last_completed_review()
    {
        var baseline = await SeedBaselineAsync();

        Assert.Equal(baseline.Id, await _service.SuggestBaselineSnapshotIdAsync("Payments ART", "PI 26.1"));

        var review = await _service.CreateAsync(baseline.Id, null);
        await _service.CompleteAsync(review.Id);

        Assert.Equal(review.ReviewSnapshotId, await _service.SuggestBaselineSnapshotIdAsync("Payments ART", "PI 26.1"));
    }

    [Fact]
    public async Task Suggested_baseline_prefers_the_automatic_pi_lock_snapshot()
    {
        await SeedBaselineAsync();

        await _db.SeedAsync(db => db.FeatureSnapshots.Add(new FeatureSnapshot
        {
            ArtName = "Payments ART",
            ArtJiraKey = "PAY",
            PiName = "PI 26.1",
            IsAutomatic = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        }));

        var automaticId = await _db.ReadAsync(db =>
            db.FeatureSnapshots.Where(s => s.IsAutomatic).Select(s => s.Id).SingleAsync());

        Assert.Equal(automaticId, await _service.SuggestBaselineSnapshotIdAsync("Payments ART", "PI 26.1"));
    }

    [Fact]
    public async Task Suggested_baseline_is_null_when_no_snapshot_exists()
    {
        Assert.Null(await _service.SuggestBaselineSnapshotIdAsync("Payments ART", "PI 26.1"));
    }
}
