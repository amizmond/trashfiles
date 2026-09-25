using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.JiraIntegration.Services;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraLabelsRefreshServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryDatabase _db = new();
    private readonly JiraLabelsRefreshService _service;

    public JiraLabelsRefreshServiceTests()
    {
        _service = new JiraLabelsRefreshService(_db, new FixedTimeProvider(Now));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTime now)
        {
            _now = new DateTimeOffset(now);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private Task SyncKeysAsync(params string[] keys) =>
        _db.SeedAsync(db => db.JiraSyncKeys.AddRange(keys.Select(k => new JiraSyncKey { JiraKey = k })));

    private Task ArtKeysAsync(params string[] keys) =>
        _db.SeedAsync(db => db.CapitalProjects.Add(new Art
        {
            Name = "Payments",
            JiraKeys = keys.Select(k => new ArtJiraKey { JiraKey = k }).ToList(),
        }));

    private Task RowAsync(string key, DateTime? refreshedAt, DateTime? attemptedAt = null, string? error = null, string labelsJson = "[\"a\",\"b\"]") =>
        _db.SeedAsync(db => db.JiraLabelCaches.Add(new JiraLabelCache
        {
            CacheKey = key,
            LabelsJson = labelsJson,
            UpdatedAt = refreshedAt,
            LastAttemptAt = attemptedAt ?? refreshedAt,
            LastError = error,
        }));

    [Fact]
    public async Task Nothing_is_due_without_keys()
    {
        await RowAsync("OLD", Now.AddDays(-3));

        Assert.Null(await _service.GetNextDueKeyAsync());
    }

    [Fact]
    public async Task A_key_never_fetched_comes_before_stale_ones()
    {
        await SyncKeysAsync("AAA", "ZZZ");
        await RowAsync("AAA", Now.AddDays(-3));

        Assert.Equal("ZZZ", await _service.GetNextDueKeyAsync());
    }

    [Fact]
    public async Task Among_stale_keys_the_longest_unrefreshed_comes_first()
    {
        await SyncKeysAsync("AAA", "BBB", "CCC");
        await RowAsync("AAA", Now.AddHours(-7));
        await RowAsync("BBB", Now.AddHours(-30));
        await RowAsync("CCC", Now.AddHours(-1));

        Assert.Equal("BBB", await _service.GetNextDueKeyAsync());
    }

    [Fact]
    public async Task A_key_refreshed_within_six_hours_is_not_due()
    {
        await SyncKeysAsync("PAY");
        await RowAsync("PAY", Now.AddHours(-5).AddMinutes(-59));

        Assert.Null(await _service.GetNextDueKeyAsync());
    }

    [Fact]
    public async Task A_key_whose_refresh_failed_waits_half_an_hour_before_the_next_try()
    {
        await SyncKeysAsync("PAY", "HR");
        await RowAsync("PAY", Now.AddDays(-2), attemptedAt: Now.AddMinutes(-29), error: "Jira returned 500");
        await RowAsync("HR", null, attemptedAt: Now.AddMinutes(-10), error: "Jira returned 400");

        Assert.Null(await _service.GetNextDueKeyAsync());
    }

    [Fact]
    public async Task A_failed_key_is_tried_again_after_half_an_hour()
    {
        await SyncKeysAsync("PAY");
        await RowAsync("PAY", null, attemptedAt: Now.AddMinutes(-30), error: "Jira returned 500");

        Assert.Equal("PAY", await _service.GetNextDueKeyAsync());
    }

    [Fact]
    public async Task Art_keys_are_refreshed_too_and_each_key_only_once()
    {
        await ArtKeysAsync("loan", "CARD");
        await SyncKeysAsync("LOAN");
        await RowAsync("CARD", Now.AddHours(-1));

        Assert.Equal("LOAN", await _service.GetNextDueKeyAsync());
        var status = await _service.GetStatusAsync();
        Assert.Equal(new[] { "CARD", "LOAN" }, status.Select(s => s.JiraKey));
    }

    [Fact]
    public async Task The_status_describes_every_configured_key()
    {
        await SyncKeysAsync("CARD", "HR", "LOAN", "PAY");
        await RowAsync("CARD", Now.AddHours(-1));
        await RowAsync("HR", Now.AddHours(-9));
        await RowAsync("LOAN", null, attemptedAt: Now.AddMinutes(-5), error: "Jira returned 403", labelsJson: "[]");
        await RowAsync("OLD", Now.AddHours(-1));

        var status = (await _service.GetStatusAsync()).ToDictionary(s => s.JiraKey);

        Assert.Equal(new[] { "CARD", "HR", "LOAN", "PAY" }, status.Keys.OrderBy(k => k));
        Assert.True(status["CARD"].IsFresh);
        Assert.Equal(2, status["CARD"].LabelCount);
        Assert.False(status["HR"].IsFresh);
        Assert.Equal(2, status["HR"].LabelCount);
        Assert.True(status["LOAN"].LastAttemptFailed);
        Assert.Equal("Jira returned 403", status["LOAN"].LastError);
        Assert.Null(status["LOAN"].RefreshedAt);
        Assert.Equal(0, status["LOAN"].LabelCount);
        Assert.Null(status["PAY"].RefreshedAt);
        Assert.False(status["PAY"].LastAttemptFailed);
    }
}
