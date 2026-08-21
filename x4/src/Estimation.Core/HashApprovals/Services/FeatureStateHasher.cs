using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;

namespace Estimation.Core.HashApprovals.Services;

/// <summary>
/// Turns the compared fields of a feature into a stable hash. The canonical values come from
/// <see cref="FeatureSnapshotDeltaService.CanonicalValue"/>, so two states hash the same exactly
/// when the delta would report them as unchanged. The version prefix lets a future change of the
/// compared field set be detected instead of silently un-approving every stored state.
/// </summary>
public static class FeatureStateHasher
{
    public const string Version = "v1";

    /// <summary>The hash of "the feature no longer belongs to the ART and PI" (a Removed delta row).</summary>
    public static readonly string RemovedHash = $"{Version}:REMOVED";

    /// <summary>Deterministic JSON of the canonical field values, in <see cref="FeatureDeltaFields.All"/> order.</summary>
    public static string StateJsonOf(FeatureSnapshotItem item)
    {
        var fields = FeatureDeltaFields.All
            .Select(field => new StateField(field, FeatureSnapshotDeltaService.CanonicalValue(item, field)))
            .ToArray();

        return JsonSerializer.Serialize(fields);
    }

    public static string HashOf(FeatureSnapshotItem item) => HashOfStateJson(StateJsonOf(item));

    public static string HashOfStateJson(string stateJson) =>
        $"{Version}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson))).ToLowerInvariant()}";

    /// <summary>The hash of a delta row's B side — the state a user approves — or the removal hash.</summary>
    public static string HashForRow(FeatureDeltaRow row) => row.B is null ? RemovedHash : HashOf(row.B);

    public static string? StateJsonForRow(FeatureDeltaRow row) => row.B is null ? null : StateJsonOf(row.B);

    private sealed record StateField(
        [property: JsonPropertyName("f")] string Field,
        [property: JsonPropertyName("v")] string? Value);
}
