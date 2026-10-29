using System.Runtime.CompilerServices;
using System.Text.Json;
using Estimation.Core.Administration.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Estimation.Core.Administration.Audit;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;

    private readonly ConditionalWeakTable<DbContext, List<GeneratedValueRefresh>> _pendingRefreshes = new();

    private sealed record GeneratedValueRefresh(EntityEntry Entry, AuditLog Audit, PropertyEntry? Property);

    private static readonly HashSet<string> ExcludedEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(AuditLog),
        nameof(BackupHistory),
        "JiraToken",
        "JiraLabelCache",
        "IssueLink",
        "FeatureSnapshotItem"
    };

    public AuditSaveChangesInterceptor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            OnSavingChanges(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            OnSavingChanges(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is { } context
            && TakeRefreshes(context) is { } refreshes
            && ApplyGeneratedValues(context, refreshes))
        {
            try
            {
                context.SaveChanges();
            }
            catch (Exception ex)
            {
                DropUnsavedRefreshes(context, refreshes, ex);
            }
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context
            && TakeRefreshes(context) is { } refreshes
            && ApplyGeneratedValues(context, refreshes))
        {
            try
            {
                await context.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                DropUnsavedRefreshes(context, refreshes, ex);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ForgetRefreshes(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ForgetRefreshes(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        ForgetRefreshes(eventData.Context);
        base.SaveChangesCanceled(eventData);
    }

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ForgetRefreshes(eventData.Context);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private List<GeneratedValueRefresh>? TakeRefreshes(DbContext context)
    {
        if (!_pendingRefreshes.TryGetValue(context, out var refreshes))
        {
            return null;
        }

        _pendingRefreshes.Remove(context);
        return refreshes;
    }

    private void ForgetRefreshes(DbContext? context)
    {
        if (context is not null)
        {
            _pendingRefreshes.Remove(context);
        }
    }

    private static bool ApplyGeneratedValues(DbContext context, List<GeneratedValueRefresh> refreshes)
    {
        if (context.ChangeTracker.Entries().Any(e => e.State != EntityState.Unchanged))
        {
            return false;
        }

        foreach (var refresh in refreshes)
        {
            var audit = context.Entry(refresh.Audit);
            if (refresh.Property is null)
            {
                audit.Property(a => a.EntityId).CurrentValue = GetPrimaryKeyValue(refresh.Entry);
                audit.Property(a => a.NewValue).CurrentValue = SerializeEntity(refresh.Entry);
            }
            else
            {
                audit.Property(a => a.NewValue).CurrentValue = refresh.Property.CurrentValue?.ToString();
            }
        }

        return true;
    }

    private static void DropUnsavedRefreshes(DbContext context, List<GeneratedValueRefresh> refreshes, Exception ex)
    {
        foreach (var refresh in refreshes)
        {
            context.Entry(refresh.Audit).State = EntityState.Detached;
        }

        Log.Warning(ex, "Audit entries of {Count} saved records keep the temporary values EF used before the save", refreshes.Count);
    }

    private static bool HasGeneratedValues(EntityEntry entry) =>
        entry.Properties.Any(p => p.IsTemporary || p.Metadata.ValueGenerated != ValueGenerated.Never);

    private void OnSavingChanges(DbContext context)
    {
        context.ChangeTracker.DetectChanges();

        string userName;
        using (var scope = _serviceProvider.CreateScope())
        {
            var userProvider = scope.ServiceProvider.GetService<IAuditUserProvider>();
            userName = userProvider?.GetCurrentUserName() ?? "System";
        }
        var now = DateTime.UtcNow;
        var batchId = Guid.NewGuid();
        var auditEntries = new List<AuditLog>();
        var refreshes = new List<GeneratedValueRefresh>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
            {
                continue;
            }

            var entityName = entry.Metadata.ClrType.Name;
            if (ExcludedEntities.Contains(entityName))
            {
                continue;
            }

            var entityId = GetPrimaryKeyValue(entry);
            var displayName = GetDisplayName(entry);

            switch (entry.State)
            {
                case EntityState.Added:
                    var created = new AuditLog
                    {
                        EntityName = entityName,
                        EntityId = entityId,
                        EntityDisplayName = displayName,
                        Action = "Create",
                        PropertyName = null,
                        OldValue = null,
                        NewValue = SerializeEntity(entry),
                        Timestamp = now,
                        UserName = userName,
                        BatchId = batchId
                    };
                    auditEntries.Add(created);
                    if (HasGeneratedValues(entry))
                    {
                        refreshes.Add(new GeneratedValueRefresh(entry, created, null));
                    }
                    break;

                case EntityState.Modified:
                    foreach (var prop in entry.Properties)
                    {
                        if (!prop.IsModified)
                        {
                            continue;
                        }

                        var oldVal = prop.OriginalValue?.ToString();
                        var newVal = prop.CurrentValue?.ToString();

                        if (string.Equals(oldVal, newVal, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var updated = new AuditLog
                        {
                            EntityName = entityName,
                            EntityId = entityId,
                            EntityDisplayName = displayName,
                            Action = "Update",
                            PropertyName = prop.Metadata.Name,
                            OldValue = oldVal,
                            NewValue = newVal,
                            Timestamp = now,
                            UserName = userName,
                            BatchId = batchId
                        };
                        auditEntries.Add(updated);
                        if (prop.IsTemporary)
                        {
                            refreshes.Add(new GeneratedValueRefresh(entry, updated, prop));
                        }
                    }
                    break;

                case EntityState.Deleted:
                    auditEntries.Add(new AuditLog
                    {
                        EntityName = entityName,
                        EntityId = entityId,
                        EntityDisplayName = displayName,
                        Action = "Delete",
                        PropertyName = null,
                        OldValue = SerializeEntity(entry),
                        NewValue = null,
                        Timestamp = now,
                        UserName = userName,
                        BatchId = batchId
                    });
                    break;
            }
        }

        if (auditEntries.Count > 0)
        {
            context.Set<AuditLog>().AddRange(auditEntries);
        }

        if (refreshes.Count > 0)
        {
            _pendingRefreshes.AddOrUpdate(context, refreshes);
        }
        else
        {
            _pendingRefreshes.Remove(context);
        }
    }

    private static string GetPrimaryKeyValue(EntityEntry entry)
    {
        var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProperties is null || keyProperties.Count == 0)
        {
            return "unknown";
        }

        var values = keyProperties.Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "");
        return string.Join(",", values);
    }

    private static readonly string[] DisplayNameProperties =
    {
        "Name", "DisplayName", "FullName", "Title", "Summary"
    };

    private const int DisplayNameMaxLength = 300;

    private static string? GetDisplayName(EntityEntry entry)
    {
        foreach (var propName in DisplayNameProperties)
        {
            var prop = entry.Metadata.FindProperty(propName);
            if (prop is null || prop.ClrType != typeof(string))
            {
                continue;
            }

            var value = entry.Property(propName).CurrentValue as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            return value.Length > DisplayNameMaxLength
                ? value[..DisplayNameMaxLength]
                : value;
        }

        return null;
    }

    private static string SerializeEntity(EntityEntry entry)
    {
        var properties = entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty())
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
        return JsonSerializer.Serialize(properties, SnapshotJsonOptions);
    }

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
