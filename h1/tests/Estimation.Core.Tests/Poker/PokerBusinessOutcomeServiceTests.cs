using Estimation.Core.Features.Models;
using Estimation.Core.Poker.Services;
using Estimation.Core.Tests.Administration;
using Estimation.Core.Train.Models;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Estimation.Core.Tests.Poker;

[Collection(LocalDbCollection.Name)]
public class PokerBusinessOutcomeServiceTests
{
    private readonly LocalDbFixture _localDb;

    public PokerBusinessOutcomeServiceTests(LocalDbFixture localDb)
    {
        _localDb = localDb;
    }

    [LocalDbFact]
    public async Task ResolveAsync_PrefersTheParentLink_ThenFollowsTheFeatureLink()
    {
        await SeedAsync();
        var service = new PokerBusinessOutcomeService(_localDb.Factory);

        var direct = await service.ResolveAsync("BO-1", null);
        Assert.Equal("BO-1", direct?.Key);
        Assert.Equal("Faster onboarding", direct?.Name);

        Assert.Equal("BO-1", (await service.ResolveAsync(null, "FEAT-1"))?.Key);

        Assert.Equal("BO-1", (await service.ResolveAsync("NOPE-9", "FEAT-1"))?.Key);
    }

    [LocalDbFact]
    public async Task ResolveAsync_ReturnsNull_WhenNeitherKeyLeadsToAnOutcome()
    {
        await SeedAsync();
        var service = new PokerBusinessOutcomeService(_localDb.Factory);

        Assert.Null(await service.ResolveAsync(null, null));
        Assert.Null(await service.ResolveAsync("  ", ""));
        Assert.Null(await service.ResolveAsync("NOPE-9", "NOPE-8"));
        Assert.Null(await service.ResolveAsync(null, "FEAT-2"));
    }

    private async Task SeedAsync()
    {
        await using var db = await _localDb.Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM Features WHERE JiraId IN ('FEAT-1', 'FEAT-2'); " +
            "DELETE FROM BusinessOutcomes WHERE JiraId = 'BO-1';");

        var outcome = new BusinessOutcome { JiraId = "BO-1", Summary = "Faster onboarding" };
        db.BusinessOutcomes.Add(outcome);
        db.Features.Add(new Feature { JiraId = "FEAT-1", Summary = "Sign-up flow", BusinessOutcome = outcome });
        db.Features.Add(new Feature { JiraId = "FEAT-2", Summary = "Feature without an outcome" });
        await db.SaveChangesAsync();
    }
}
