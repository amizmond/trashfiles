using Estimation.Core.Administration.Audit;
using Estimation.Core.Administration.Models;
using Estimation.Core.JiraIntegration.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Estimation.Core.Tests.Administration;

[Collection(LocalDbCollection.Name)]
public class AuditExclusionTests : IAsyncLifetime
{
    private readonly LocalDbFixture _localDb;

    public AuditExclusionTests(LocalDbFixture localDb)
    {
        _localDb = localDb;
    }

    public async Task InitializeAsync()
    {
        if (LocalDbAvailability.UnavailableReason is null)
        {
            await _localDb.ResetAsync();
            await using var db = CreateAuditedContext();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private EstimationDbContext CreateAuditedContext()
    {
        var interceptor = new AuditSaveChangesInterceptor(new ServiceCollection().BuildServiceProvider());

        var options = new DbContextOptionsBuilder<EstimationDbContext>()
            .UseSqlServer(_localDb.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        return new EstimationDbContext(options);
    }

    [LocalDbFact]
    public async Task Backup_history_rows_do_not_produce_audit_entries()
    {
        await using (var db = CreateAuditedContext())
        {
            var history = new BackupHistory
            {
                StartedAt = DateTime.UtcNow,
                Status = BackupStatuses.Running,
                TriggeredBy = "Scheduler"
            };
            db.BackupHistory.Add(history);
            await db.SaveChangesAsync();

            history.Status = BackupStatuses.Success;
            history.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using var check = CreateAuditedContext();
        var audited = await check.AuditLogs
            .Where(a => a.EntityName == nameof(BackupHistory))
            .CountAsync();

        Assert.Equal(0, audited);
    }

    [LocalDbFact]
    public async Task Linked_issue_rows_do_not_produce_audit_entries()
    {
        await using (var db = CreateAuditedContext())
        {
            var link = new IssueLink { JiraLinkId = "501", TypeName = "Blocks", FromKey = "PAY-1", ToKey = "PAY-2" };
            db.IssueLinks.Add(link);
            await db.SaveChangesAsync();

            db.IssueLinks.Remove(link);
            await db.SaveChangesAsync();
        }

        await using var check = CreateAuditedContext();
        var audited = await check.AuditLogs
            .Where(a => a.EntityName == nameof(IssueLink))
            .CountAsync();

        Assert.Equal(0, audited);
    }

    [LocalDbFact]
    public async Task Backup_settings_changes_are_still_audited()
    {
        await using (var db = CreateAuditedContext())
        {
            db.BackupSettings.Add(new BackupSettings
            {
                Enabled = true,
                BackupFolderPath = @"C:\Backups\Estimation",
                RetentionCount = 7
            });
            await db.SaveChangesAsync();
        }

        await using var check = CreateAuditedContext();
        var audited = await check.AuditLogs
            .Where(a => a.EntityName == nameof(BackupSettings))
            .CountAsync();

        Assert.True(audited > 0, "changing the backup schedule should leave an audit trail");
    }
}
