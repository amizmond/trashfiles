using Estimation.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Estimation.Services;

public class BackupBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _services;

    public BackupBackgroundService(IServiceProvider services)
    {
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("BackupBackgroundService started (poll interval: {Interval})", PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BackupBackgroundService tick failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Log.Information("BackupBackgroundService stopped");
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var backup = scope.ServiceProvider.GetRequiredService<IBackupService>();

        var settings = await backup.GetSettingsAsync();

        if (!settings.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.BackupFolderPath))
        {
            Log.Warning("Backup is enabled but folder path is empty; skipping tick");
            return;
        }

        var now = DateTime.UtcNow;

        if (settings.NextBackupAt is null)
        {
            await backup.UpdateSettingsAsync(settings);
            return;
        }

        if (settings.NextBackupAt > now)
        {
            return;
        }

        Log.Information("Scheduled database backup triggered (scheduled at {Scheduled})", settings.NextBackupAt);
        await backup.RunBackupAsync("Scheduler", cancellationToken);
    }
}
