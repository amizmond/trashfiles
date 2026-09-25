using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Services.JiraIntegration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Estimation.Tests.Services;

public class JiraSyncBackgroundServiceTests
{
    private sealed class StubJiraSyncService : IJiraSyncService
    {
        public JiraSyncSettings Settings { get; set; } = new();
        public bool Connected { get; set; } = true;
        public Exception? ThrowOnRun { get; set; }

        public int RunCalls;
        public int SettingsReads;
        public int ConnectionChecks;

        public string? LastTriggeredBy;
        public CancellationToken LastRunToken;

        public Task<JiraSyncSettings> GetSettingsAsync()
        {
            SettingsReads++;
            return Task.FromResult(Settings);
        }

        public Task<JiraSyncSettings> StartAsync(int cooldownMinutes) => Task.FromResult(Settings);
        public Task<JiraSyncSettings> StopAsync() => Task.FromResult(Settings);
        public Task UpdateJiraTimeZoneAsync(string? timeZoneId) => Task.CompletedTask;
        public Task<List<JiraSyncKeyOverview>> GetKeysAsync() => Task.FromResult(new List<JiraSyncKeyOverview>());
        public Task<JiraSyncKeySaveResult> SaveKeyAsync(JiraSyncKey settings) => Task.FromResult(new JiraSyncKeySaveResult(settings, false));
        public Task<JiraSyncKey> AddKeyAsync(string jiraKey) => Task.FromResult(new JiraSyncKey { JiraKey = jiraKey });
        public Task<JiraSyncKeyItemCounts> CountSyncedItemsAsync(string jiraKey) => Task.FromResult(new JiraSyncKeyItemCounts(0, 0, 0, 0));
        public Task RemoveKeyAsync(string jiraKey) => Task.CompletedTask;
        public Task ResetWatermarkAsync(string jiraKey) => Task.CompletedTask;
        public Task<List<JiraSyncHistory>> GetHistoryAsync(int maxRows = 100) => Task.FromResult(new List<JiraSyncHistory>());
        public DateTime CalculateNextRun(JiraSyncSettings settings, DateTime fromUtc) => fromUtc.AddMinutes(5);

        public Task<bool> IsServiceAccountConnectedAsync()
        {
            ConnectionChecks++;
            return Task.FromResult(Connected);
        }

        public Task<JiraSyncHistory> RunSyncAsync(string triggeredBy, CancellationToken cancellationToken = default)
        {
            RunCalls++;
            LastTriggeredBy = triggeredBy;
            LastRunToken = cancellationToken;

            if (ThrowOnRun is not null)
            {
                throw ThrowOnRun;
            }

            return Task.FromResult(new JiraSyncHistory());
        }
    }

    private sealed class TestableService : JiraSyncBackgroundService
    {
        public TestableService(IServiceProvider services) : base(services)
        {
        }

        public Task TickOnceAsync(IServiceProvider scoped, CancellationToken cancellationToken = default) =>
            TickAsync(scoped, cancellationToken);

        public string ServiceNameValue => ServiceName;

        public TimeSpan PollIntervalValue => PollInterval;

        public TimeSpan StartupDelayValue => StartupDelay;

        public (bool Started, string? Reason) TryStartResult()
        {
            var started = TryStart(out var reason);
            return (started, reason);
        }
    }

    private static (TestableService Service, IServiceProvider Scope, StubJiraSyncService Stub) Build(
        Action<StubJiraSyncService> configure)
    {
        var stub = new StubJiraSyncService();
        configure(stub);

        var provider = new ServiceCollection()
            .AddSingleton<IJiraSyncService>(stub)
            .BuildServiceProvider();

        return (new TestableService(provider), provider, stub);
    }

