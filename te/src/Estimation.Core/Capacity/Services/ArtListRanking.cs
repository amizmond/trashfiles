namespace Estimation.Core.Capacity.Services;

public sealed record ArtListRanking(string? Text, int? Number, int? SortKey)
{
    public static readonly ArtListRanking None = new(null, null, null);

    public static ArtListRanking For(IEnumerable<ArtRank> ranks, IReadOnlySet<int> artIds, bool singleArt)
    {
        var shown = ranks
            .Where(r => artIds.Contains(r.ArtId))
            .OrderBy(r => r.ArtName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ArtId)
            .ToList();
        if (shown.Count == 0)
        {
            return None;
        }

        var sortKey = shown.Min(r => r.Rank);
        if (singleArt && shown.Count == 1)
        {
            return new ArtListRanking(RankText(shown[0]), shown[0].Rank, sortKey);
        }

        return new ArtListRanking(string.Join("; ", shown.Select(r => $"{r.ArtName}: {RankText(r)}")), null, sortKey);
    }

    public static Dictionary<int, ArtListRanking> Build(
        IReadOnlyDictionary<int, IReadOnlyList<ArtRank>> ranksByItem, IReadOnlySet<int> artIds, bool singleArt)
    {
        var result = new Dictionary<int, ArtListRanking>();
        foreach (var (itemId, ranks) in ranksByItem)
        {
            var ranking = For(ranks, artIds, singleArt);
            if (ranking != None)
            {
                result[itemId] = ranking;
            }
        }

        return result;
    }

    private static string RankText(ArtRank rank) => rank.IsBelowLine ? $"{rank.Rank} (below line)" : $"{rank.Rank}";
}
