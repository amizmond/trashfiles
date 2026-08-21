using System.Text.Json;
using Estimation.Core.Features.Services;
using Estimation.Core.HashApprovals.Services;

namespace Estimation.Components.HashApprovals;

/// <summary>
/// Page-side helper for the Feature delta: annotates every Added / Changed / Removed row with the
/// hash approval of its B-side state, filters rows by approval status and performs approve /
/// withdraw. Created only when hash approvals are switched on (the service is registered).
/// </summary>
public sealed class DeltaApprovalState
{
    private readonly IFeatureStateApprovalService _service;

    private Dictionary<FeatureDeltaRow, FeatureStateApprovalInfo?> _byRow = new(ReferenceEqualityComparer.Instance);
    private string _artName = string.Empty;
    private string _piName = string.Empty;
    private int? _baselineSnapshotId;
    private FeatureDeltaResult? _result;

    private DeltaApprovalState(IFeatureStateApprovalService service) => _service = service;

    /// <summary>Null when hash approvals are switched off.</summary>
    public static DeltaApprovalState? TryCreate(IServiceProvider services) =>
        services.GetService<IFeatureStateApprovalService>() is { } service ? new DeltaApprovalState(service) : null;

    public bool ShowApproved { get; set; } = true;

    public bool ShowNotApproved { get; set; } = true;

    public int ApprovedCount { get; private set; }

    public int NotApprovedCount { get; private set; }

    public async Task LoadAsync(string artName, string piName, int? baselineSnapshotId, FeatureDeltaResult result)
    {
        _artName = artName;
        _piName = piName;
        _baselineSnapshotId = baselineSnapshotId;
        _result = result;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var byRow = new Dictionary<FeatureDeltaRow, FeatureStateApprovalInfo?>(ReferenceEqualityComparer.Instance);

        if (_result is not null)
        {
            var active = await _service.GetActiveAsync(_artName, _piName);

            foreach (var row in _result.Rows.Where(NeedsApproval))
            {
                active.TryGetValue(FeatureStateKey.Of(row.Key, FeatureStateHasher.HashForRow(row)), out var info);
                byRow[row] = info;
            }
        }

        _byRow = byRow;
        ApprovedCount = byRow.Values.Count(v => v is not null);
        NotApprovedCount = byRow.Count - ApprovedCount;
    }

    /// <summary>Unchanged rows equal the baseline and need no approval.</summary>
    public static bool NeedsApproval(FeatureDeltaRow row) => row.Kind != FeatureDeltaChangeKind.Unchanged;

    public FeatureStateApprovalInfo? ApprovalOf(FeatureDeltaRow row) => _byRow.GetValueOrDefault(row);

    public bool IsApproved(FeatureDeltaRow row) => ApprovalOf(row) is not null;

    public bool IsVisible(FeatureDeltaRow row) =>
        !NeedsApproval(row) || (IsApproved(row) ? ShowApproved : ShowNotApproved);

    public IReadOnlyList<FeatureDeltaRow> PendingOf(IEnumerable<FeatureDeltaRow> rows) =>
        rows.Where(r => NeedsApproval(r) && !IsApproved(r)).ToList();

    /// <summary>Approves the current state of every pending row among <paramref name="rows"/>.</summary>
    public async Task<int> ApproveAsync(IEnumerable<FeatureDeltaRow> rows, string? comment)
    {
        var requests = PendingOf(rows).Select(ToRequest).ToList();
        var added = await _service.ApproveAsync(_artName, _piName, _baselineSnapshotId, requests, comment);
        await RefreshAsync();
        return added;
    }

    public async Task<bool> WithdrawAsync(FeatureDeltaRow row)
    {
        if (ApprovalOf(row) is not { } info)
        {
            return false;
        }

        var withdrawn = await _service.WithdrawAsync(info.Id);
        await RefreshAsync();
        return withdrawn;
    }

    public string ExportValue(FeatureDeltaRow row, Func<DateTime, string> formatTime)
    {
        if (!NeedsApproval(row))
        {
            return string.Empty;
        }

        return ApprovalOf(row) is { } info
            ? $"APPROVED by {info.ApprovedBy} on {formatTime(info.ApprovedAt)}"
            : "NOT APPROVED";
    }

    private static FeatureStateApprovalRequest ToRequest(FeatureDeltaRow row) =>
        new(
            row.Key,
            FeatureStateHasher.HashForRow(row),
            FeatureStateHasher.StateJsonForRow(row),
            row.Changes.Count == 0 ? null : JsonSerializer.Serialize(row.Changes),
            row.JiraId,
            row.Current.Name);
}
