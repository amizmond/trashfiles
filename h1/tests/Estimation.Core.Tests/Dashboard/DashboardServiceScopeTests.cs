using Estimation.Core.Dashboard.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Estimation.Core.Tests.Dashboard;

public class DashboardServiceScopeTests
{
    private const int Alpha = 1;
    private const int Beta = 2;
    private const int AlphaFeatureCount = 2;
    private const int BetaFeatureCount = 3;

    private readonly InMemoryDatabase _db = new();

    private DashboardService NewService() => new(_db, new MemoryCache(new MemoryCacheOptions()));

    private Task SeedAsync() => _db.SeedAsync(db =>
    {
        db.Pis.Add(new Pi { Id = 1, Name = "PI-1", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 3, 31) });

        db.CapitalProjects.Add(new CapitalProject { Id = Alpha, Name = "ART Alpha" });
        db.CapitalProjects.Add(new CapitalProject { Id = Beta, Name = "ART Beta" });

        SeedTrainChain(db, capitalProjectId: Alpha, keySeed: 10, featureCount: AlphaFeatureCount);
        SeedTrainChain(db, capitalProjectId: Beta, keySeed: 20, featureCount: BetaFeatureCount);
    });

    private static void SeedTrainChain(EstimationDbContext db, int capitalProjectId, int keySeed, int featureCount)
    {
        db.StrategicObjectives.Add(new StrategicObjective { Id = keySeed, Summary = $"SO {keySeed}" });
        db.PortfolioEpics.Add(new PortfolioEpic { Id = keySeed, Summary = $"Epic {keySeed}" });
        db.BusinessOutcomes.Add(new BusinessOutcome { Id = keySeed, Summary = $"BO {keySeed}", PortfolioEpicId = keySeed });

        db.CapitalProjectStrategicObjectives.Add(new CapitalProjectStrategicObjective
        {
            CapitalProjectId = capitalProjectId,
            StrategicObjectiveId = keySeed
        });
        db.StrategicObjectivePortfolioEpics.Add(new StrategicObjectivePortfolioEpic
        {
            StrategicObjectiveId = keySeed,
            PortfolioEpicId = keySeed
        });

        for (var i = 0; i < featureCount; i++)
        {
            db.Features.Add(new Feature
            {
                Id = keySeed + i,
                Summary = $"Feature {keySeed + i}",
                BusinessOutcomeId = keySeed,
                PiId = 1
            });
        }
    }

    private static double FeaturesInPi(DashboardData data) =>
        data.FeaturesByPi.Single(c => c.Label == "PI-1").Value;

    private static int TimelineFeatureCount(DashboardData data) =>
        data.PiTimeline.Single(p => p.Name == "PI-1").FeatureCount;

    [Fact]
    public async Task An_unrestricted_caller_with_no_selection_sees_the_whole_portfolio()
    {
        await SeedAsync();

        var data = await NewService().GetDashboardDataAsync(capitalProjectId: null, allowedCapitalProjectIds: null);

        Assert.Equal(AlphaFeatureCount + BetaFeatureCount, FeaturesInPi(data));
        Assert.Equal(AlphaFeatureCount + BetaFeatureCount, TimelineFeatureCount(data));
    }

    [Fact]
    public async Task A_restricted_caller_with_no_selection_sees_only_their_own_ARTs()
    {
        await SeedAsync();

        var data = await NewService().GetDashboardDataAsync(capitalProjectId: null, allowedCapitalProjectIds: new[] { Alpha });

        Assert.Equal(AlphaFeatureCount, FeaturesInPi(data));
        Assert.Equal(AlphaFeatureCount, TimelineFeatureCount(data));
    }

    [Fact]
    public async Task A_caller_allowed_several_ARTs_sees_their_union()
    {
        await SeedAsync();

        var data = await NewService().GetDashboardDataAsync(null, new[] { Alpha, Beta });

        Assert.Equal(AlphaFeatureCount + BetaFeatureCount, FeaturesInPi(data));
    }

    [Fact]
    public async Task Selecting_an_allowed_ART_narrows_to_that_ART()
    {
        await SeedAsync();

        var data = await NewService().GetDashboardDataAsync(Beta, new[] { Alpha, Beta });

        Assert.Equal(BetaFeatureCount, FeaturesInPi(data));
    }

    [Fact]
    public async Task Selecting_an_ART_outside_the_grant_is_refused()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => NewService().GetDashboardDataAsync(Beta, new[] { Alpha }));
    }

    [Fact]
    public async Task A_caller_with_no_grants_sees_nothing()
    {
        await SeedAsync();

        var data = await NewService().GetDashboardDataAsync(null, Array.Empty<int>());

        Assert.Empty(data.FeaturesByPi);
        Assert.Equal(0, TimelineFeatureCount(data));
    }

    [Fact]
    public async Task The_shared_cache_never_serves_one_scope_to_another()
    {
        await SeedAsync();
        var service = NewService();

        var alpha = await service.GetDashboardDataAsync(null, new[] { Alpha });
        var beta = await service.GetDashboardDataAsync(null, new[] { Beta });
        var everything = await service.GetDashboardDataAsync(null, null);
        var alphaAgain = await service.GetDashboardDataAsync(null, new[] { Alpha });

        Assert.Equal(AlphaFeatureCount, FeaturesInPi(alpha));
        Assert.Equal(BetaFeatureCount, FeaturesInPi(beta));
        Assert.Equal(AlphaFeatureCount + BetaFeatureCount, FeaturesInPi(everything));
        Assert.Equal(AlphaFeatureCount, FeaturesInPi(alphaAgain));
    }

    [Fact]
    public async Task The_scope_key_ignores_the_order_the_ARTs_arrive_in()
    {
        await SeedAsync();
        var service = NewService();

        var ascending = await service.GetDashboardDataAsync(null, new[] { Alpha, Beta });
        var descending = await service.GetDashboardDataAsync(null, new[] { Beta, Alpha });

        Assert.Equal(FeaturesInPi(ascending), FeaturesInPi(descending));
    }

    [Fact]
    public async Task The_selector_only_offers_ARTs_the_caller_may_see()
    {
        await SeedAsync();
        var service = NewService();

        var restricted = await service.GetCapitalProjectOptionsAsync(new[] { Beta });
        var unrestricted = await service.GetCapitalProjectOptionsAsync(null);

        Assert.Equal(new[] { "ART Beta" }, restricted.Select(o => o.Name));
        Assert.Equal(new[] { "ART Alpha", "ART Beta" }, unrestricted.Select(o => o.Name));
    }

    [Fact]
    public async Task A_caller_with_no_grants_is_offered_no_ARTs()
    {
        await SeedAsync();

        Assert.Empty(await NewService().GetCapitalProjectOptionsAsync(Array.Empty<int>()));
    }
}
