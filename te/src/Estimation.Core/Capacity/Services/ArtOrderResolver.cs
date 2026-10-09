namespace Estimation.Core.Capacity.Services;

public enum ArtOrderOrigin
{
    Saved,
    FromGeneral,
    New,
}

public enum ArtOrderMode
{
    Empty,
    General,
    PiOrder,
    FollowsGeneral,
}

public sealed record ArtOrderRow(int ItemId, int SortOrder, bool IsIncluded, int Id = 0);

public sealed record ArtOrderItem(int ItemId, string? JiraId);

public sealed record ArtOrderEntry(int ItemId, int Rank, bool IsIncluded, ArtOrderOrigin Origin);

public sealed record ArtOrderResolution(ArtOrderMode Mode, IReadOnlyList<ArtOrderEntry> Entries)
{
    public static ArtOrderResolution Empty { get; } = new(ArtOrderMode.Empty, []);
}

public static class ArtOrderResolver
{
    public static List<ArtOrderRow> Normalize(IEnumerable<ArtOrderRow> rows) =>
        rows.OrderByDescending(r => r.IsIncluded)
            .ThenBy(r => r.SortOrder)
            .ThenBy(r => r.Id)
            .DistinctBy(r => r.ItemId)
            .ToList();

    public static ArtOrderResolution ResolveGeneral(IEnumerable<ArtOrderRow> general, IEnumerable<ArtOrderItem> relevant)
    {
        var saved = Normalize(general);
        var newItems = NewItems(relevant, saved.Select(r => r.ItemId).ToHashSet());
        if (saved.Count == 0)
        {
            return new ArtOrderResolution(ArtOrderMode.Empty, Number(newItems.Select(id => (id, true, ArtOrderOrigin.New))));
        }

        return new ArtOrderResolution(ArtOrderMode.General, Number(
            saved.Where(r => r.IsIncluded).Select(r => (r.ItemId, true, ArtOrderOrigin.Saved))
                .Concat(newItems.Select(id => (id, true, ArtOrderOrigin.New)))
                .Concat(saved.Where(r => !r.IsIncluded).Select(r => (r.ItemId, false, ArtOrderOrigin.Saved)))));
    }

    public static ArtOrderResolution ResolvePi(
        IEnumerable<ArtOrderRow> own, IEnumerable<ArtOrderRow> general, IEnumerable<ArtOrderItem> relevant)
    {
        var relevantItems = relevant.ToList();
        var relevantIds = relevantItems.Select(i => i.ItemId).ToHashSet();
        var ownRows = Normalize(own);
        var generalRows = Normalize(general);
        var ownIds = ownRows.Select(r => r.ItemId).ToHashSet();
        var fromGeneral = generalRows.Where(r => !ownIds.Contains(r.ItemId) && relevantIds.Contains(r.ItemId)).ToList();
        var newItems = NewItems(
            relevantItems,
            ownIds.Concat(generalRows.Select(r => r.ItemId)).ToHashSet());

        if (ownRows.Count > 0)
        {
            return new ArtOrderResolution(ArtOrderMode.PiOrder, Number(
                ownRows.Where(r => r.IsIncluded).Select(r => (r.ItemId, true, ArtOrderOrigin.Saved))
                    .Concat(fromGeneral.Where(r => r.IsIncluded).Select(r => (r.ItemId, true, ArtOrderOrigin.FromGeneral)))
                    .Concat(newItems.Select(id => (id, true, ArtOrderOrigin.New)))
                    .Concat(ownRows.Where(r => !r.IsIncluded).Select(r => (r.ItemId, false, ArtOrderOrigin.Saved)))
                    .Concat(fromGeneral.Where(r => !r.IsIncluded).Select(r => (r.ItemId, false, ArtOrderOrigin.FromGeneral)))));
        }

        if (generalRows.Count == 0)
        {
            return new ArtOrderResolution(ArtOrderMode.Empty, Number(newItems.Select(id => (id, true, ArtOrderOrigin.New))));
        }

        return new ArtOrderResolution(ArtOrderMode.FollowsGeneral, Number(
            fromGeneral.Where(r => r.IsIncluded).Select(r => (r.ItemId, true, ArtOrderOrigin.FromGeneral))
                .Concat(newItems.Select(id => (id, true, ArtOrderOrigin.New)))
                .Concat(fromGeneral.Where(r => !r.IsIncluded).Select(r => (r.ItemId, false, ArtOrderOrigin.FromGeneral)))));
    }

    public static int CompareJiraKeys(string? left, string? right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return string.IsNullOrEmpty(left).CompareTo(string.IsNullOrEmpty(right));
        }

        var leftDash = left.LastIndexOf('-');
        var rightDash = right.LastIndexOf('-');
        if (leftDash > 0 && rightDash > 0
            && long.TryParse(left[(leftDash + 1)..], out var leftNumber)
            && long.TryParse(right[(rightDash + 1)..], out var rightNumber))
        {
            var project = string.Compare(left[..leftDash], right[..rightDash], StringComparison.OrdinalIgnoreCase);
            return project != 0 ? project : leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static List<int> NewItems(IEnumerable<ArtOrderItem> relevant, IReadOnlySet<int> known) =>
        relevant
            .Where(i => !known.Contains(i.ItemId))
            .DistinctBy(i => i.ItemId)
            .OrderBy(i => i.ItemId == ArtBoardItems.None)
            .ThenBy(i => i.JiraId, Comparer<string?>.Create(CompareJiraKeys))
            .ThenBy(i => i.ItemId)
            .Select(i => i.ItemId)
            .ToList();

    private static List<ArtOrderEntry> Number(IEnumerable<(int ItemId, bool IsIncluded, ArtOrderOrigin Origin)> items)
    {
        var rank = 0;
        return items.Select(i => new ArtOrderEntry(i.ItemId, ++rank, i.IsIncluded, i.Origin)).ToList();
    }
}
