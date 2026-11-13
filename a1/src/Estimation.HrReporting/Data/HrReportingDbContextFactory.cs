using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Estimation.HrReporting.Data;

public sealed class HrReportingDbContextFactory : IDesignTimeDbContextFactory<HrReportingDbContext>
{
    private const string Fallback =
        @"Server=(localdb)\mssqllocaldb;Database=db_estimation;Trusted_Connection=True;";

    public HrReportingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("HRREPORTING_DESIGNTIME_CONNECTION") ?? Fallback;

        var options = new DbContextOptionsBuilder<HrReportingDbContext>()
            .UseSqlServer(connectionString, sql => sql
                .MigrationsAssembly(typeof(HrReportingDbContext).Assembly.FullName)
                .MigrationsHistoryTable(HrReportingDbContext.MigrationsHistoryTable))
            .Options;

        return new HrReportingDbContext(options);
    }
}