    [Fact]
    public async Task Does_not_run_while_disabled()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings
        {
            Enabled = false,
            NextRunAt = DateTime.UtcNow.AddMinutes(-1)
        });

        await service.TickOnceAsync(scope);

        Assert.Equal(0, stub.RunCalls);
    }

    [Fact]
    public async Task Does_not_even_probe_the_connection_while_disabled()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings
        {
            Enabled = false,
            NextRunAt = DateTime.UtcNow.AddMinutes(-1)
        });

        await service.TickOnceAsync(scope);

        Assert.Equal(0, stub.ConnectionChecks);
    }

    [Fact]
    public async Task Does_not_run_before_its_next_run_time()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings
        {
            Enabled = true,
            NextRunAt = DateTime.UtcNow.AddMinutes(10)
        });

        await service.TickOnceAsync(scope);

        Assert.Equal(0, stub.RunCalls);
        Assert.Equal(0, stub.ConnectionChecks);
    }

    [Fact]
    public async Task Waits_for_the_service_account_to_be_connected()
    {
        var (service, scope, stub) = Build(s =>
        {
            s.Settings = new JiraSyncSettings { Enabled = true, NextRunAt = DateTime.UtcNow.AddMinutes(-1) };
            s.Connected = false;
        });

        await service.TickOnceAsync(scope);

        Assert.Equal(1, stub.ConnectionChecks);
        Assert.Equal(0, stub.RunCalls);
    }

    [Fact]
    public async Task Picks_the_sync_back_up_once_the_account_reconnects()
    {
        var (service, scope, stub) = Build(s =>
        {
            s.Settings = new JiraSyncSettings { Enabled = true, NextRunAt = DateTime.UtcNow.AddMinutes(-1) };
            s.Connected = false;
        });

        await service.TickOnceAsync(scope);
        stub.Connected = true;
        await service.TickOnceAsync(scope);

        Assert.Equal(1, stub.RunCalls);
    }

    [Fact]
    public async Task Runs_when_due_and_connected()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings
        {
            Enabled = true,
            NextRunAt = DateTime.UtcNow.AddMinutes(-1)
        });

        await service.TickOnceAsync(scope);

        Assert.Equal(1, stub.RunCalls);
    }

    [Fact]
    public async Task Runs_immediately_when_no_next_run_is_recorded()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings
        {
            Enabled = true,
            NextRunAt = null
        });

        await service.TickOnceAsync(scope);

        Assert.Equal(1, stub.RunCalls);
    }

    [Fact]
    public async Task Runs_as_soon_as_the_scheduled_moment_is_reached()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings
        {
            Enabled = true,
            NextRunAt = DateTime.UtcNow
        });

        await service.TickOnceAsync(scope);

        Assert.Equal(1, stub.RunCalls);
    }

    [Fact]
    public async Task Runs_at_most_once_per_tick()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings
        {
            Enabled = true,
            NextRunAt = DateTime.UtcNow.AddMinutes(-1)
        });

        await service.TickOnceAsync(scope);

        Assert.Equal(1, stub.SettingsReads);
        Assert.Equal(1, stub.RunCalls);
    }

    [Fact]
    public async Task Attributes_the_run_to_the_scheduler()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings { Enabled = true });

        await service.TickOnceAsync(scope);

        Assert.Equal("Scheduler", stub.LastTriggeredBy);
    }

    [Fact]
    public async Task Passes_the_shutdown_token_down_to_the_sync()
    {
        var (service, scope, stub) = Build(s => s.Settings = new JiraSyncSettings { Enabled = true });

        using var cts = new CancellationTokenSource();
        await service.TickOnceAsync(scope, cts.Token);

        Assert.Equal(cts.Token, stub.LastRunToken);
    }

    [Fact]
    public async Task A_failed_sync_is_left_for_the_loop_to_log()
    {
        var (service, scope, _) = Build(s =>
        {
            s.Settings = new JiraSyncSettings { Enabled = true };
            s.ThrowOnRun = new HttpRequestException("Jira is down");
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => service.TickOnceAsync(scope));
    }

    [Fact]
    public void Polls_every_minute_with_no_startup_delay()
    {
        var (service, _, _) = Build(_ => { });

        Assert.Equal(TimeSpan.FromMinutes(1), service.PollIntervalValue);
        Assert.Equal(TimeSpan.Zero, service.StartupDelayValue);
    }

    [Fact]
    public void Is_always_enabled_because_the_schedule_lives_in_the_database()
    {
        var (service, _, _) = Build(_ => { });

        var (started, reason) = service.TryStartResult();

        Assert.True(started);
        Assert.Null(reason);
        Assert.Equal(nameof(JiraSyncBackgroundService), service.ServiceNameValue);
    }
}
