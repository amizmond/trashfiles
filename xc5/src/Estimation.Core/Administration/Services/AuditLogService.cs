using Estimation.Core.Administration.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Administration.Services;

public class AuditLogFilter
{
    public DateTime? FromUtc { get; set; }

    public DateTime? ToUtc { get; set; }

    public IReadOnlyCollection<string> EntityNames { get; set; } = [];

    public string? EntityId { get; set; }

    public IReadOnlyCollection<string> UserNames { get; set; } = [];

    public IReadOnlyCollection<string> Actions { get; set; } = [];

    public string? Text { get; set; }

    public bool SearchValues { get; set; }
}

public class AuditLogListItem
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? EntityDisplayName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? PropertyName { get; set; }
    public string? OldValuePreview { get; set; }
    public string? NewValuePreview { get; set; }
    public bool OldValueIsTruncated { get; set; }
    public bool NewValueIsTruncated { get; set; }
    public Guid? BatchId { get; set; }
    public int BatchSize { get; set; } = 1;
}

public class AuditLogPageResult
{
    public List<AuditLogListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
}

public interface IAuditLogService
{
    Task<AuditLogPageResult> QueryAsync(
        AuditLogFilter filter,
        int page,
        int pageSize,
        string? sortBy = null,
        bool sortDescending = true,
        CancellationToken cancellationToken = default);

    Task<AuditLog?> GetByIdAsync(long id);
    Task<List<AuditLog>> GetBatchAsync(Guid batchId);
    Task<List<string>> GetEntityNamesAsync();
    Task<List<string>> GetUserNamesAsync();
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
}

public class AuditLogService : IAuditLogService
{
    private const int PreviewLength = 200;

    private const int ValueSearchMaxWindowDays = 30;

    private const int RetentionDeleteBatchSize = 5000;

    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public AuditLogService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<AuditLogPageResult> QueryAsync(
        AuditLogFilter filter,
        int page,
        int pageSize,
        string? sortBy = null,
        bool sortDescending = true,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);

        var query = ApplyFilter(db.AuditLogs.AsNoTracking(), filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await ApplySort(query, sortBy, sortDescending)
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogListItem
            {
                Id = a.Id,
                Timestamp = a.Timestamp,
                UserName = a.UserName,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                EntityDisplayName = a.EntityDisplayName,
                Action = a.Action,
                PropertyName = a.PropertyName,
                OldValuePreview = a.OldValue == null
                    ? null
                    : (a.OldValue.Length <= PreviewLength ? a.OldValue : a.OldValue.Substring(0, PreviewLength)),
                NewValuePreview = a.NewValue == null
                    ? null
                    : (a.NewValue.Length <= PreviewLength ? a.NewValue : a.NewValue.Substring(0, PreviewLength)),
                OldValueIsTruncated = a.OldValue != null && a.OldValue.Length > PreviewLength,
                NewValueIsTruncated = a.NewValue != null && a.NewValue.Length > PreviewLength,
                BatchId = a.BatchId
            })
            .ToListAsync(cancellationToken);

        await FillBatchSizesAsync(db, items, cancellationToken);

