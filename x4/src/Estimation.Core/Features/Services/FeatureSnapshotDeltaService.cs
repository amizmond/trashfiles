using System.Globalization;
using Estimation.Core.Features.Models;

namespace Estimation.Core.Features.Services;

public enum FeatureDeltaChangeKind
{
    Unchanged = 0,
    Changed = 1,
    Added = 2,
    Removed = 3
}

public static class FeatureDeltaFields
{
    public const string Art = "ART";
    public const string Pi = "PI";
    public const string Labels = "Labels";
    public const string BusinessOutcome = "Business outcome";
    public const string TargetStart = "Target start";
    public const string TargetEnd = "Target end";
    public const string StoryPoints = "Story points";
    public const string Teams = "Teams";
    public const string RequirementStatus = "Requirement status";
    public const string FundingStatus = "Funding";
    public const string Summary = "Summary";
    public const string Name = "Name";
    public const string AcceptanceCriteria = "Acceptance criteria";
    public const string PiObjective = "PI objective";
    public const string RagExplain = "Rag explain";

    /// <summary>Every compared field, in comparison order. Hash-based approvals hash the fields in this order.</summary>
    public static readonly string[] All =
    [
        Art,
        Pi,
        Labels,
        BusinessOutcome,
        TargetStart,
        TargetEnd,
        StoryPoints,
        Teams,
        RequirementStatus,
        FundingStatus,
        Summary,
        Name,
        AcceptanceCriteria,
        PiObjective,
        RagExplain
    ];
}

public record FeatureDeltaFieldChange(string Field, string? OldValue, string? NewValue);

public record FeatureDeltaRow(
    string Key,
    FeatureDeltaChangeKind Kind,
    FeatureSnapshotItem? A,
    FeatureSnapshotItem? B,
    IReadOnlyList<FeatureDeltaFieldChange> Changes)
{
    public FeatureSnapshotItem Current => B ?? A!;

    public string? JiraId => Current.JiraId;

    public bool HasChange(string field) => Changes.Any(c => c.Field == field);

    public FeatureDeltaFieldChange? Change(string field) => Changes.FirstOrDefault(c => c.Field == field);
}

public record FeatureDeltaResult(IReadOnlyList<FeatureDeltaRow> Rows)
{
    public int ChangedCount => Rows.Count(r => r.Kind == FeatureDeltaChangeKind.Changed);

    public int AddedCount => Rows.Count(r => r.Kind == FeatureDeltaChangeKind.Added);

    public int RemovedCount => Rows.Count(r => r.Kind == FeatureDeltaChangeKind.Removed);

    public int UnchangedCount => Rows.Count(r => r.Kind == FeatureDeltaChangeKind.Unchanged);
}

public interface IFeatureSnapshotDeltaService
{
    FeatureDeltaResult Compare(IReadOnlyList<FeatureSnapshotItem> sideA, IReadOnlyList<FeatureSnapshotItem> sideB);
}

public class FeatureSnapshotDeltaService : IFeatureSnapshotDeltaService
{
    public FeatureDeltaResult Compare(IReadOnlyList<FeatureSnapshotItem> sideA, IReadOnlyList<FeatureSnapshotItem> sideB)
    {
        var byJiraB = BuildJiraIndex(sideB);
        var byFeatureB = BuildFeatureIndex(sideB);
        var matchedB = new HashSet<FeatureSnapshotItem>();
        var pairs = new Dictionary<FeatureSnapshotItem, FeatureSnapshotItem>();

        foreach (var a in sideA)
        {
            var jiraKey = NormalizeJiraId(a.JiraId);
            if (jiraKey is not null && byJiraB.TryGetValue(jiraKey, out var match) && matchedB.Add(match))
            {
                pairs[a] = match;
            }
        }

        foreach (var a in sideA)
        {
            if (pairs.ContainsKey(a))
            {
                continue;
            }

            if (byFeatureB.TryGetValue(a.FeatureId, out var match) && matchedB.Add(match))
            {
                pairs[a] = match;
            }
        }

        var rows = new List<FeatureDeltaRow>();

        foreach (var a in sideA)
        {
            if (!pairs.TryGetValue(a, out var b))
            {
                rows.Add(new FeatureDeltaRow(RowKey(a), FeatureDeltaChangeKind.Removed, a, null, []));
                continue;
            }

            var changes = DiffFields(a, b);
            rows.Add(new FeatureDeltaRow(
                RowKey(b),
                changes.Count == 0 ? FeatureDeltaChangeKind.Unchanged : FeatureDeltaChangeKind.Changed,
                a,
                b,
                changes));
        }

        foreach (var b in sideB.Where(item => !matchedB.Contains(item)))
        {
            rows.Add(new FeatureDeltaRow(RowKey(b), FeatureDeltaChangeKind.Added, null, b, []));
        }

        return new FeatureDeltaResult(rows);
    }

    private static List<FeatureDeltaFieldChange> DiffFields(FeatureSnapshotItem a, FeatureSnapshotItem b)
    {
        var changes = new List<FeatureDeltaFieldChange>();

        foreach (var field in FeatureDeltaFields.All)
        {
            if (!string.Equals(CanonicalValue(a, field), CanonicalValue(b, field), StringComparison.Ordinal))
            {
                changes.Add(new FeatureDeltaFieldChange(field, ReportedValue(a, field), ReportedValue(b, field)));
            }
        }

        return changes;
    }

