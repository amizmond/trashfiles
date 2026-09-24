using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.PlanningIncrement.Services;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Resources.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Estimation.MasterData.Services;

public interface IMasterDataCacheService
{
    Task<IEnumerable<Art>> GetCapitalProjectsAsync();
    Task<IEnumerable<TechnologyStack>> GetTechnologyStacksAsync();
    Task<IEnumerable<Feature>> GetFeaturesAsync();
    Task<IEnumerable<UnfundedOption>> GetUnfundedOptionsAsync();
    Task<IEnumerable<StrategicObjective>> GetStrategicObjectivesAsync();
    Task<IEnumerable<PortfolioEpic>> GetPortfolioEpicsAsync();
    Task<IEnumerable<BusinessOutcome>> GetBusinessOutcomesAsync();
    Task<IEnumerable<Pi>> GetPlanningIncrementsAsync();
    Task<IEnumerable<Team>> GetTeamsAsync();
    Task ForceRefreshAsync();
    Task<bool> IsCacheWarmedUpAsync();
}
public class MasterDataCacheService : IMasterDataCacheService
{
    private readonly IMemoryCache _cache;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MasterDataCacheService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public MasterDataCacheService(
        IMemoryCache cache,
        IServiceProvider serviceProvider,
        ILogger<MasterDataCacheService> logger)
    {
        _cache = cache;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IEnumerable<Art>> GetCapitalProjectsAsync()
    {
        if (_cache.TryGetValue(CacheKeys.CapitalProjects, out IEnumerable<Art> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for CapitalProjects, fetching from service");
        await RefreshCapitalProjectsAsync();
        return _cache.Get<IEnumerable<Art>>(CacheKeys.CapitalProjects) ?? Enumerable.Empty<Art>();
    }

    public async Task<IEnumerable<TechnologyStack>> GetTechnologyStacksAsync()
    {
        if (_cache.TryGetValue(CacheKeys.TechnologyStacks, out IEnumerable<TechnologyStack> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for TechnologyStacks, fetching from service");
        await RefreshTechnologyStacksAsync();
        return _cache.Get<IEnumerable<TechnologyStack>>(CacheKeys.TechnologyStacks) ?? Enumerable.Empty<TechnologyStack>();
    }



    public async Task<IEnumerable<UnfundedOption>> GetUnfundedOptionsAsync()
    {
        if (_cache.TryGetValue(CacheKeys.UnfundedOptions, out IEnumerable<UnfundedOption> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for UnfundedOptions, fetching from service");
        await RefreshUnfundedOptionsAsync();
        return _cache.Get<IEnumerable<UnfundedOption>>(CacheKeys.UnfundedOptions) ?? Enumerable.Empty<UnfundedOption>();
    }

    public async Task<IEnumerable<StrategicObjective>> GetStrategicObjectivesAsync()
    {
        if (_cache.TryGetValue(CacheKeys.StrategicObjectives, out IEnumerable<StrategicObjective> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for StrategicObjectives, fetching from service");
        await RefreshStrategicObjectivesAsync();
        return _cache.Get<IEnumerable<StrategicObjective>>(CacheKeys.StrategicObjectives) ?? Enumerable.Empty<StrategicObjective>();
    }

    public async Task<IEnumerable<PortfolioEpic>> GetPortfolioEpicsAsync()
    {
        if (_cache.TryGetValue(CacheKeys.PortfolioEpics, out IEnumerable<PortfolioEpic> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for PortfolioEpics, fetching from service");
        await RefreshPortfolioEpicsAsync();
        return _cache.Get<IEnumerable<PortfolioEpic>>(CacheKeys.PortfolioEpics) ?? Enumerable.Empty<PortfolioEpic>();
    }

    public async Task<IEnumerable<Feature>> GetFeaturesAsync()
    {
        if (_cache.TryGetValue(CacheKeys.Features, out IEnumerable<Feature> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for Features, fetching from service");
        await RefreshFeaturesAsync();
        return _cache.Get<IEnumerable<Feature>>(CacheKeys.Features) ?? Enumerable.Empty<Feature>();
    }

    public async Task<IEnumerable<BusinessOutcome>> GetBusinessOutcomesAsync()
    {
        if (_cache.TryGetValue(CacheKeys.BusinessOutcomes, out IEnumerable<BusinessOutcome> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for BusinessOutcomes, fetching from service");
        await RefreshBusinessOutcomesAsync();
        return _cache.Get<IEnumerable<BusinessOutcome>>(CacheKeys.BusinessOutcomes) ?? Enumerable.Empty<BusinessOutcome>();
    }

    public async Task<IEnumerable<Pi>> GetPlanningIncrementsAsync()
    {
        if (_cache.TryGetValue(CacheKeys.ProgramIncrements, out IEnumerable<Pi> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for ProgramIncrements, fetching from service");
        await RefreshProgramIncrementsAsync();
        return _cache.Get<IEnumerable<Pi>>(CacheKeys.ProgramIncrements) ?? Enumerable.Empty<Pi>();
    }

    public async Task<IEnumerable<Team>> GetTeamsAsync()
    {
        if (_cache.TryGetValue(CacheKeys.Teams, out IEnumerable<Team> cached))
        {
            return cached;
        }

        _logger.LogWarning("Cache miss for Teams, fetching from service");
        await RefreshTeamsAsync();
        return _cache.Get<IEnumerable<Team>>(CacheKeys.Teams) ?? Enumerable.Empty<Team>();
    }

    public async Task<bool> IsCacheWarmedUpAsync()
    {
        return _cache.TryGetValue(CacheKeys.CapitalProjects, out _) &&
               _cache.TryGetValue(CacheKeys.TechnologyStacks, out _);
    }

    public async Task ForceRefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            _logger.LogInformation("Force refresh requested");
            await RefreshAllDataAsync();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // Private refresh methods
    private async Task RefreshCapitalProjectsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IArtService>();
        var data = await service.GetAllAsync();
        _cache.Set(CacheKeys.CapitalProjects, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshTechnologyStacksAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITechnologyStackService>();
        var data = await service.GetAllAsync();
        _cache.Set(CacheKeys.TechnologyStacks, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshUnfundedOptionsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUnfundedOptionService>();
        var data = await service.GetAllAsync();
        _cache.Set(CacheKeys.UnfundedOptions, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshStrategicObjectivesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStrategicObjectiveService>();
        var data = await service.GetAllLightAsync();
        _cache.Set(CacheKeys.StrategicObjectives, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshFeaturesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMasterSheetService>();
        var data = await service.GetAllWithHierarchyAsync();
        _cache.Set(CacheKeys.Features, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshPortfolioEpicsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPortfolioEpicService>();
        var data = await service.GetAllLightAsync();
        _cache.Set(CacheKeys.PortfolioEpics, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshBusinessOutcomesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMasterSheetService>();
        var data = await service.GetBusinessOutcomeHierarchy();
        _cache.Set(CacheKeys.BusinessOutcomes, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshProgramIncrementsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPiService>();
        var data = await service.GetAllLightAsync();
        _cache.Set(CacheKeys.ProgramIncrements, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshTeamsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITeamService>();
        var data = await service.GetAllNamesAsync();
        _cache.Set(CacheKeys.Teams, data, TimeSpan.FromMinutes(10));
    }

    private async Task RefreshAllDataAsync()
    {
        await Task.WhenAll(
            RefreshCapitalProjectsAsync(),
            RefreshTechnologyStacksAsync(),
            RefreshUnfundedOptionsAsync(),
            RefreshStrategicObjectivesAsync(),
            //RefreshPortfolioEpicsAsync(),
            //RefreshBusinessOutcomesAsync(),
            RefreshProgramIncrementsAsync(),
            RefreshTeamsAsync(),
            RefreshFeaturesAsync()
        );
    }
}

public static class CacheKeys
{
    public const string CapitalProjects = "AllCapitalProjects";
    public const string TechnologyStacks = "AllTechnologyStacks";
    public const string Features = "AllFeatures";
    public const string UnfundedOptions = "AllUnfundedOptions";
    public const string StrategicObjectives = "AllStrategicObjectives";
    public const string PortfolioEpics = "AllPortfolioEpics";
    public const string BusinessOutcomes = "AllBusinessOutcomes";
    public const string ProgramIncrements = "AllProgramIncrements";
    public const string Teams = "AllTeams";
}
