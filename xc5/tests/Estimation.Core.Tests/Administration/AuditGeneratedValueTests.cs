using System.Text.Json;
using Estimation.Core.Administration.Audit;
using Estimation.Core.Administration.Models;
using Estimation.Core.Resources.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Estimation.Core.Tests.Administration;

[Collection(LocalDbCollection.Name)]
public class AuditGeneratedValueTests : IAsyncLifetime
{
    private const int DashboardPageId = 1;

    private const int AuditLogPageId = 14;

    private static readonly AuditSaveChangesInterceptor Interceptor =
        new(new ServiceCollection().BuildServiceProvider());

    private readonly LocalDbFixture _localDb;

    public AuditGeneratedValueTests(LocalDbFixture localDb)
    {
        _localDb = localDb;
    }

    public async Task InitializeAsync()
    {
        if (LocalDbAvailability.UnavailableReason is null)
        {
            await ClearAuditAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private EstimationDbContext CreateAuditedContext()
    {
        var options = new DbContextOptionsBuilder<EstimationDbContext>()
            .UseSqlServer(_localDb.ConnectionString)
            .AddInterceptors(Interceptor)
            .Options;

        return new EstimationDbContext(options);
    }

    private async Task ClearAuditAsync()
    {
        await using var db = CreateAuditedContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs");
    }

    private async Task<List<AuditLog>> ReadAuditAsync()
    {
        await using var db = CreateAuditedContext();
        return await db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
    }

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static JsonElement Snapshot(AuditLog audit, string property)
    {
        Assert.NotNull(audit.NewValue);
        return JsonDocument.Parse(audit.NewValue!).RootElement.GetProperty(property);
    }

    [LocalDbFact]
    public async Task A_new_record_is_logged_with_the_id_the_database_gave_it()
    {
        var department = new Department { Name = UniqueName("Audit") };

        await using (var db = CreateAuditedContext())
        {
            db.Departments.Add(department);
            await db.SaveChangesAsync();
        }

        var audit = Assert.Single(await ReadAuditAsync());

        Assert.Equal("Create", audit.Action);
        Assert.True(department.Id > 0);
        Assert.Equal(department.Id.ToString(), audit.EntityId);
        Assert.Equal(department.Id, Snapshot(audit, nameof(Department.Id)).GetInt32());
    }

    [LocalDbFact]
    public async Task A_synchronous_save_logs_the_database_id_too()
    {
        var department = new Department { Name = UniqueName("Audit") };

        await using (var db = CreateAuditedContext())
        {
            db.Departments.Add(department);
            db.SaveChanges();
        }

        var audit = Assert.Single(await ReadAuditAsync());

        Assert.Equal(department.Id.ToString(), audit.EntityId);
        Assert.Equal(department.Id, Snapshot(audit, nameof(Department.Id)).GetInt32());
    }

    [LocalDbFact]
    public async Task Records_created_together_log_each_others_database_ids()
    {
        var profile = new Profile { Name = UniqueName("Audit"), CreatedAt = DateTime.UtcNow };
        profile.PagePermissions.Add(new ProfilePagePermission { AppPageId = DashboardPageId, AccessLevel = AccessLevel.Edit });
        profile.PagePermissions.Add(new ProfilePagePermission { AppPageId = AuditLogPageId, AccessLevel = AccessLevel.ReadOnly });

        await using (var db = CreateAuditedContext())
        {
            db.Profiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var audit = await ReadAuditAsync();

        var created = Assert.Single(audit, a => a.EntityName == nameof(Profile));
        Assert.Equal(profile.Id.ToString(), created.EntityId);

        var permissions = audit.Where(a => a.EntityName == nameof(ProfilePagePermission)).ToList();
        Assert.Equal(
            profile.PagePermissions.Select(p => p.Id.ToString()).Order(),
            permissions.Select(a => a.EntityId).Order());
        Assert.All(permissions, a =>
            Assert.Equal(profile.Id, Snapshot(a, nameof(ProfilePagePermission.ProfileId)).GetInt32()));
        Assert.Single(audit.Select(a => a.BatchId).Distinct());
    }

    [LocalDbFact]
    public async Task Pointing_a_saved_record_at_a_new_one_logs_the_new_database_id()
    {
        var first = new Profile { Name = UniqueName("First"), CreatedAt = DateTime.UtcNow };
        var permission = new ProfilePagePermission { AppPageId = AuditLogPageId, AccessLevel = AccessLevel.ReadOnly };
        first.PagePermissions.Add(permission);

        await using (var db = CreateAuditedContext())
        {
            db.Profiles.Add(first);
            await db.SaveChangesAsync();
        }

        await ClearAuditAsync();

        var second = new Profile { Name = UniqueName("Second"), CreatedAt = DateTime.UtcNow };
        await using (var db = CreateAuditedContext())
        {
            var saved = await db.ProfilePagePermissions.SingleAsync(p => p.Id == permission.Id);
            saved.Profile = second;
            await db.SaveChangesAsync();
        }

        var audit = await ReadAuditAsync();

        var moved = Assert.Single(audit, a => a.Action == "Update");
        Assert.Equal(nameof(ProfilePagePermission.ProfileId), moved.PropertyName);
        Assert.Equal(permission.Id.ToString(), moved.EntityId);
        Assert.Equal(first.Id.ToString(), moved.OldValue);
        Assert.Equal(second.Id.ToString(), moved.NewValue);
        Assert.Equal(second.Id.ToString(), Assert.Single(audit, a => a.Action == "Create").EntityId);
    }

    [LocalDbFact]
    public async Task Filling_in_database_ids_adds_no_extra_audit_rows()
    {
        var departments = Enumerable.Range(1, 3).Select(_ => new Department { Name = UniqueName("Audit") }).ToList();

        await using (var db = CreateAuditedContext())
        {
            db.Departments.AddRange(departments);
            await db.SaveChangesAsync();
        }

        var audit = await ReadAuditAsync();

        Assert.Equal(3, audit.Count);
        Assert.Equal(
            departments.Select(d => d.Id.ToString()).Order(),
            audit.Select(a => a.EntityId).Order());
    }

    [LocalDbFact]
    public async Task A_later_save_on_the_same_context_is_audited_normally()
    {
        var department = new Department { Name = UniqueName("Audit") };

        await using (var db = CreateAuditedContext())
        {
            db.Departments.Add(department);
            await db.SaveChangesAsync();

            department.Description = "renamed later";
            await db.SaveChangesAsync();
        }

        var audit = await ReadAuditAsync();

        Assert.Equal(2, audit.Count);
        Assert.Equal(department.Id.ToString(), audit[0].EntityId);
        var update = audit[1];
        Assert.Equal("Update", update.Action);
        Assert.Equal(nameof(Department.Description), update.PropertyName);
        Assert.Equal(department.Id.ToString(), update.EntityId);
        Assert.NotEqual(audit[0].BatchId, update.BatchId);
    }

    [LocalDbFact]
    public async Task Saving_without_accepting_changes_writes_each_record_and_entry_once()
    {
        var department = new Department { Name = UniqueName("Audit") };

        await using (var db = CreateAuditedContext())
        {
            db.Departments.Add(department);
            await db.SaveChangesAsync(acceptAllChangesOnSuccess: false);
            db.ChangeTracker.AcceptAllChanges();
        }

        Assert.Single(await ReadAuditAsync());

        await using var check = CreateAuditedContext();
        Assert.Equal(1, await check.Departments.CountAsync(d => d.Name == department.Name));
    }
}
