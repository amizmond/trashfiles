using System.Data;
using Estimation.HrReporting.Data;
using Estimation.HrReporting.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Estimation.HrReporting.Services;

public interface IHeadcountSnapshotService
{
    Task<List<HeadcountUpload>> GetUploadsAsync(CancellationToken ct = default);

    Task<HeadcountUpload?> FindPreviousAsync(DateTime dataDate, CancellationToken ct = default);

    Task<IReadOnlyList<HeadcountRow>> GetRowsAsync(int uploadId, CancellationToken ct = default);

    Task<HeadcountUpload> SaveAsync(
        IReadOnlyList<HeadcountRow> rows,
        string fileName,
        DateTime dataDate,
        string? comment,
        string? uploadedBy,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(int uploadId, CancellationToken ct = default);
}

public sealed class HeadcountSnapshotService : IHeadcountSnapshotService
{
    private readonly IDbContextFactory<HrReportingDbContext> _factory;
    private readonly HeadcountRowsCache _cache;
    private readonly TimeProvider _clock;

    public HeadcountSnapshotService(
        IDbContextFactory<HrReportingDbContext> factory, HeadcountRowsCache cache, TimeProvider clock)
    {
        _factory = factory;
        _cache = cache;
        _clock = clock;
    }

    public async Task<List<HeadcountUpload>> GetUploadsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await Newest(db.HeadcountUploads.AsNoTracking()).ToListAsync(ct);
    }

    public async Task<HeadcountUpload?> FindPreviousAsync(DateTime dataDate, CancellationToken ct = default)
    {
        var date = dataDate.Date;
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await Newest(db.HeadcountUploads.AsNoTracking().Where(u => u.DataDate <= date))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<HeadcountRow>> GetRowsAsync(int uploadId, CancellationToken ct = default)
    {
        if (_cache.TryGet(uploadId, out var cached))
        {
            return cached;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.HeadcountRows
            .AsNoTracking()
            .Where(r => r.UploadId == uploadId)
            .OrderBy(r => r.RowNumber)
            .ToListAsync(ct);

        return _cache.Set(uploadId, rows);
    }

    public async Task<HeadcountUpload> SaveAsync(
        IReadOnlyList<HeadcountRow> rows,
        string fileName,
        DateTime dataDate,
        string? comment,
        string? uploadedBy,
        CancellationToken ct = default)
    {
        var copies = rows.Select(r => r.CopyForInsert()).ToList();

        var upload = new HeadcountUpload
        {
            DataDate = dataDate.Date,
            FileName = Limit(fileName, HeadcountUpload.FileNameMaxLength) ?? string.Empty,
            Comment = Limit(comment, HeadcountUpload.CommentMaxLength),
            UploadedAt = _clock.GetUtcNow().UtcDateTime,
            UploadedBy = Limit(uploadedBy, HeadcountUpload.UploadedByMaxLength),
            RowCount = copies.Count,
            TotalHc = copies.Sum(r => r.Hc ?? 0),
            TotalFte = copies.Sum(r => r.Fte ?? 0)
        };

        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            if (db.Database.IsSqlServer())
            {
                await InsertWithBulkCopyAsync(db, upload, copies, ct);
            }
            else
            {
                upload.Rows = copies;
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.HeadcountUploads.Add(upload);
                await db.SaveChangesAsync(ct);
            }
        }

        _cache.Set(upload.Id, copies.OrderBy(r => r.RowNumber).ToList());

        return new HeadcountUpload
        {
            Id = upload.Id,
            DataDate = upload.DataDate,
            FileName = upload.FileName,
            Comment = upload.Comment,
            UploadedAt = upload.UploadedAt,
            UploadedBy = upload.UploadedBy,
            RowCount = upload.RowCount,
            TotalHc = upload.TotalHc,
            TotalFte = upload.TotalFte
        };
    }

    public async Task<bool> DeleteAsync(int uploadId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var upload = await db.HeadcountUploads.FirstOrDefaultAsync(u => u.Id == uploadId, ct);
        if (upload is null)
        {
            return false;
        }

        db.HeadcountUploads.Remove(upload);
        await db.SaveChangesAsync(ct);
        _cache.Remove(uploadId);
        return true;
    }

    private static async Task InsertWithBulkCopyAsync(
        HrReportingDbContext db, HeadcountUpload upload, List<HeadcountRow> rows, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        db.HeadcountUploads.Add(upload);
        await db.SaveChangesAsync(ct);

        var entity = db.Model.FindEntityType(typeof(HeadcountRow))!;
        string ColumnName(string property) => entity.FindProperty(property)!.GetColumnName();

        var table = new DataTable();
        table.Columns.Add(ColumnName(nameof(HeadcountRow.UploadId)), typeof(int));
        table.Columns.Add(ColumnName(nameof(HeadcountRow.RowNumber)), typeof(int));
        foreach (var column in HeadcountColumns.All)
        {
            var type = Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType;
            table.Columns.Add(ColumnName(column.Key), type);
        }

        foreach (var row in rows)
        {
            var values = new object[table.Columns.Count];
            values[0] = upload.Id;
            values[1] = row.RowNumber;
            for (var i = 0; i < HeadcountColumns.All.Count; i++)
            {
                values[i + 2] = HeadcountColumns.All[i].GetValue(row) ?? DBNull.Value;
            }

            table.Rows.Add(values);
        }

        using var bulkCopy = new SqlBulkCopy(
            (SqlConnection)db.Database.GetDbConnection(),
            SqlBulkCopyOptions.CheckConstraints,
            (SqlTransaction)transaction.GetDbTransaction())
        {
            DestinationTableName = $"[{entity.GetTableName()}]",
            BatchSize = 5000,
            BulkCopyTimeout = 600
        };

        foreach (DataColumn column in table.Columns)
        {
            bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(table, ct);
        await transaction.CommitAsync(ct);

        foreach (var row in rows)
        {
            row.UploadId = upload.Id;
        }
    }

    public static IOrderedQueryable<HeadcountUpload> Newest(IQueryable<HeadcountUpload> uploads) =>
        uploads
            .OrderByDescending(u => u.DataDate)
            .ThenByDescending(u => u.UploadedAt)
            .ThenByDescending(u => u.Id);

    private static string? Limit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
