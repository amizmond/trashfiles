using Estimation.Core.Capacity.Services;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class ArtOrderResolverTests
{
    private const ArtOrderOrigin Saved = ArtOrderOrigin.Saved;
    private const ArtOrderOrigin FromGeneral = ArtOrderOrigin.FromGeneral;
    private const ArtOrderOrigin New = ArtOrderOrigin.New;

    private static ArtOrderRow Row(int itemId, int sortOrder, bool isIncluded = true, int id = 0) =>
        new(itemId, sortOrder, isIncluded, id);

    private static ArtOrderItem Item(int itemId) =>
        new(itemId, itemId == ArtBoardItems.None ? null : $"EPIC-{itemId}");

    private static ArtOrderItem[] Items(params int[] ids) => ids.Select(Item).ToArray();

    private static List<(int ItemId, int Rank, bool IsIncluded, ArtOrderOrigin Origin)> Shape(ArtOrderResolution resolution) =>
        resolution.Entries.Select(e => (e.ItemId, e.Rank, e.IsIncluded, e.Origin)).ToList();

    [Fact]
    public void Normalize_puts_above_first_then_sort_order_then_id_and_keeps_the_first_of_a_duplicate()
    {
        var rows = ArtOrderResolver.Normalize(
        [
            Row(1, 1, isIncluded: false, id: 1),
            Row(2, 7, id: 2),
            Row(3, 3, id: 4),
            Row(4, 3, id: 3),
            Row(2, 9, id: 5),
        ]);

        Assert.Equal([4, 3, 2, 1], rows.Select(r => r.ItemId));
        Assert.Equal(7, rows.Single(r => r.ItemId == 2).SortOrder);
    }

    [Fact]
    public void General_keeps_the_saved_rows_and_adds_new_relevant_items_at_the_end_of_the_above_group()
    {
        var resolution = ArtOrderResolver.ResolveGeneral(
            [Row(5, 1), Row(7, 2), Row(9, 3, isIncluded: false)],
            Items(9, 7, ArtBoardItems.None, 12, 3, 5));

        Assert.Equal(ArtOrderMode.General, resolution.Mode);
        Assert.Equal(
        [
            (5, 1, true, Saved),
            (7, 2, true, Saved),
            (3, 3, true, New),
            (12, 4, true, New),
            (ArtBoardItems.None, 5, true, New),
            (9, 6, false, Saved),
        ],
        Shape(resolution));
    }

    [Fact]
    public void General_numbers_a_non_normalised_saved_order_above_first()
    {
        var resolution = ArtOrderResolver.ResolveGeneral(
            [Row(1, 10, isIncluded: false), Row(2, 40), Row(3, 20), Row(4, 5, isIncluded: false)],
            []);

        Assert.Equal(
        [
            (3, 1, true, Saved),
            (2, 2, true, Saved),
            (4, 3, false, Saved),
            (1, 4, false, Saved),
        ],
        Shape(resolution));
    }

    [Fact]
    public void General_without_saved_rows_is_empty_and_lists_the_relevant_items_as_new_by_jira_key()
    {
        var resolution = ArtOrderResolver.ResolveGeneral([], [Item(ArtBoardItems.None), Item(10), Item(2), Item(9)]);

        Assert.Equal(ArtOrderMode.Empty, resolution.Mode);
        Assert.Equal([2, 9, 10, ArtBoardItems.None], resolution.Entries.Select(e => e.ItemId));
        Assert.All(resolution.Entries, e => Assert.Equal(New, e.Origin));
    }

    [Fact]
    public void A_pi_with_its_own_rows_merges_general_and_new_items_around_its_own_order()
    {
        var resolution = ArtOrderResolver.ResolvePi(
            own: [Row(3, 1), Row(1, 2), Row(4, 3, isIncluded: false)],
            general: [Row(1, 1), Row(2, 2), Row(6, 3), Row(5, 4, isIncluded: false), Row(8, 5, isIncluded: false)],
            relevant: Items(1, 2, 3, 5, 7));

        Assert.Equal(ArtOrderMode.PiOrder, resolution.Mode);
        Assert.Equal(
        [
            (3, 1, true, Saved),
            (1, 2, true, Saved),
            (2, 3, true, FromGeneral),
            (7, 4, true, New),
            (4, 5, false, Saved),
            (5, 6, false, FromGeneral),
        ],
        Shape(resolution));
    }

    [Fact]
    public void A_pi_keeps_its_own_rows_even_when_their_items_are_no_longer_relevant()
    {
        var resolution = ArtOrderResolver.ResolvePi(
            own: [Row(1, 1), Row(2, 2, isIncluded: false)],
            general: [],
            relevant: Items(3));

        Assert.Equal(
        [
            (1, 1, true, Saved),
            (3, 2, true, New),
            (2, 3, false, Saved),
        ],
        Shape(resolution));
    }

    [Fact]
    public void A_pi_following_general_shows_only_the_relevant_general_items_with_the_general_line()
    {
        var resolution = ArtOrderResolver.ResolvePi(
            own: [],
            general: [Row(4, 1), Row(1, 2), Row(9, 3), Row(2, 4, isIncluded: false), Row(8, 5, isIncluded: false)],
            relevant: Items(1, 2, 4, 6));

        Assert.Equal(ArtOrderMode.FollowsGeneral, resolution.Mode);
        Assert.Equal(
        [
            (4, 1, true, FromGeneral),
            (1, 2, true, FromGeneral),
            (6, 3, true, New),
            (2, 4, false, FromGeneral),
        ],
        Shape(resolution));
    }

    [Fact]
    public void A_pi_following_general_normalises_a_non_normalised_general_order()
    {
        var resolution = ArtOrderResolver.ResolvePi(
            own: [],
            general: [Row(1, 9, isIncluded: false), Row(2, 30), Row(3, 4)],
            relevant: Items(1, 2, 3));

        Assert.Equal(
        [
            (3, 1, true, FromGeneral),
            (2, 2, true, FromGeneral),
            (1, 3, false, FromGeneral),
        ],
        Shape(resolution));
    }

    [Fact]
    public void A_pi_without_own_or_general_rows_is_empty()
    {
        var resolution = ArtOrderResolver.ResolvePi([], [], Items(2, 1));

        Assert.Equal(ArtOrderMode.Empty, resolution.Mode);
        Assert.Equal([1, 2], resolution.Entries.Select(e => e.ItemId));
        Assert.All(resolution.Entries, e => Assert.Equal(New, e.Origin));
    }

    [Fact]
    public void Nothing_relevant_and_nothing_saved_gives_an_empty_list()
    {
        Assert.Empty(ArtOrderResolver.ResolvePi([], [], []).Entries);
        Assert.Empty(ArtOrderResolver.ResolveGeneral([], []).Entries);
    }

    [Fact]
    public void Above_the_line_items_are_always_ranked_before_the_ones_below()
    {
        var resolution = ArtOrderResolver.ResolvePi(
            own: [Row(1, 1, isIncluded: false), Row(2, 2)],
            general: [Row(3, 1, isIncluded: false), Row(4, 2)],
            relevant: Items(1, 2, 3, 4, 5));

        var above = resolution.Entries.Where(e => e.IsIncluded).Select(e => e.Rank).ToList();
        var below = resolution.Entries.Where(e => !e.IsIncluded).Select(e => e.Rank).ToList();
        Assert.Equal([1, 2, 3], above);
        Assert.Equal([4, 5], below);
    }

    [Fact]
    public void Jira_keys_compare_by_project_then_number()
    {
        Assert.True(ArtOrderResolver.CompareJiraKeys("EPIC-2", "EPIC-10") < 0);
        Assert.True(ArtOrderResolver.CompareJiraKeys("ABC-10", "EPIC-2") < 0);
        Assert.True(ArtOrderResolver.CompareJiraKeys("epic-3", "EPIC-3") == 0);
        Assert.True(ArtOrderResolver.CompareJiraKeys("EPIC-3", null) < 0);
    }
}
