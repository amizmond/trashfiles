namespace Estimation.Core.Features.Services;

/// <summary>
/// Lets another feature area that builds on snapshots veto the deletion of a snapshot it still
/// depends on. Implementations are registered in DI; the snapshot service asks every one of them
/// before deleting.
/// </summary>
public interface IFeatureSnapshotDeletionGuard
{
    /// <summary>A human-readable reason when the snapshot must not be deleted, otherwise null.</summary>
    Task<string?> GetBlockingReasonAsync(int snapshotId);
}
