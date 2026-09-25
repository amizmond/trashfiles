using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Services;
using Serilog;

namespace Estimation.Services.JiraIntegration;

public class JiraLabelsRefreshBackgroundService : PollingBackgroundService
{
    public JiraLabelsRefreshBackgroundService(IServiceProvider services) : base(services)
    {
    }

    protected override string ServiceName => nameof(JiraLabelsRefreshBackgroundService);

    protected override TimeSpan PollInterval => TimeSpan.FromMinutes(1);

    protected override TimeSpan StartupDelay => TimeSpan.FromSeconds(30);

    protected override async Task TickAsync(IServiceProvider scopedServices, CancellationToken cancellationToken)
    {
        var sync = scopedServices.GetRequiredService<IJiraSyncService>();

        var settings = await sync.GetSettingsAsync();
        if (!settings.LabelsSyncEnabled)
        {
            return;
        }

        if (!await sync.IsServiceAccountConnectedAsync())
        {
            return;
        }

        var key = await scopedServices.GetRequiredService<IJiraLabelsRefreshService>().GetNextDueKeyAsync(cancellationToken);
        if (key is null)
        {
            return;
        }

        var result = await scopedServices.GetRequiredService<IJiraMetadataService>().RefreshLabelsAsync(key, cancellationToken);
        if (result.Succeeded)
        {
            Log.Information("{Service} refreshed {Count} Jira labels for {Key}", ServiceName, result.Labels.Count, key);
        }
    }
}
