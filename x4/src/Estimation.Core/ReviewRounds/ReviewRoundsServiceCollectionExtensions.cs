using Estimation.Core.Features.Services;
using Estimation.Core.ReviewRounds.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Estimation.Core.ReviewRounds;

public static class ReviewRoundsServiceCollectionExtensions
{
    /// <summary>Registers the review-round services. Remove this call to switch the feature off.</summary>
    public static IServiceCollection AddReviewRounds(this IServiceCollection services)
    {
        services.AddScoped<IFeatureChangeReviewService, FeatureChangeReviewService>();
        services.AddScoped<IFeatureSnapshotDeletionGuard, ReviewRoundSnapshotDeletionGuard>();
        return services;
    }
}
