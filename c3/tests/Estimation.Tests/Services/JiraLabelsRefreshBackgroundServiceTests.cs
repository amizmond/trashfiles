using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Services.JiraIntegration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Estimation.Tests.Services;

public class JiraLabelsRefreshBackgroundServiceTests
{
    private sealed class StubJiraSyncService : IJiraSyncService
    {
        public JiraSyncSettings Settings { get; set; } = new();
        public bool Connected { get; set; } = true;
        public int ConnectionChecks;

        public Task<JiraSyncSettings> GetSettingsAsync() => Task.FromResult(Settings);

        public Task<bool> IsServiceAccountConnectedAsync()
        {
            ConnectionChecks++;
            return Task.FromResult(Connected);
        }

        public Task<JiraSyncSettings> StartAsync(int cooldownMinutes) => Task.FromResult(Settings);
        public Task<JiraSyncSettings> StopAsync() => Task.FromResult(Settings);
        public Task UpdateJiraTimeZoneAsync(string? timeZoneId) => Task.CompletedTask;
        public Task SetLabelsSyncEnabledAsync(bool enabled) => Task.CompletedTask;
        public Task<List<JiraSyncKeyOverview>> GetKeysAsync() => Task.FromResult(new List<JiraSyncKeyOverview>());
        public Task<JiraSyncKeySaveResult> SaveKeyAsync(JiraSyncKey settings) => Task.FromResult(new JiraSyncKeySaveResult(settings, false));
        public Task<JiraSyncKey> AddKeyAsync(string jiraKey) => Task.FromResult(new JiraSyncKey { JiraKey = jiraKey });
        public Task<JiraSyncKeyItemCounts> CountSyncedItemsAsync(string jiraKey) => Task.FromResult(new JiraSyncKeyItemCounts(0, 0, 0, 0));
        public Task RemoveKeyAsync(string jiraKey) => Task.CompletedTask;
        public Task ResetWatermarkAsync(string jiraKey) => Task.CompletedTask;
        public Task<List<JiraSyncHistory>> GetHistoryAsync(int maxRows = 100) => Task.FromResult(new List<JiraSyncHistory>());
        public Task<JiraSyncHistory> RunSyncAsync(string triggeredBy, CancellationToken cancellationToken = default) => Task.FromResult(new JiraSyncHistory());
        public DateTime CalculateNextRun(JiraSyncSettings settings, DateTime fromUtc) => fromUtc;
    }

    private sealed class StubLabelsRefreshService : IJiraLabelsRefreshService
    {
        public string? DueKey { get; set; }
        public int DueKeyLookups;
        public CancellationToken LastLookupToken;

        public Task<string?> GetNextDueKeyAsync(CancellationToken cancellationToken = default)
        {
            DueKeyLookups++;
            LastLookupToken = cancellationToken;
            return Task.FromResult(DueKey);
        }

        public Task<List<JiraLabelsKeyStatus>> GetStatusAsync() => Task.FromResult(new List<JiraLabelsKeyStatus>());
    }

    private sealed class StubMetadataService : IJiraMetadataService
    {
        public string? Error { get; set; }
        public List<string> Refreshed { get; } = [];
        public CancellationToken LastRefreshToken;

        public Task<JiraLabelRefreshResult> RefreshLabelsAsync(string projectKey, CancellationToken cancellationToken = default)
        {
            Refreshed.Add(projectKey);
            LastRefreshToken = cancellationToken;
            return Task.FromResult(Error is null
                ? JiraLabelRefreshResult.Success(projectKey, [new JiraLabel { Name = "a" }])
                : JiraLabelRefreshResult.Failure(projectKey, Error));
        }

        public Task<List<JiraLabel>> GetLabelsAsync(string userName, string projectKey) => throw new NotSupportedException();
        public Task<List<JiraStatus>> GetStatusesAsync(string userName, string projectKey) => throw new NotSupportedException();
        public Task<JiraProject?> GetProjectAsync(string userName, string projectKey) => throw new NotSupportedException();
    }

    private sealed class TestableService : JiraLabelsRefreshBackgroundService
    {
        public TestableService(IServiceProvider services) : base(services)
        {
        }

        public Task TickOnceAsync(IServiceProvider scoped, CancellationToken cancellationToken = default) =>
            TickAsync(scoped, cancellationToken);

        public string ServiceNameValue => ServiceName;

