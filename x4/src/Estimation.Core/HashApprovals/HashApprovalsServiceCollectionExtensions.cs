using Estimation.Core.HashApprovals.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Estimation.Core.HashApprovals;

public static class HashApprovalsServiceCollectionExtensions
{
    /// <summary>Registers the hash-based approval service. Remove this call to switch the feature off.</summary>
    public static IServiceCollection AddHashApprovals(this IServiceCollection services)
    {
        services.AddScoped<IFeatureStateApprovalService, FeatureStateApprovalService>();
        return services;
    }
}
