using Estimation.HrReporting.Models;
using Estimation.HrReporting.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.HrReporting.Tests;

public sealed class HeadcountSnapshotServiceTests : IDisposable
{
    private readonly HrReportingDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static List<HeadcountRow> Rows(params string[] brids) =>
        brids.Select((b, i) => new HeadcountRow { Brid = b, RowNumber = i + 2, Hc = 1, Fte = 0.5m }).ToList();

    [Fact]
    public async Task Saves_a_snapshot_with_its_totals()
    {
        var service = _db.Service();

        var saved = await service.SaveAsync(Rows("B1", "B2", "B3"), "headcount.xlsx", new DateTime(2026, 11, 10, 15, 0, 0), "  November  ", @"CORP\hr");

        Assert.Equal(new DateTime(2026, 11, 10), saved.DataDate);
        Assert.Equal(3, saved.RowCount);
        Assert.Equal(3m, saved.TotalHc);
        Assert.Equal(1.5m, saved.TotalFte);
        Assert.Equal("November", saved.Comment);
        Assert.Equal(@"CORP\hr", saved.UploadedBy);
        Assert.Equal(_db.Clock.Now.UtcDateTime, saved.UploadedAt);
        Assert.Empty(saved.Rows);

        await using var context = _db.CreateDbContext();
        var stored = await context.HeadcountRows.Where(r => r.UploadId == saved.Id).ToListAsync();
        Assert.Equal(["B1", "B2", "B3"], stored.OrderBy(r => r.RowNumber).Select(r => r.Brid));
    }

    [Fact]
    public async Task Saving_leaves_the_parsed_rows_untouched()
    {
        var rows = Rows("B1");

        await _db.Service().SaveAsync(rows, "headcount.xlsx", new DateTime(2026, 11, 10), null, null);

        Assert.Equal(0, rows[0].Id);
        Assert.Equal(0, rows[0].UploadId);
    }

    [Fact]
    public async Task Lists_uploads_newest_data_date_first()
    {
        var service = _db.Service();
        var october = await service.SaveAsync(Rows("B1"), "oct.xlsx", new DateTime(2026, 10, 31), null, null);
        var november = await service.SaveAsync(Rows("B1"), "nov.xlsx", new DateTime(2026, 11, 30), null, null);
        _db.Clock.Now = _db.Clock.Now.AddDays(1);
        var backfilled = await service.SaveAsync(Rows("B1"), "sep.xlsx", new DateTime(2026, 9, 30), null, null);

        var uploads = await service.GetUploadsAsync();

        Assert.Equal([november.Id, october.Id, backfilled.Id], uploads.Select(u => u.Id));
    }

    [Fact]
    public async Task Previous_upload_is_the_newest_on_or_before_the_date()
    {
        var service = _db.Service();
        var first = await service.SaveAsync(Rows("B1"), "a.xlsx", new DateTime(2026, 10, 31), null, null);
        _db.Clock.Now = _db.Clock.Now.AddHours(1);
        var correction = await service.SaveAsync(Rows("B1"), "b.xlsx", new DateTime(2026, 10, 31), null, null);
        await service.SaveAsync(Rows("B1"), "c.xlsx", new DateTime(2026, 11, 30), null, null);

        Assert.Equal(correction.Id, (await service.FindPreviousAsync(new DateTime(2026, 11, 15)))?.Id);
        Assert.Equal(correction.Id, (await service.FindPreviousAsync(new DateTime(2026, 10, 31)))?.Id);
        Assert.Null(await service.FindPreviousAsync(new DateTime(2026, 10, 30)));
        Assert.NotEqual(first.Id, correction.Id);
    }

    [Fact]
    public async Task Reads_rows_in_file_order_and_caches_them()
    {
        var service = _db.Service();
        var saved = await service.SaveAsync(Rows("B2", "B1"), "a.xlsx", new DateTime(2026, 10, 31), null, null);
        _db.Cache.Remove(saved.Id);

        var first = await service.GetRowsAsync(saved.Id);
        var second = await _db.Service().GetRowsAsync(saved.Id);

        Assert.Equal(["B2", "B1"], first.Select(r => r.Brid));
        Assert.Same(first, second);
    }

    [Fact]
    public async Task Deletes_an_upload()
    {
        var service = _db.Service();
        var keep = await service.SaveAsync(Rows("B1"), "a.xlsx", new DateTime(2026, 10, 31), null, null);
        var remove = await service.SaveAsync(Rows("B1"), "b.xlsx", new DateTime(2026, 11, 30), null, null);

        Assert.True(await service.DeleteAsync(remove.Id));
        Assert.False(await service.DeleteAsync(remove.Id));

        Assert.Equal([keep.Id], (await service.GetUploadsAsync()).Select(u => u.Id));
        Assert.False(_db.Cache.TryGet(remove.Id, out _));
    }
}