        public TimeSpan PollIntervalValue => PollInterval;

        public TimeSpan StartupDelayValue => StartupDelay;
    }

    private sealed record Harness(
        TestableService Service,
        IServiceProvider Scope,
        StubJiraSyncService Sync,
        StubLabelsRefreshService Labels,
        StubMetadataService Metadata);

    private static Harness Build(bool enabled = true, bool connected = true, string? dueKey = "PAY")
    {
        var sync = new StubJiraSyncService
        {
            Settings = new JiraSyncSettings { LabelsSyncEnabled = enabled },
            Connected = connected,
        };
        var labels = new StubLabelsRefreshService { DueKey = dueKey };
        var metadata = new StubMetadataService();

        var provider = new ServiceCollection()
            .AddSingleton<IJiraSyncService>(sync)
            .AddSingleton<IJiraLabelsRefreshService>(labels)
            .AddSingleton<IJiraMetadataService>(metadata)
            .BuildServiceProvider();

        return new Harness(new TestableService(provider), provider, sync, labels, metadata);
    }

    [Fact]
    public async Task Does_nothing_while_the_labels_refresh_is_switched_off()
    {
        var h = Build(enabled: false);

        await h.Service.TickOnceAsync(h.Scope);

        Assert.Equal(0, h.Sync.ConnectionChecks);
        Assert.Equal(0, h.Labels.DueKeyLookups);
        Assert.Empty(h.Metadata.Refreshed);
    }

    [Fact]
    public async Task Waits_for_the_service_account_to_be_connected()
    {
        var h = Build(connected: false);

        await h.Service.TickOnceAsync(h.Scope);

        Assert.Equal(1, h.Sync.ConnectionChecks);
        Assert.Equal(0, h.Labels.DueKeyLookups);
        Assert.Empty(h.Metadata.Refreshed);
    }

    [Fact]
    public async Task Refreshes_the_next_due_key_with_the_stopping_token()
    {
        var h = Build(dueKey: "LOAN");
        using var stopping = new CancellationTokenSource();

        await h.Service.TickOnceAsync(h.Scope, stopping.Token);

        Assert.Equal(new[] { "LOAN" }, h.Metadata.Refreshed);
        Assert.Equal(stopping.Token, h.Metadata.LastRefreshToken);
        Assert.Equal(stopping.Token, h.Labels.LastLookupToken);
    }

    [Fact]
    public async Task Refreshes_one_key_per_tick()
    {
        var h = Build(dueKey: "PAY");

        await h.Service.TickOnceAsync(h.Scope);
        h.Labels.DueKey = "HR";
        await h.Service.TickOnceAsync(h.Scope);

        Assert.Equal(new[] { "PAY", "HR" }, h.Metadata.Refreshed);
    }

    [Fact]
    public async Task Does_nothing_when_no_key_is_due()
    {
        var h = Build(dueKey: null);

        await h.Service.TickOnceAsync(h.Scope);

        Assert.Equal(1, h.Labels.DueKeyLookups);
        Assert.Empty(h.Metadata.Refreshed);
    }

    [Fact]
    public async Task A_failed_refresh_does_not_stop_the_next_tick()
    {
        var h = Build(dueKey: "PAY");
        h.Metadata.Error = "Jira returned 500";

        await h.Service.TickOnceAsync(h.Scope);
        await h.Service.TickOnceAsync(h.Scope);

        Assert.Equal(new[] { "PAY", "PAY" }, h.Metadata.Refreshed);
    }

    [Fact]
    public async Task Stops_refreshing_on_the_next_tick_after_the_switch_is_turned_off()
    {
        var h = Build(dueKey: "PAY");

        await h.Service.TickOnceAsync(h.Scope);
        h.Sync.Settings.LabelsSyncEnabled = false;
        await h.Service.TickOnceAsync(h.Scope);

        Assert.Equal(new[] { "PAY" }, h.Metadata.Refreshed);
    }

    [Fact]
    public void Polls_every_minute_after_a_30_second_start_delay()
    {
        var h = Build();

        Assert.Equal(TimeSpan.FromMinutes(1), h.Service.PollIntervalValue);
        Assert.Equal(TimeSpan.FromSeconds(30), h.Service.StartupDelayValue);
        Assert.Equal(nameof(JiraLabelsRefreshBackgroundService), h.Service.ServiceNameValue);
    }
}
