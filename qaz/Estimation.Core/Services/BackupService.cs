using System.Diagnostics;
using Estimation.Core.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Estimation.Core.Services;

public interface IBackupService
{
    Task<BackupSettings> GetSettingsAsync();
    Task<BackupSettings> UpdateSettingsAsync(BackupSettings settings);
    Task<List<BackupHistory>> GetHistoryAsync(int maxRows = 100);
    Task<BackupHistory> RunBackupAsync(string triggeredBy, CancellationToken cancellationToken = default);
    DateTime CalculateNextRun(BackupSettings settings, DateTime fromUtc);
}

public class BackupService : IBackupService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public BackupService(IDbContextFactory<EstimationDbContext> ctx)
    {
        _ctx = ctx;
    }

    public async Task<BackupSettings> GetSettingsAsync()
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var settings = await db.BackupSettings.FirstOrDefaultAsync();

        if (settings is null)
        {
            settings = new BackupSettings
            {
                Enabled = false,
                BackupFolderPath = string.Empty,
                ScheduleType = BackupScheduleType.Daily,
                IntervalHours = 24,
                DailyTime = new TimeOnly(2, 0),
                WeeklyDay = DayOfWeek.Sunday,
                RetentionCount = 7
            };
            db.BackupSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        return settings;
    }

    public async Task<BackupSettings> UpdateSettingsAsync(BackupSettings settings)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        var existing = await db.BackupSettings.FirstOrDefaultAsync();

        if (existing is null)
        {
            db.BackupSettings.Add(settings);
        }
        else
        {
            existing.Enabled = settings.Enabled;
            existing.BackupFolderPath = settings.BackupFolderPath;
            existing.ScheduleType = settings.ScheduleType;
            existing.IntervalHours = settings.IntervalHours;
            existing.DailyTime = settings.DailyTime;
            existing.WeeklyDay = settings.WeeklyDay;
            existing.RetentionCount = settings.RetentionCount;

            if (existing.Enabled)
            {
                existing.NextBackupAt = CalculateNextRun(existing, DateTime.UtcNow);
            }
            else
            {
                existing.NextBackupAt = null;
            }
        }

        await db.SaveChangesAsync();
        return await db.BackupSettings.AsNoTracking().FirstAsync();
    }

    public async Task<List<BackupHistory>> GetHistoryAsync(int maxRows = 100)
    {
        await using var db = await _ctx.CreateDbContextAsync();
        return await db.BackupHistory
            .AsNoTracking()
            .OrderByDescending(h => h.StartedAt)
            .Take(maxRows)
            .ToListAsync();
    }

    public async Task<BackupHistory> RunBackupAsync(string triggeredBy, CancellationToken cancellationToken = default)
    {
        var history = new BackupHistory
        {
            StartedAt = DateTime.UtcNow,
            Status = "Running",
            TriggeredBy = triggeredBy
        };

        await using (var db = await _ctx.CreateDbContextAsync(cancellationToken))
        {
            db.BackupHistory.Add(history);
            await db.SaveChangesAsync(cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var db = await _ctx.CreateDbContextAsync(cancellationToken);
            var settings = await db.BackupSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

            if (settings is null || string.IsNullOrWhiteSpace(settings.BackupFolderPath))
            {
                throw new InvalidOperationException("Backup folder path is not configured.");
            }

            Directory.CreateDirectory(settings.BackupFolderPath);

            var dbName = db.Database.GetDbConnection().Database;
            if (string.IsNullOrWhiteSpace(dbName))
            {
                throw new InvalidOperationException("Cannot determine database name from connection.");
            }

            var fileName = $"{dbName}_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            var fullPath = Path.Combine(settings.BackupFolderPath, fileName);
            var backupName = $"{dbName} full backup {DateTime.UtcNow:O}";

            var previousTimeout = db.Database.GetCommandTimeout();
            db.Database.SetCommandTimeout(TimeSpan.FromHours(2));
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $"BACKUP DATABASE [{dbName}] TO DISK = @path WITH FORMAT, INIT, COMPRESSION, NAME = @name;",
                    new[]
                    {
                        new Microsoft.Data.SqlClient.SqlParameter("@path", fullPath),
                        new Microsoft.Data.SqlClient.SqlParameter("@name", backupName)
                    },
                    cancellationToken);
            }
            finally
            {
                db.Database.SetCommandTimeout(previousTimeout);
            }

            stopwatch.Stop();

            long? size = null;
            if (File.Exists(fullPath))
            {
                size = new FileInfo(fullPath).Length;
            }

            var tracked = await db.BackupHistory.FirstAsync(h => h.Id == history.Id, cancellationToken);
            tracked.FinishedAt = DateTime.UtcNow;
            tracked.Status = "Success";
            tracked.FilePath = fullPath;
            tracked.FileSizeBytes = size;
            tracked.Message = $"Completed in {stopwatch.Elapsed.TotalSeconds:F1}s";

            var settingsRow = await db.BackupSettings.FirstOrDefaultAsync(cancellationToken);
            if (settingsRow is not null)
            {
                settingsRow.LastBackupAt = DateTime.UtcNow;
                if (settingsRow.Enabled)
                {
                    settingsRow.NextBackupAt = CalculateNextRun(settingsRow, DateTime.UtcNow);
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            await PruneOldBackupsAsync(settings.BackupFolderPath, dbName, settings.RetentionCount, cancellationToken);

            Log.Information("Database backup completed: {Path} ({Size} bytes)", fullPath, size);

            return await db.BackupHistory.AsNoTracking().FirstAsync(h => h.Id == history.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database backup failed");
            try
            {
                await using var db = await _ctx.CreateDbContextAsync(CancellationToken.None);
                var tracked = await db.BackupHistory.FirstAsync(h => h.Id == history.Id, CancellationToken.None);
                tracked.FinishedAt = DateTime.UtcNow;
                tracked.Status = "Failed";
                tracked.Message = ex.Message.Length > 2000 ? ex.Message.Substring(0, 2000) : ex.Message;
                await db.SaveChangesAsync(CancellationToken.None);
                return tracked;
            }
            catch (Exception inner)
            {
                Log.Error(inner, "Failed to persist backup failure state");
                throw;
            }
        }
    }

    public DateTime CalculateNextRun(BackupSettings settings, DateTime fromUtc)
    {
        var local = fromUtc.ToLocalTime();

        switch (settings.ScheduleType)
        {
            case BackupScheduleType.Hourly:
            {
                var hours = settings.IntervalHours > 0 ? settings.IntervalHours : 1;
                return fromUtc.AddHours(hours);
            }
            case BackupScheduleType.Weekly:
            {
                var target = NextLocalOccurrence(local, settings.WeeklyDay, settings.DailyTime);
                return target.ToUniversalTime();
            }
            case BackupScheduleType.Daily:
            default:
            {
                var todayAt = new DateTime(local.Year, local.Month, local.Day,
                    settings.DailyTime.Hour, settings.DailyTime.Minute, 0, DateTimeKind.Local);

                if (todayAt <= local)
                {
                    todayAt = todayAt.AddDays(1);
                }

                return todayAt.ToUniversalTime();
            }
        }
    }

    private static DateTime NextLocalOccurrence(DateTime fromLocal, DayOfWeek day, TimeOnly time)
    {
        var todayAt = new DateTime(fromLocal.Year, fromLocal.Month, fromLocal.Day,
            time.Hour, time.Minute, 0, DateTimeKind.Local);

        var daysAhead = ((int)day - (int)fromLocal.DayOfWeek + 7) % 7;

        if (daysAhead == 0 && todayAt <= fromLocal)
        {
            daysAhead = 7;
        }

        return todayAt.AddDays(daysAhead);
    }

    private static async Task PruneOldBackupsAsync(string folder, string dbName, int retention, CancellationToken cancellationToken)
    {
        if (retention <= 0)
        {
            return;
        }

        try
        {
            var files = new DirectoryInfo(folder)
                .GetFiles($"{dbName}_*.bak")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(retention)
                .ToList();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    file.Delete();
                    Log.Information("Pruned old backup file {File}", file.FullName);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete old backup {File}", file.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate backup folder {Folder} for retention", folder);
        }

        await Task.CompletedTask;
    }
}
