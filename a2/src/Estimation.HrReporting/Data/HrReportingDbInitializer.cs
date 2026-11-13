using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Estimation.HrReporting.Data;

public static class HrReportingDbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(HrReportingDbInitializer).FullName!);

        try
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HrReportingDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);

            await db.Database.MigrateAsync(ct);
            foreach (var page in HrReportingRoutes.NavPages)
            {
                await EnsureNavigationPageAsync(db, page, logger, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "HR Reporting module failed to initialise. Its pages will not be available.");
        }
    }

    private static async Task EnsureNavigationPageAsync(
        HrReportingDbContext db, HrReportingRoutes.NavPage page, ILogger logger, CancellationToken ct)
    {
        var existing = await db.AppPages.FirstOrDefaultAsync(p => p.Id == page.Id, ct);

        if (existing is not null)
        {
            var changed =
                existing.Key != page.Key
                || existing.DisplayName != page.DisplayName
                || existing.Group != HrReportingRoutes.NavGroup
                || existing.SortOrder != page.SortOrder
                || existing.ScopeMode != HrReportingRoutes.ScopeMode
                || existing.IsAdminOnly;

            if (!changed)
            {
                return;
            }

            var previousKey = existing.Key;
            existing.Key = page.Key;
            existing.DisplayName = page.DisplayName;
            existing.Group = HrReportingRoutes.NavGroup;
            existing.SortOrder = page.SortOrder;
            existing.ScopeMode = HrReportingRoutes.ScopeMode;
            existing.IsAdminOnly = false;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Updated AppPages row {Id} from '{PreviousKey}' to '{Key}' for the HR Reporting module.",
                page.Id, previousKey, page.Key);
            return;
        }

        var sql = $"""
            SET IDENTITY_INSERT [AppPages] ON;
            INSERT INTO [AppPages] ([Id], [Key], [DisplayName], [Group], [IsAdminOnly], [SortOrder], [ScopeMode])
            VALUES ({page.Id}, @key, @displayName, @group, 0, {page.SortOrder}, {HrReportingRoutes.ScopeMode});
            SET IDENTITY_INSERT [AppPages] OFF;
            """;

        await db.Database.ExecuteSqlRawAsync(
            sql,
            new[]
            {
                new SqlParameter("@key", page.Key),
                new SqlParameter("@displayName", page.DisplayName),
                new SqlParameter("@group", HrReportingRoutes.NavGroup),
            },
            ct);

        logger.LogInformation(
            "Registered AppPages row {Id} '{Key}' for the HR Reporting module.", page.Id, page.Key);
    }
}