        return new AuditLogPageResult { Items = items, TotalCount = totalCount };
    }

    private static async Task FillBatchSizesAsync(
        EstimationDbContext db, List<AuditLogListItem> items, CancellationToken cancellationToken)
    {
        var batchIds = items
            .Where(i => i.BatchId is not null)
            .Select(i => i.BatchId!.Value)
            .Distinct()
            .ToList();

        if (batchIds.Count == 0)
        {
            return;
        }

        var sizes = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.BatchId != null && batchIds.Contains(a.BatchId.Value))
            .GroupBy(a => a.BatchId!.Value)
            .Select(g => new { BatchId = g.Key, Size = g.Count() })
            .ToDictionaryAsync(g => g.BatchId, g => g.Size, cancellationToken);

        foreach (var item in items)
        {
            if (item.BatchId is Guid batchId && sizes.TryGetValue(batchId, out var size))
            {
                item.BatchSize = size;
            }
        }
    }

    public async Task<AuditLog?> GetByIdAsync(long id)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<AuditLog>> GetBatchAsync(Guid batchId)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.BatchId == batchId)
            .OrderBy(a => a.Id)
            .ToListAsync();
    }

    public async Task<List<string>> GetEntityNamesAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AuditLogs
            .AsNoTracking()
            .Select(a => a.EntityName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
    }

    public async Task<List<string>> GetUserNamesAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.AuditLogs
            .AsNoTracking()
            .Select(a => a.UserName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await _ctx.CreateDbContextAsync(cancellationToken);

        var totalDeleted = 0;
        while (true)
        {
            var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE TOP ({RetentionDeleteBatchSize}) FROM AuditLogs WHERE [Timestamp] < {cutoffUtc}",
                cancellationToken);
            totalDeleted += deleted;

            if (deleted < RetentionDeleteBatchSize)
            {
                return totalDeleted;
            }
        }
    }

    private static IQueryable<AuditLog> ApplyFilter(IQueryable<AuditLog> query, AuditLogFilter filter)
    {
        var from = filter.FromUtc;
        if (filter.SearchValues && !string.IsNullOrWhiteSpace(filter.Text) && from is null)
        {
            from = DateTime.UtcNow.AddDays(-ValueSearchMaxWindowDays);
        }

        if (from is not null)
        {
            query = query.Where(a => a.Timestamp >= from);
        }

        if (filter.ToUtc is not null)
        {
            query = query.Where(a => a.Timestamp < filter.ToUtc);
        }

        if (filter.EntityNames.Count > 0)
        {
            var entityNames = filter.EntityNames.ToList();
            query = query.Where(a => entityNames.Contains(a.EntityName));
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(a => a.EntityId == filter.EntityId);
        }

        if (filter.UserNames.Count > 0)
        {
            var userNames = filter.UserNames.ToList();
            query = query.Where(a => userNames.Contains(a.UserName));
        }

        if (filter.Actions.Count > 0)
        {
            var actions = filter.Actions.ToList();
            query = query.Where(a => actions.Contains(a.Action));
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var text = filter.Text.Trim();
            if (filter.SearchValues)
            {
                query = query.Where(a =>
                    a.EntityId == text ||
                    (a.EntityDisplayName != null && a.EntityDisplayName.Contains(text)) ||
                    (a.PropertyName != null && a.PropertyName.Contains(text)) ||
                    (a.OldValue != null && a.OldValue.Contains(text)) ||
                    (a.NewValue != null && a.NewValue.Contains(text)));
            }
            else
            {
                query = query.Where(a =>
                    a.EntityId == text ||
                    (a.EntityDisplayName != null && a.EntityDisplayName.Contains(text)) ||
                    (a.PropertyName != null && a.PropertyName.Contains(text)));
            }
        }

        return query;
    }

    private static IOrderedQueryable<AuditLog> ApplySort(
        IQueryable<AuditLog> query, string? sortBy, bool descending)
    {
        return sortBy switch
        {
            nameof(AuditLog.UserName) => descending
                ? query.OrderByDescending(a => a.UserName).ThenByDescending(a => a.Id)
                : query.OrderBy(a => a.UserName).ThenBy(a => a.Id),
            nameof(AuditLog.EntityName) => descending
                ? query.OrderByDescending(a => a.EntityName).ThenByDescending(a => a.Id)
                : query.OrderBy(a => a.EntityName).ThenBy(a => a.Id),
            nameof(AuditLog.EntityId) => descending
                ? query.OrderByDescending(a => a.EntityId).ThenByDescending(a => a.Id)
                : query.OrderBy(a => a.EntityId).ThenBy(a => a.Id),
            nameof(AuditLog.EntityDisplayName) => descending
                ? query.OrderByDescending(a => a.EntityDisplayName).ThenByDescending(a => a.Id)
                : query.OrderBy(a => a.EntityDisplayName).ThenBy(a => a.Id),
            nameof(AuditLog.Action) => descending
                ? query.OrderByDescending(a => a.Action).ThenByDescending(a => a.Id)
                : query.OrderBy(a => a.Action).ThenBy(a => a.Id),
            nameof(AuditLog.PropertyName) => descending
                ? query.OrderByDescending(a => a.PropertyName).ThenByDescending(a => a.Id)
                : query.OrderBy(a => a.PropertyName).ThenBy(a => a.Id),
            _ => descending
                ? query.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
                : query.OrderBy(a => a.Timestamp).ThenBy(a => a.Id)
        };
    }
}
