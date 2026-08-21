using Estimation.Core.Administration.Audit;
using Estimation.Core.HashApprovals.Data;
using Estimation.Core.HashApprovals.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.HashApprovals.Services;

/// <summary>Lookup key of an approval: the delta row key (case-insensitive) plus the state hash.</summary>
public readonly record struct FeatureStateKey(string FeatureKey, string StateHash)
{
    public static FeatureStateKey Of(string featureKey, string stateHash) =>
        new(featureKey.Trim().ToUpperInvariant(), stateHash.Trim());
}

public record FeatureStateApprovalInfo(
    int Id,
    string FeatureKey,
    string StateHash,
    string? ApprovedBy,
    DateTime ApprovedAt,
    string? Comment);

public record FeatureStateApprovalRequest(
    string FeatureKey,
    string StateHash,
    string? StateJson,
    string? ChangesJson,
    string? JiraId,
    string? FeatureName);

public interface IFeatureStateApprovalService
{
    /// <summary>Every approval of the ART and PI that has not been withdrawn, keyed for delta lookups.</summary>
    Task<IReadOnlyDictionary<FeatureStateKey, FeatureStateApprovalInfo>> GetActiveAsync(string artName, string piName);

    /// <summary>Approves the given states; states that are already approved are skipped. Returns the number of new approvals.</summary>
    Task<int> ApproveAsync(
        string artName,
        string piName,
        int? baselineSnapshotId,
        IReadOnlyCollection<FeatureStateApprovalRequest> requests,
        string? comment);

    /// <summary>Soft-deletes an approval. Returns false when it does not exist or is already withdrawn.</summary>
    Task<bool> WithdrawAsync(int approvalId);

    /// <summary>Full history (active and withdrawn) of one feature in the ART and PI, newest first.</summary>
    Task<List<FeatureStateApproval>> GetHistoryAsync(string artName, string piName, string featureKey);
}

public class FeatureStateApprovalService : IFeatureStateApprovalService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;
    private readonly IAuditUserProvider _auditUser;

    public FeatureStateApprovalService(IDbContextFactory<EstimationDbContext> ctx, IAuditUserProvider auditUser)
    {
        _ctx = ctx;
        _auditUser = auditUser;
    }

    public async Task<IReadOnlyDictionary<FeatureStateKey, FeatureStateApprovalInfo>> GetActiveAsync(string artName, string piName)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var active = await db.FeatureStateApprovals()
            .AsNoTracking()
            .Where(a => a.ArtName == artName && a.PiName == piName && a.WithdrawnAt == null)
            .OrderBy(a => a.ApprovedAt)
            .ThenBy(a => a.Id)
            .ToListAsync();

        var result = new Dictionary<FeatureStateKey, FeatureStateApprovalInfo>();

        foreach (var approval in active)
        {
            // Two users approving the same state at the same moment leave two rows; the first one wins.
            result.TryAdd(
                FeatureStateKey.Of(approval.FeatureKey, approval.StateHash),
                new FeatureStateApprovalInfo(
                    approval.Id,
                    approval.FeatureKey,
                    approval.StateHash,
                    approval.ApprovedBy,
                    approval.ApprovedAt,
                    approval.Comment));
        }

        return result;
    }

    public async Task<int> ApproveAsync(
        string artName,
        string piName,
        int? baselineSnapshotId,
        IReadOnlyCollection<FeatureStateApprovalRequest> requests,
        string? comment)
    {
        if (requests.Count == 0)
        {
            return 0;
        }

        var alreadyActive = await GetActiveAsync(artName, piName);
        var approvedBy = _auditUser.GetCurrentUserName();
        var approvedAt = DateTime.UtcNow;
        var normalizedComment = Normalize(comment);
        var seen = new HashSet<FeatureStateKey>();

        await using var db = await _ctx.CreateDbContextAsync();
        var added = 0;

        foreach (var request in requests)
        {
            var key = FeatureStateKey.Of(request.FeatureKey, request.StateHash);

            if (alreadyActive.ContainsKey(key) || !seen.Add(key))
            {
                continue;
            }

            db.FeatureStateApprovals().Add(new FeatureStateApproval
            {
                ArtName = artName,
                PiName = piName,
                FeatureKey = request.FeatureKey.Trim(),
                JiraId = Normalize(request.JiraId),
                FeatureName = Normalize(request.FeatureName),
                StateHash = request.StateHash.Trim(),
                StateJson = request.StateJson,
                ChangesJson = request.ChangesJson,
                BaselineSnapshotId = baselineSnapshotId,
                Comment = normalizedComment,
                ApprovedBy = approvedBy,
                ApprovedAt = approvedAt
            });

            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
        }

        return added;
    }

    public async Task<bool> WithdrawAsync(int approvalId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var approval = await db.FeatureStateApprovals().FirstOrDefaultAsync(a => a.Id == approvalId);

        if (approval is null || approval.WithdrawnAt is not null)
        {
            return false;
        }

        approval.WithdrawnBy = _auditUser.GetCurrentUserName();
        approval.WithdrawnAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<FeatureStateApproval>> GetHistoryAsync(string artName, string piName, string featureKey)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var normalizedKey = featureKey.Trim();

        return await db.FeatureStateApprovals()
            .AsNoTracking()
            .Where(a => a.ArtName == artName && a.PiName == piName && a.FeatureKey == normalizedKey)
            .OrderByDescending(a => a.ApprovedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
