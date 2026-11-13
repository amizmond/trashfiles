using Estimation.HrReporting.Data;
using Estimation.HrReporting.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Estimation.HrReporting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHrReportingModule(
        this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContextFactory<HrReportingDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql
                .MigrationsAssembly(typeof(HrReportingDbContext).Assembly.FullName)
                .MigrationsHistoryTable(HrReportingDbContext.MigrationsHistoryTable)));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HeadcountRowsCache>();
        services.AddScoped<IHeadcountSnapshotService, HeadcountSnapshotService>();
        services.AddScoped<ITeamsAnalysisExclusionService, TeamsAnalysisExclusionService>();
        services.AddScoped<HeadcountUploadPreviewState>();
        services.AddScoped<HeadcountListState>();

        return services;
    }

    public static Task UseHrReportingModuleAsync(
        this IServiceProvider services, CancellationToken ct = default) =>
        HrReportingDbInitializer.InitializeAsync(services, ct);
}
