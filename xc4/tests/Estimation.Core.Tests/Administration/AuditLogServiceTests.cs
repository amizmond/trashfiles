using Estimation.Core.Administration.Models;
using Estimation.Core.Administration.Services;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.Administration;

public class AuditLogServiceTests
{
    private readonly InMemoryDatabase _db = new();

    private static AuditLog Change(Guid? batchId, DateTime timestamp, string property, string entityName = "Feature") => new()
    {
        EntityName = entityName,
        EntityId = "1",
        Action = "Update",
        PropertyName = property,
        BatchId = batchId,
        Timestamp = timestamp,
        UserName = "tester"
    };

    [Fact]
    public async Task Every_row_carries_the_size_of_its_whole_operation_when_a_page_shows_only_part_of_it()
    {
        var newest = Guid.NewGuid();
        var split = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await _db.SeedAsync(db =>
        {
            db.AuditLogs.AddRange(Enumerable.Range(1, 2).Select(i => Change(newest, now, $"New{i}")));
            db.AuditLogs.AddRange(Enumerable.Range(1, 7).Select(i => Change(split, now.AddMinutes(-1), $"Split{i}")));
            db.AuditLogs.Add(Change(null, now.AddMinutes(-2), "Unbatched"));
        });
        var service = new AuditLogService(_db);

        var first = await service.QueryAsync(new AuditLogFilter(), page: 0, pageSize: 5);
        var second = await service.QueryAsync(new AuditLogFilter(), page: 1, pageSize: 5);

        Assert.Equal(10, first.TotalCount);
        Assert.Equal(3, first.Items.Count(i => i.BatchId == split));
        Assert.Equal(4, second.Items.Count(i => i.BatchId == split));
        Assert.All(first.Items.Where(i => i.BatchId == newest), i => Assert.Equal(2, i.BatchSize));
        Assert.All(first.Items.Concat(second.Items).Where(i => i.BatchId == split), i => Assert.Equal(7, i.BatchSize));
        Assert.Equal(1, Assert.Single(second.Items, i => i.BatchId is null).BatchSize);
    }

    [Fact]
    public async Task The_operation_size_includes_changes_the_filters_hide()
    {
        var batch = Guid.NewGuid();
        await _db.SeedAsync(db =>
        {
            db.AuditLogs.Add(Change(batch, DateTime.UtcNow, "Name"));
            db.AuditLogs.Add(Change(batch, DateTime.UtcNow, "Name", entityName: "Team"));
        });

        var result = await new AuditLogService(_db).QueryAsync(new AuditLogFilter { EntityName = "Feature" }, 0, 25);

        Assert.Equal(2, Assert.Single(result.Items).BatchSize);
    }
}
