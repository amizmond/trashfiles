using Estimation.Core.Features.Hygiene.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Estimation.Core.Features.Hygiene;

public static class FeatureHygieneServiceCollectionExtensions
{
    public static IServiceCollection AddFeatureHygiene(this IServiceCollection services)
    {
        services.AddScoped<IFeatureHygieneRuleService, FeatureHygieneRuleService>();
        services.AddScoped<IFeatureHygieneService, FeatureHygieneService>();
        return services;
    }
}
