using Estimation.Core.Capacity.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;
using static Estimation.Core.Tests.Capacity.ArtOrderTestData;

namespace Estimation.Core.Tests.Capacity;

public class ArtRankingLookupServiceTests
{
    private const ArtPrioritization Pe = ArtPrioritization.PortfolioEpic;
    private const ArtPrioritization Bo = ArtPrioritization.BusinessOutcome;

    private readonly InMemoryDatabase _db = new();
    private readonly ArtRankingLookupService _service;

    public ArtRankingLookupServiceTests()
    {
        _service = new ArtRankingLookupService(_db);
    }

    private async Task SeedGeneralOrdersAsync()
    {
        await SeedAsync(_db, mobile: Pe);
        await SaveRowsAsync(_db, CorePayments, null, Pe, (3, 1, true), (1, 2, false), (null, 3, true));
        await SaveRowsAsync(_db, Mobile, null, Pe, (1, 1, true), (Featureless, 2, true));
    }

    [Fact]
    public async Task General_ranks_only_saved_items_with_the_numbers_the_art_page_shows()
    {
        await SeedGeneralOrdersAsync();

        var ranks = await _service.GetAsync(Pe, null);

        Assert.Equal([new ArtRank(CorePayments, "Core Payments", 1, false, false)], ranks[3]);
        Assert.Equal(
            [new ArtRank(CorePayments, "Core Payments", 4, true, false), new ArtRank(Mobile, "Mobile", 1, false, false)],
            ranks[1]);
        Assert.Equal([new ArtRank(Mobile, "Mobile", 2, false, false)], ranks[Featureless]);
        Assert.False(ranks.ContainsKey(2));
        Assert.False(ranks.ContainsKey(ArtBoardItems.None));
    }

    [Fact]
    public async Task A_pi_following_general_ranks_the_items_it_takes_from_general()
    {
        await SeedGeneralOrdersAsync();

        var ranks = await _service.GetAsync(Pe, CurrentPi);

        Assert.Equal(
            [new ArtRank(CorePayments, "Core Payments", 4, true, false), new ArtRank(Mobile, "Mobile", 1, false, false)],
            ranks[1]);
        Assert.False(ranks.ContainsKey(2));
        Assert.False(ranks.ContainsKey(3));
    }

    [Fact]
    public async Task A_pi_with_its_own_order_is_marked_as_pi_order()
    {
        await SeedGeneralOrdersAsync();
        await SaveRowsAsync(_db, CorePayments, CurrentPi, Pe, (2, 1, true));

        var ranks = await _service.GetAsync(Pe, CurrentPi);

        Assert.Equal([new ArtRank(CorePayments, "Core Payments", 1, false, true)], ranks[2]);
        Assert.Contains(new ArtRank(CorePayments, "Core Payments", 4, true, true), ranks[1]);
        Assert.False(ranks.ContainsKey(PastOnly));
    }

    [Fact]
    public async Task Only_arts_currently_at_the_level_count()
    {
        await SeedAsync(_db);
        await SaveRowsAsync(_db, DataPlatform, null, Bo, (5, 1, true));
        await SaveRowsAsync(_db, CorePayments, null, Bo, (1, 1, true));

        var ranks = await _service.GetAsync(Bo, null);

        Assert.Equal([new ArtRank(DataPlatform, "Data Platform", 1, false, false)], ranks[5]);
        Assert.False(ranks.ContainsKey(1));
    }

    [Fact]
    public async Task Ranking_arts_by_item_cover_every_saved_scope_at_the_current_level()
    {
        await SeedAsync(_db, mobile: Pe);
        await SaveRowsAsync(_db, CorePayments, null, Pe, (3, 1, true), (null, 2, true));
        await SaveRowsAsync(_db, CorePayments, CurrentPi, Pe, (2, 1, false));
        await SaveRowsAsync(_db, CorePayments, CurrentPi, Bo, (7, 1, true));
        await SaveRowsAsync(_db, Mobile, OwnPi, Pe, (1, 1, true), (3, 2, true));
        await SaveRowsAsync(_db, DataPlatform, null, Pe, (Featureless, 1, true));

        var arts = await _service.RankingArtIdsByItemAsync(Pe);

        Assert.Equal([1, 2, 3], arts.Keys.Order());
        Assert.Equal([Mobile], arts[1]);
        Assert.Equal([CorePayments], arts[2]);
        Assert.Equal([CorePayments, Mobile], arts[3]);
    }
}
