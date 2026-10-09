using Estimation.Core.Capacity.Models;
using Estimation.Core.Capacity.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Estimation.Core.Tests.Capacity.ArtOrderTestData;

namespace Estimation.Core.Tests.Capacity;

public class ArtPrioritizationServiceTests
{
    private const ArtPrioritization Pe = ArtPrioritization.PortfolioEpic;
    private const ArtPrioritization Bo = ArtPrioritization.BusinessOutcome;

    private readonly InMemoryDatabase _db = new();
    private readonly ArtPrioritizationService _service;

    public ArtPrioritizationServiceTests()
    {
        _service = new ArtPrioritizationService(_db);
    }

    private Task SaveRowsAsync(int? piId, ArtPrioritization level, params (int? ItemId, int SortOrder, bool IsIncluded)[] rows) =>
        ArtOrderTestData.SaveRowsAsync(_db, CorePayments, piId, level, rows);

    private async Task<ArtPrioritizationBoard> GetAsync(int? piId)
    {
        var board = await _service.GetAsync(CorePayments, piId);
        Assert.NotNull(board);
        return board;
    }

    private Task<List<ArtPrioritizationOrder>> RowsAsync(int? piId, ArtPrioritization level = Pe) =>
        _db.ReadAsync(db => db.ArtPrioritizationOrders
            .Where(o => o.CapitalProjectId == CorePayments && o.PiId == piId && o.Level == level)
            .OrderBy(o => o.SortOrder)
            .ToListAsync());

    private static List<(int Id, int Rank, bool IsIncluded, ArtOrderOrigin Origin)> Shape(ArtPrioritizationBoard board) =>
        board.Items.Select(i => (i.Id, i.Rank, i.IsIncluded, i.Origin)).ToList();

    [Fact]
    public async Task An_unknown_art_gives_null()
    {
        await SeedAsync(_db);

        Assert.Null(await _service.GetAsync(99, null));
    }

    [Fact]
    public async Task An_art_without_a_level_has_no_cards()
    {
        await SeedAsync(_db, corePayments: null);

        var board = await GetAsync(CurrentPi);

        Assert.Null(board.Level);
        Assert.Equal(ArtOrderMode.Empty, board.Mode);
        Assert.Empty(board.Items);
    }

    [Fact]
    public async Task Without_any_saved_order_the_general_scope_lists_its_items_as_new_by_jira_key()
    {
        await SeedAsync(_db);

        var board = await GetAsync(null);

        Assert.Null(board.PiId);
        Assert.Equal(ArtOrderMode.Empty, board.Mode);
        Assert.False(board.HasGeneralOrder);
        Assert.Equal([1, 2, 3, ArtBoardItems.None], board.Items.Select(i => i.Id));
        Assert.All(board.Items, i => Assert.Equal(ArtOrderOrigin.New, i.Origin));
        Assert.Equal([1, 8], board.Items[0].FeatureIds);
        Assert.Equal([5, 4], board.Items[3].FeatureIds);
        Assert.True(board.Items[3].IsNone);
        Assert.Equal([1, 2, 3, 4, 5, 8], board.Features.Keys.Order());
        Assert.Equal("Martin Vale", board.Items[0].L6Owner);
        Assert.Equal("Board ask", board.Items[0].Comments);
    }

    [Fact]
    public async Task The_general_order_keeps_saved_items_and_adds_new_ones_above_the_line()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(null, Pe, (3, 1, true), (Featureless, 2, true), (1, 3, false));

        var board = await GetAsync(null);

