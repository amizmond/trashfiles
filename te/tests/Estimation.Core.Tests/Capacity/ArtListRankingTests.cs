using Estimation.Core.Capacity.Services;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class ArtListRankingTests
{
    private static readonly ArtRank Payments = new(1, "Core Payments", 2, false, false);
    private static readonly ArtRank MobileBelow = new(2, "Mobile", 5, true, false);
    private static readonly ArtRank Atlas = new(3, "atlas", 7, false, true);

    [Fact]
    public void Several_arts_are_listed_by_name_with_the_below_line_marker()
    {
        var ranking = ArtListRanking.For([MobileBelow, Payments, Atlas], new HashSet<int> { 1, 2, 3 }, singleArt: false);

        Assert.Equal("atlas: 7; Core Payments: 2; Mobile: 5 (below line)", ranking.Text);
        Assert.Null(ranking.Number);
        Assert.Equal(2, ranking.SortKey);
    }

    [Fact]
    public void One_selected_art_shows_just_its_number()
    {
        var ranking = ArtListRanking.For([Payments, MobileBelow], new HashSet<int> { 1 }, singleArt: true);

        Assert.Equal(new ArtListRanking("2", 2, 2), ranking);
    }

    [Fact]
    public void One_selected_art_below_the_line_says_so()
    {
        var ranking = ArtListRanking.For([Payments, MobileBelow], new HashSet<int> { 2 }, singleArt: true);

        Assert.Equal(new ArtListRanking("5 (below line)", 5, 5), ranking);
    }

    [Fact]
    public void A_single_art_without_a_selection_still_names_the_art()
    {
        var ranking = ArtListRanking.For([Payments], new HashSet<int> { 1, 2 }, singleArt: false);

        Assert.Equal(new ArtListRanking("Core Payments: 2", null, 2), ranking);
    }

    [Fact]
    public void Arts_outside_the_considered_set_are_ignored()
    {
        Assert.Same(ArtListRanking.None, ArtListRanking.For([Payments, MobileBelow], new HashSet<int> { 3 }, singleArt: false));
    }

    [Fact]
    public void Build_leaves_out_items_without_a_considered_rank()
    {
        var ranks = new Dictionary<int, IReadOnlyList<ArtRank>>
        {
            [10] = [Payments],
            [11] = [MobileBelow],
        };

        var built = ArtListRanking.Build(ranks, new HashSet<int> { 2 }, singleArt: true);

        Assert.Equal([11], built.Keys);
        Assert.Equal("5 (below line)", built[11].Text);
    }
}
