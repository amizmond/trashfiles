using System.Text.Json;
using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Services;
using Estimation.Core.ReviewRounds.Data;
using Estimation.Core.ReviewRounds.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.ReviewRounds.Services;

public record FeatureChangeReviewSummary(
    FeatureChangeReview Review,
    int TotalCount,
    int ApprovedCount,
    int RejectedCount,
    int PendingCount);

public interface IFeatureChangeReviewService
{
    Task<List<FeatureChangeReviewSummary>> GetAllAsync();

    Task<FeatureChangeReview?> GetByIdAsync(int id);

    /// <summary>
    /// The baseline a new review should default to: the snapshot captured by the latest
    /// completed review of the same ART and PI (rolling baseline), else the latest automatic
    /// PI-lock snapshot, else the latest snapshot.
    /// </summary>
    Task<int?> SuggestBaselineSnapshotIdAsync(string artName, string piName);

    Task<FeatureChangeReview> CreateAsync(int baselineSnapshotId, string? name);

    Task<int> DecideAsync(int reviewId, IReadOnlyCollection<int> itemIds, FeatureChangeDecision decision, string? comment);

    Task<FeatureChangeReview> CompleteAsync(int reviewId);

    Task<FeatureChangeReview> ReopenAsync(int reviewId);

    Task<bool> DeleteAsync(int id);
}

