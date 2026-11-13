using Estimation.HrReporting.Data;
using Estimation.HrReporting.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Estimation.HrReporting.Tests.Infrastructure;

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 9, 30, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class HrReportingDatabase : IDbContextFactory<HrReportingDbContext>, IDisposable
{
    private static readonly InMemoryDatabaseRoot Root = new();

    private readonly DbContextOptions<HrReportingDbContext> _options = new DbContextOptionsBuilder<HrReportingDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString(), Root)
        .ConfigureWarnings(w => w
            .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
            .Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;

    public TestClock Clock { get; } = new();

    public HeadcountRowsCache Cache { get; } = new();

    public HrReportingDbContext CreateDbContext() => new(_options);

    public HeadcountSnapshotService Service() => new(this, Cache, Clock);

    public void Dispose() => Cache.Dispose();
}