        Assert.Equal(ArtOrderMode.General, board.Mode);
        Assert.True(board.HasGeneralOrder);
        Assert.Equal(
        [
            (3, 1, true, ArtOrderOrigin.Saved),
            (Featureless, 2, true, ArtOrderOrigin.Saved),
            (2, 3, true, ArtOrderOrigin.New),
            (ArtBoardItems.None, 4, true, ArtOrderOrigin.New),
            (1, 5, false, ArtOrderOrigin.Saved),
        ],
        Shape(board));
        var featureless = board.Items.Single(i => i.Id == Featureless);
        Assert.False(featureless.HasFeaturesInScope);
        Assert.True(featureless.IsSaved);
        Assert.Equal("EPIC-4", featureless.JiraId);
        Assert.True(board.Items.Single(i => i.Id == 3).HasFeaturesInScope);
    }

    [Fact]
    public async Task A_pi_without_its_own_rows_follows_the_relevant_part_of_the_general_order()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(null, Pe, (3, 1, true), (Featureless, 2, true), (2, 3, true), (1, 4, false), (OtherArtOnly, 5, true));

        var board = await GetAsync(CurrentPi);

        Assert.Equal(ArtOrderMode.FollowsGeneral, board.Mode);
        Assert.Equal(
        [
            (Featureless, 1, true, ArtOrderOrigin.FromGeneral),
            (2, 2, true, ArtOrderOrigin.FromGeneral),
            (OtherArtOnly, 3, true, ArtOrderOrigin.FromGeneral),
            (PastOnly, 4, true, ArtOrderOrigin.New),
            (ArtBoardItems.None, 5, true, ArtOrderOrigin.New),
            (1, 6, false, ArtOrderOrigin.FromGeneral),
        ],
        Shape(board));
        Assert.Equal([9], board.Items.Single(i => i.Id == PastOnly).FeatureIds);
        Assert.Equal("PI 26.07", board.Features[9].PiName);
    }

    [Fact]
    public async Task In_a_pi_only_featureless_items_of_the_general_order_are_kept_by_it()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(null, Pe, (Featureless, 1, true), (3, 2, true));
        await SaveRowsAsync(CurrentPi, Pe, (3, 1, true), (Featureless, 2, true));

        var board = await GetAsync(CurrentPi);

        Assert.True(board.Items.Single(i => i.Id == Featureless).IsKeptByGeneralOrder);
        Assert.False(board.Items.Single(i => i.Id == 3).IsKeptByGeneralOrder);
        Assert.False(board.Items.Single(i => i.Id == 1).IsKeptByGeneralOrder);
        Assert.All((await GetAsync(null)).Items, i => Assert.False(i.IsKeptByGeneralOrder));
    }

    [Theory]
    [InlineData("Done", true)]
    [InlineData(" closed ", true)]
    [InlineData("Resolved", true)]
    [InlineData("Accepted for Release", true)]
    [InlineData("rejected", true)]
    [InlineData("Cancelled", true)]
    [InlineData("Canceled", true)]
    [InlineData("In Progress", false)]
    [InlineData(null, false)]
    public void Done_like_statuses_include_finished_and_rejected_ones(string? status, bool expected) =>
        Assert.Equal(expected, ArtPrioritizationService.IsDoneStatus(status));

    [Fact]
    public async Task A_pi_with_its_own_rows_merges_the_general_order_after_them()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(null, Pe, (1, 1, true), (3, 2, true), (PastOnly, 3, false));
        await SaveRowsAsync(CurrentPi, Pe, (2, 1, true), (null, 2, false));

        var board = await GetAsync(CurrentPi);

        Assert.Equal(ArtOrderMode.PiOrder, board.Mode);
        Assert.True(board.HasGeneralOrder);
        Assert.Equal(
        [
            (2, 1, true, ArtOrderOrigin.Saved),
            (1, 2, true, ArtOrderOrigin.FromGeneral),
            (ArtBoardItems.None, 3, false, ArtOrderOrigin.Saved),
            (PastOnly, 4, false, ArtOrderOrigin.FromGeneral),
        ],
        Shape(board));
    }

    [Fact]
    public async Task A_pi_without_any_order_is_empty_and_lists_its_items_as_new()
    {
        await SeedAsync(_db);

        var board = await GetAsync(CurrentPi);

        Assert.Equal(ArtOrderMode.Empty, board.Mode);
        Assert.Equal([1, 2, PastOnly, ArtBoardItems.None], board.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Outcome_cards_carry_their_parent_epic()
    {
        await SeedAsync(_db, corePayments: Bo);

        var board = await GetAsync(CurrentPi);

        Assert.Equal(Bo, board.Level);
        Assert.Equal([1, 2, 4, 7, ArtBoardItems.None], board.Items.Select(i => i.Id));
        var outcome = board.Items.Single(i => i.Id == 1);
        Assert.Equal("EPIC-1", outcome.ParentJiraId);
        Assert.Equal("Instant payments", outcome.ParentSummary);
        Assert.Equal("Amber", outcome.RagStatus);
        Assert.Null(board.Items.Single(i => i.Id == 4).ParentJiraId);
        Assert.Equal([4], board.Items.Single(i => i.IsNone).FeatureIds);
        Assert.Equal("BO-1", board.Features[1].BusinessOutcomeJiraId);
    }

    [Fact]
    public async Task Save_writes_a_normalised_order_and_skips_unknown_items_and_duplicates()
    {
        await SeedAsync(_db);

        await _service.SaveAsync(CorePayments, null, Pe,
        [
            new ArtPrioritizationOrderRow(2, 5, false),
            new ArtPrioritizationOrderRow(1, 10, true),
            new ArtPrioritizationOrderRow(ArtBoardItems.None, 7, true),
            new ArtPrioritizationOrderRow(999, 1, true),
            new ArtPrioritizationOrderRow(1, 3, true),
        ]);

        var rows = await RowsAsync(null);
        Assert.Equal([(int?)1, null, 2], rows.Select(r => r.PortfolioEpicId));
        Assert.Equal([1, 2, 3], rows.Select(r => r.SortOrder));
        Assert.Equal([true, true, false], rows.Select(r => r.IsIncluded));
        Assert.All(rows, r => Assert.Null(r.BusinessOutcomeId));
    }

    [Fact]
    public async Task Save_updates_existing_rows_and_drops_the_ones_left_out()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(CurrentPi, Pe, (1, 1, true), (2, 2, true), (3, 3, true));
        var before = await RowsAsync(CurrentPi);

        await _service.SaveAsync(CorePayments, CurrentPi, Pe,
        [
            new ArtPrioritizationOrderRow(3, 1, false),
            new ArtPrioritizationOrderRow(1, 2, true),
        ]);

        var rows = await RowsAsync(CurrentPi);
        Assert.Equal([(int?)1, 3], rows.Select(r => r.PortfolioEpicId));
        Assert.Equal([true, false], rows.Select(r => r.IsIncluded));
        Assert.Equal(before.Single(r => r.PortfolioEpicId == 3).Id, rows.Single(r => r.PortfolioEpicId == 3).Id);
    }

    [Fact]
    public async Task Saving_a_pi_leaves_the_general_order_other_pis_and_levels_alone()
    {
        await SeedAsync(_db, corePayments: Bo);
        await SaveRowsAsync(null, Bo, (1, 1, true));
        await SaveRowsAsync(NextPi, Bo, (2, 1, false));
        await SaveRowsAsync(CurrentPi, Pe, (1, 1, false));

        await _service.SaveAsync(CorePayments, CurrentPi, Bo, [new ArtPrioritizationOrderRow(2, 1, true)]);

        Assert.Equal((int?)2, Assert.Single(await RowsAsync(CurrentPi, Bo)).BusinessOutcomeId);
        Assert.Equal((int?)1, Assert.Single(await RowsAsync(null, Bo)).BusinessOutcomeId);
        Assert.Single(await RowsAsync(NextPi, Bo));
        Assert.Single(await RowsAsync(CurrentPi, Pe));
    }

    [Fact]
    public async Task Saving_the_general_order_freezes_locked_pis_that_follow_it()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(null, Pe, (3, 1, true), (1, 2, true), (PastOnly, 3, false));
        await SaveRowsAsync(OwnPi, Pe, (2, 1, true));

        await _service.SaveAsync(CorePayments, null, Pe,
        [
            new ArtPrioritizationOrderRow(PastOnly, 1, true),
            new ArtPrioritizationOrderRow(1, 2, true),
            new ArtPrioritizationOrderRow(3, 3, false),
        ]);

        Assert.Equal([(int?)PastOnly, 1, 3], (await RowsAsync(null)).Select(r => r.PortfolioEpicId));

        var past = await RowsAsync(PastPi);
        Assert.Equal([((int?)PastOnly, 1, false)], past.Select(r => (r.PortfolioEpicId, r.SortOrder, r.IsIncluded)));
        var next = await RowsAsync(NextPi);
        Assert.Equal([((int?)3, 1, true)], next.Select(r => (r.PortfolioEpicId, r.SortOrder, r.IsIncluded)));

        Assert.Empty(await RowsAsync(CurrentPi));
        Assert.Empty(await RowsAsync(QuietPi));
        Assert.Equal([(int?)2], (await RowsAsync(OwnPi)).Select(r => r.PortfolioEpicId));

        var pastBoard = await GetAsync(PastPi);
        Assert.Equal(ArtOrderMode.PiOrder, pastBoard.Mode);
        Assert.Equal([(PastOnly, false)], pastBoard.Items.Select(i => (i.Id, i.IsIncluded)));
    }

    [Fact]
    public async Task The_first_general_order_freezes_locked_pis_with_all_their_items_above_the_line()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(CurrentPi, Bo, (1, 1, true));

        await _service.SaveAsync(CorePayments, null, Pe, [new ArtPrioritizationOrderRow(3, 1, false)]);

        Assert.Equal([((int?)3, 1, false)], (await RowsAsync(null)).Select(r => (r.PortfolioEpicId, r.SortOrder, r.IsIncluded)));
        Assert.Equal([((int?)PastOnly, 1, true)], (await RowsAsync(PastPi)).Select(r => (r.PortfolioEpicId, r.SortOrder, r.IsIncluded)));
        Assert.Equal([((int?)3, 1, true)], (await RowsAsync(NextPi)).Select(r => (r.PortfolioEpicId, r.SortOrder, r.IsIncluded)));
        Assert.Empty(await RowsAsync(CurrentPi));
        Assert.Empty(await RowsAsync(QuietPi));
        Assert.Empty(await RowsAsync(OwnPi));

        var nextBoard = await GetAsync(NextPi);
        Assert.Equal(ArtOrderMode.PiOrder, nextBoard.Mode);
        Assert.Equal([(3, true)], nextBoard.Items.Select(i => (i.Id, i.IsIncluded)));
        Assert.Equal(ArtOrderMode.FollowsGeneral, (await GetAsync(CurrentPi)).Mode);
    }

    [Fact]
    public async Task Use_general_order_deletes_only_the_pi_rows()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(null, Pe, (1, 1, true));
        await SaveRowsAsync(CurrentPi, Pe, (2, 1, true), (1, 2, false));
        await SaveRowsAsync(NextPi, Pe, (3, 1, true));

        await _service.UseGeneralOrderAsync(CorePayments, CurrentPi, Pe);

        Assert.Empty(await RowsAsync(CurrentPi));
        Assert.Single(await RowsAsync(null));
        Assert.Single(await RowsAsync(NextPi));
        Assert.Equal(ArtOrderMode.FollowsGeneral, (await GetAsync(CurrentPi)).Mode);
    }

    [Fact]
    public async Task Search_matches_jira_id_or_summary_and_hides_done_items_unless_asked()
    {
        await SeedAsync(_db);

        var payments = await _service.SearchItemsAsync(Pe, "PAYMENT", includeDone: false, excludeIds: []);
        Assert.Equal([1, 10], payments.Select(i => i.Id));

        var withDone = await _service.SearchItemsAsync(Pe, "payment", includeDone: true, excludeIds: [1]);
        Assert.Equal([DoneEpic, 10], withDone.Select(i => i.Id));

        var byKey = await _service.SearchItemsAsync(Pe, "epic-1", includeDone: false, excludeIds: []);
        Assert.Equal([1, 10], byKey.Select(i => i.Id));

        var all = await _service.SearchItemsAsync(Pe, "", includeDone: false, excludeIds: [], take: 3);
        Assert.Equal([1, 2, 3], all.Select(i => i.Id));
    }

    [Fact]
    public async Task Search_at_outcome_level_looks_at_business_outcomes()
    {
        await SeedAsync(_db, corePayments: Bo);

        var outcomes = await _service.SearchItemsAsync(Bo, "outcome", includeDone: false, excludeIds: []);

        Assert.Equal([4, 8], outcomes.Select(i => i.Id));
        Assert.Equal("BO-4", outcomes[0].JiraId);
    }

    [Fact]
    public async Task Get_item_returns_a_new_card_without_features()
    {
        await SeedAsync(_db);

        var item = await _service.GetItemAsync(Pe, Featureless);

        Assert.NotNull(item);
        Assert.Equal("EPIC-4", item.JiraId);
        Assert.Equal("In Progress", item.Status);
        Assert.Equal(ArtOrderOrigin.New, item.Origin);
        Assert.False(item.HasFeaturesInScope);
        Assert.Null(await _service.GetItemAsync(Pe, 999));
        Assert.True((await _service.GetItemAsync(Pe, ArtBoardItems.None))!.IsNone);
    }

    [Fact]
    public async Task A_saved_order_reaches_the_team_boards_of_the_art()
    {
        await SeedAsync(_db);
        await _service.SaveAsync(CorePayments, null, Pe,
        [
            new ArtPrioritizationOrderRow(2, 1, true),
            new ArtPrioritizationOrderRow(1, 2, false),
        ]);

        var general = await new TeamCapacityService(_db).GetAsync(Argon, CurrentPi);
        Assert.Equal(ArtOrderMode.FollowsGeneral, general.Prioritization.OrderArt?.Kind);
        Assert.Equal([9, 2, 4, 1], general.Features.Select(f => f.FeatureId));
        Assert.Equal([true, true, true, false], general.Features.Select(f => f.IsIncludedInCapacity));

        await _service.SaveAsync(CorePayments, CurrentPi, Pe,
        [
            new ArtPrioritizationOrderRow(ArtBoardItems.None, 1, true),
            new ArtPrioritizationOrderRow(1, 2, true),
        ]);

        var own = await new TeamCapacityService(_db).GetAsync(Argon, CurrentPi);
        Assert.Equal(ArtOrderMode.PiOrder, own.Prioritization.OrderArt?.Kind);
        Assert.Equal([9, 4, 1, 2], own.Features.Select(f => f.FeatureId));
        Assert.Equal([(int?)4, 1, 2, 3], own.Features.Select(f => f.ArtBoardPosition));
    }
}