    /// <summary>
    /// The comparison form of a field: two items are unchanged in a field exactly when their canonical
    /// values are equal (ordinal). Text is trimmed, dates keep the date only, label and team lists are
    /// split, de-duplicated, sorted and upper-cased so order and casing do not count as a change.
    /// HashApprovals hashes these values, so they must stay the single definition of "unchanged".
    /// </summary>
    public static string? CanonicalValue(FeatureSnapshotItem item, string field) => field switch
    {
        FeatureDeltaFields.Art => Trimmed(item.ArtName),
        FeatureDeltaFields.Pi => Trimmed(item.PiName),
        FeatureDeltaFields.Labels => CanonicalSet(item.Labels),
        FeatureDeltaFields.BusinessOutcome => BusinessOutcomeText(item),
        FeatureDeltaFields.TargetStart => FormatNullableDate(item.TargetStart),
        FeatureDeltaFields.TargetEnd => FormatNullableDate(item.TargetEnd),
        FeatureDeltaFields.StoryPoints => item.StoryPoints?.ToString(CultureInfo.InvariantCulture),
        FeatureDeltaFields.Teams => CanonicalSet(item.Teams),
        FeatureDeltaFields.RequirementStatus => Trimmed(item.RequirementStatus),
        FeatureDeltaFields.FundingStatus => Trimmed(item.FundingStatus),
        FeatureDeltaFields.Summary => Trimmed(item.Summary),
        FeatureDeltaFields.Name => Trimmed(item.Name),
        FeatureDeltaFields.AcceptanceCriteria => Trimmed(item.AcceptanceCriteria),
        FeatureDeltaFields.PiObjective => Trimmed(item.PiObjective),
        FeatureDeltaFields.RagExplain => Trimmed(item.RagExplain),
        _ => null
    };

    // What a change record shows as old/new value: lists keep the user's own order and casing.
    private static string? ReportedValue(FeatureSnapshotItem item, string field) => field switch
    {
        FeatureDeltaFields.Labels => Trimmed(item.Labels),
        FeatureDeltaFields.Teams => Trimmed(item.Teams),
        _ => CanonicalValue(item, field)
    };

    public static string? BusinessOutcomeText(FeatureSnapshotItem item)
    {
        var name = Trimmed(item.BusinessOutcomeName);
        var jiraId = Trimmed(item.BusinessOutcomeJiraId);

        if (name is null)
        {
            return jiraId;
        }

        return jiraId is null ? name : $"{jiraId} — {name}";
    }

    public static string? FieldValue(FeatureSnapshotItem? item, string field)
    {
        if (item is null)
        {
            return null;
        }

        return field switch
        {
            FeatureDeltaFields.Art => Trimmed(item.ArtName),
            FeatureDeltaFields.Pi => Trimmed(item.PiName),
            FeatureDeltaFields.Labels => Trimmed(item.Labels),
            FeatureDeltaFields.BusinessOutcome => BusinessOutcomeText(item),
            FeatureDeltaFields.TargetStart => FormatDate(item.TargetStart),
            FeatureDeltaFields.TargetEnd => FormatDate(item.TargetEnd),
            FeatureDeltaFields.StoryPoints => item.StoryPoints?.ToString(),
            FeatureDeltaFields.Teams => Trimmed(item.Teams),
            FeatureDeltaFields.RequirementStatus => Trimmed(item.RequirementStatus),
            FeatureDeltaFields.FundingStatus => Trimmed(item.FundingStatus),
            FeatureDeltaFields.Summary => Trimmed(item.Summary),
            FeatureDeltaFields.Name => Trimmed(item.Name),
            FeatureDeltaFields.AcceptanceCriteria => Trimmed(item.AcceptanceCriteria),
            FeatureDeltaFields.PiObjective => Trimmed(item.PiObjective),
            FeatureDeltaFields.RagExplain => Trimmed(item.RagExplain),
            _ => null
        };
    }

    public static string FormatDate(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? string.Empty;

    public static string KindLabel(FeatureDeltaChangeKind kind) => kind switch
    {
        FeatureDeltaChangeKind.Changed => "CHANGED",
        FeatureDeltaChangeKind.Added => "ADDED",
        FeatureDeltaChangeKind.Removed => "REMOVED",
        _ => "UNCHANGED"
    };

    // Case-insensitive set equality expressed as a string: SplitValues already de-duplicates and sorts
    // ignoring case, upper-casing afterwards makes ordinal equality of the joined string equivalent.
    private static string? CanonicalSet(string? value)
    {
        var values = SplitValues(value);
        return values.Count == 0 ? null : string.Join(",", values.Select(v => v.ToUpperInvariant()));
    }

    private static Dictionary<string, FeatureSnapshotItem> BuildJiraIndex(IEnumerable<FeatureSnapshotItem> items)
    {
        var index = new Dictionary<string, FeatureSnapshotItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var key = NormalizeJiraId(item.JiraId);
            if (key is not null)
            {
                index.TryAdd(key, item);
            }
        }

        return index;
    }

    private static Dictionary<int, FeatureSnapshotItem> BuildFeatureIndex(IEnumerable<FeatureSnapshotItem> items)
    {
        var index = new Dictionary<int, FeatureSnapshotItem>();

        foreach (var item in items)
        {
            index.TryAdd(item.FeatureId, item);
        }

        return index;
    }

    private static string RowKey(FeatureSnapshotItem item) =>
        NormalizeJiraId(item.JiraId) ?? $"#{item.FeatureId}";

    private static string? NormalizeJiraId(string? jiraId) =>
        string.IsNullOrWhiteSpace(jiraId) ? null : jiraId.Trim();

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FormatNullableDate(DateTime? value) => value?.ToString("yyyy-MM-dd");

    private static List<string> SplitValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
