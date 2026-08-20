using System.Text.Json;
using Estimation.Core.Administration.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Estimation.Core.Administration.Audit;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;

    // Entities to skip auditing (the audit log itself, and any other non-business tables).
    // BackupHistory is machine-generated: every backup would otherwise write an insert and an
    // update entry describing a row the Database Backup page already shows in full.
    private static readonly HashSet<string> ExcludedEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(AuditLog),
        nameof(BackupHistory),
        "JiraToken",
        "FeatureSnapshotItem",
        // Review items are bulk-inserted when a review is created and every decision already
        // records who decided what and when on the row itself.
        "FeatureChangeReviewItem"
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
                    auditEntries.Add(new AuditLog
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
                    });
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

                        auditEntries.Add(new AuditLog
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
                        });
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

    // Property names that, by convention, hold a human-readable label of an entity.
    // Checked in order; the first non-empty value wins.
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