public class FeatureChangeReviewService : IFeatureChangeReviewService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IFeatureSnapshotService _snapshots;
    private readonly IFeatureSnapshotDeltaService _delta;
    private readonly IAuditUserProvider _auditUser;

    public FeatureChangeReviewService(
        IDbContextFactory<EstimationDbContext> ctx,
        IFeatureSnapshotService snapshots,
        IFeatureSnapshotDeltaService delta,
        IAuditUserProvider auditUser)
    {
        _ctx = ctx;
        _snapshots = snapshots;
        _delta = delta;
        _auditUser = auditUser;
    }

    public async Task<List<FeatureChangeReviewSummary>> GetAllAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var reviews = await db.FeatureChangeReviews()
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync();

        var counts = await db.FeatureChangeReviewItems()
            .AsNoTracking()
            .GroupBy(i => new { i.ReviewId, i.Decision })
            .Select(g => new { g.Key.ReviewId, g.Key.Decision, Count = g.Count() })
            .ToListAsync();

        var countsByReview = counts
            .GroupBy(c => c.ReviewId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(c => c.Decision, c => c.Count));

        return reviews
            .Select(r =>
            {
                var byDecision = countsByReview.GetValueOrDefault(r.Id) ?? [];
                var approved = byDecision.GetValueOrDefault(FeatureChangeDecision.Approved);
                var rejected = byDecision.GetValueOrDefault(FeatureChangeDecision.Rejected);
                var pending = byDecision.GetValueOrDefault(FeatureChangeDecision.Pending);
                return new FeatureChangeReviewSummary(r, approved + rejected + pending, approved, rejected, pending);
            })
            .ToList();
    }

    public async Task<FeatureChangeReview?> GetByIdAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.FeatureChangeReviews()
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<int?> SuggestBaselineSnapshotIdAsync(string artName, string piName)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var lastCompleted = await db.FeatureChangeReviews()
            .AsNoTracking()
            .Where(r => r.Status == FeatureChangeReviewStatus.Completed)
            .Where(r => r.ArtName == artName && r.PiName == piName)
            .OrderByDescending(r => r.CompletedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => new { r.ReviewSnapshotId })
            .FirstOrDefaultAsync();

        if (lastCompleted is not null)
        {
            return lastCompleted.ReviewSnapshotId;
        }

        var snapshots = await db.FeatureSnapshots
            .AsNoTracking()
            .Where(s => s.ArtName == artName && s.PiName == piName)
            .Select(s => new { s.Id, s.IsAutomatic, s.CreatedAt })
            .ToListAsync();

        var best = snapshots
            .OrderByDescending(s => s.IsAutomatic)
            .ThenByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefault();

        return best?.Id;
    }

    public async Task<FeatureChangeReview> CreateAsync(int baselineSnapshotId, string? name)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var baseline = await db.FeatureSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == baselineSnapshotId)
            ?? throw new InvalidOperationException("The baseline snapshot no longer exists.");

        var openExists = await db.FeatureChangeReviews()
            .AsNoTracking()
            .AnyAsync(r => r.Status == FeatureChangeReviewStatus.Open
                && r.ArtName == baseline.ArtName
                && r.PiName == baseline.PiName);

        if (openExists)
        {
            throw new InvalidOperationException(
                $"An open change review already exists for {baseline.ArtName} / {baseline.PiName}. Complete or delete it first.");
        }

        var reviewName = Normalize(name);

        var reviewSnapshot = await _snapshots.CreateAsync(
            new FeatureSnapshotTarget(
                baseline.ArtName,
                baseline.ArtJiraKey,
                baseline.CapitalProjectId,
                baseline.PiName,
                reviewName),
            baseline.IncludedPiLabelMatches);

        var baselineItems = await _snapshots.GetItemsAsync(baseline.Id);
        var delta = _delta.Compare(baselineItems, reviewSnapshot.Items.ToList());

        var review = new FeatureChangeReview
        {
            CapitalProjectId = baseline.CapitalProjectId,
            ArtName = baseline.ArtName,
            ArtJiraKey = baseline.ArtJiraKey,
            PiName = baseline.PiName,
            Name = reviewName,
            BaselineSnapshotId = baseline.Id,
            ReviewSnapshotId = reviewSnapshot.Id,
            Status = FeatureChangeReviewStatus.Open,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _auditUser.GetCurrentUserName(),
            Items = delta.Rows
                .Where(r => r.Kind != FeatureDeltaChangeKind.Unchanged)
                .OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                .Select(ToReviewItem)
                .ToList()
        };

        db.FeatureChangeReviews().Add(review);
        await db.SaveChangesAsync();

        return review;
    }

    public async Task<int> DecideAsync(
        int reviewId,
        IReadOnlyCollection<int> itemIds,
        FeatureChangeDecision decision,
        string? comment)
    {
        if (itemIds.Count == 0)
        {
            return 0;
        }

        await using var db = await _ctx.CreateDbContextAsync();

        var review = await db.FeatureChangeReviews().FirstOrDefaultAsync(r => r.Id == reviewId)
            ?? throw new InvalidOperationException("The change review no longer exists.");

        if (review.Status == FeatureChangeReviewStatus.Completed)
        {
            throw new InvalidOperationException("The change review is completed. Reopen it to change decisions.");
        }

        var ids = itemIds.ToHashSet();
        var items = await db.FeatureChangeReviewItems()
            .Where(i => i.ReviewId == reviewId && ids.Contains(i.Id))
            .ToListAsync();

        var pending = decision == FeatureChangeDecision.Pending;
        var decidedBy = pending ? null : _auditUser.GetCurrentUserName();
        DateTime? decidedAt = pending ? null : DateTime.UtcNow;
        var normalizedComment = pending ? null : Normalize(comment);

        foreach (var item in items)
        {
            item.Decision = decision;
            item.Comment = normalizedComment;
            item.DecidedBy = decidedBy;
            item.DecidedAt = decidedAt;
        }

        await db.SaveChangesAsync();
        return items.Count;
    }

    public async Task<FeatureChangeReview> CompleteAsync(int reviewId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var review = await db.FeatureChangeReviews().FirstOrDefaultAsync(r => r.Id == reviewId)
            ?? throw new InvalidOperationException("The change review no longer exists.");

        if (review.Status == FeatureChangeReviewStatus.Completed)
        {
            throw new InvalidOperationException("The change review is already completed.");
        }

        var pending = await db.FeatureChangeReviewItems()
            .CountAsync(i => i.ReviewId == reviewId && i.Decision == FeatureChangeDecision.Pending);

        if (pending > 0)
        {
            throw new InvalidOperationException(
                $"{pending} change(s) are still pending. Approve or reject every change before completing the review.");
        }

        review.Status = FeatureChangeReviewStatus.Completed;
        review.CompletedAt = DateTime.UtcNow;
        review.CompletedBy = _auditUser.GetCurrentUserName();

        await db.SaveChangesAsync();
        return review;
    }

    public async Task<FeatureChangeReview> ReopenAsync(int reviewId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var review = await db.FeatureChangeReviews().FirstOrDefaultAsync(r => r.Id == reviewId)
            ?? throw new InvalidOperationException("The change review no longer exists.");

        if (review.Status != FeatureChangeReviewStatus.Completed)
        {
            throw new InvalidOperationException("Only a completed change review can be reopened.");
        }

        review.Status = FeatureChangeReviewStatus.Open;
        review.CompletedAt = null;
        review.CompletedBy = null;

        await db.SaveChangesAsync();
        return review;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var review = await db.FeatureChangeReviews()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review is null)
        {
            return false;
        }

        db.FeatureChangeReviewItems().RemoveRange(review.Items);
        db.FeatureChangeReviews().Remove(review);
        await db.SaveChangesAsync();
        return true;
    }

    public static IReadOnlyList<FeatureDeltaFieldChange> ParseChanges(string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<FeatureDeltaFieldChange>>(changesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static FeatureChangeReviewItem ToReviewItem(FeatureDeltaRow row) =>
        new()
        {
            FeatureKey = row.Key,
            JiraId = row.Current.JiraId,
            FeatureName = row.Current.Name,
            Summary = row.Current.Summary,
            ChangeKind = row.Kind,
            ChangesJson = row.Changes.Count == 0 ? null : JsonSerializer.Serialize(row.Changes),
            Decision = FeatureChangeDecision.Pending
        };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
