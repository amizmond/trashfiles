using Estimation.Services.Shared;

namespace Estimation.Services.Features;

public static class FeatureSnapshotDisplay
{
    public static string SnapshotName(this IUserTimeZoneService time, string? name, string piName, DateTime createdAtUtc) =>
        string.IsNullOrWhiteSpace(name)
            ? $"{piName} — {time.Format(createdAtUtc)}"
            : name.Trim();

    public static string SuggestSnapshotName(this IUserTimeZoneService time, string piName) =>
        $"{piName} — {time.Format(DateTime.UtcNow)}";
}
