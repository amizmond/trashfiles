using Estimation.Core.Features.Services;
using Estimation.Core.ReviewRounds.Data;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.ReviewRounds.Services;

/// <summary>
/// A review is only meaningful while both of its snapshots exist, so a snapshot that is the
/// baseline or the review snapshot of any review cannot be deleted until the review is gone.
/// </summary>
public class ReviewRoundSnapshotDeletionGuard : IFeatureSnapshotDeletionGuard
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public ReviewRoundSnapshotDeletionGuard(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<string?> GetBlockingReasonAsync(int snapshotId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var usedBy = await db.FeatureChangeReviews()
            .AsNoTracking()
            .Where(r => r.BaselineSnapshotId == snapshotId || r.ReviewSnapshotId == snapshotId)
            .Select(r => r.Name ?? r.PiName)
            .ToListAsync();

        return usedBy.Count == 0
            ? null
            : $"The snapshot is used by change review(s): {string.Join(", ", usedBy)}. Delete the review(s) first.";
    }
}
