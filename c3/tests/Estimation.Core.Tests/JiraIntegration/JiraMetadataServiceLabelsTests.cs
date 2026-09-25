using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraMetadataServiceLabelsTests
{
    private const string ServiceUser = JiraSyncSettings.DefaultServiceAccountUserName;
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Earlier = Now.AddHours(-8);

    private readonly InMemoryDatabase _db = new();
    private readonly FakeJira _jira = new();
    private readonly StubAuth _auth = new();
    private readonly TestTimeProvider _time = new(Now);
    private readonly JiraMetadataService _service;

    public JiraMetadataServiceLabelsTests()
    {
        _service = ServiceOver(_db);
    }

    private JiraMetadataService ServiceOver(IDbContextFactory<EstimationDbContext> db) => new(
        _auth,
        Options.Create(new JiraSettings { Url = "https://jira.example.test" }),
        new MemoryCache(new MemoryCacheOptions()),
        new StubHttpClientFactory(_jira),
        db,
        _time);

    private sealed class TestTimeProvider : TimeProvider
    {
        public TestTimeProvider(DateTime now)
        {
            UtcNow = new DateTimeOffset(now);
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FailingSaves : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("The transaction log is full.");
    }

    private sealed class ReadOnlyDatabase : IDbContextFactory<EstimationDbContext>
    {
        private readonly DbContextOptions<EstimationDbContext> _options;

        public ReadOnlyDatabase(string name, InMemoryDatabaseRoot root)
        {
            _options = new DbContextOptionsBuilder<EstimationDbContext>()
                .UseInMemoryDatabase(name, root)
                .AddInterceptors(new FailingSaves())
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options;
        }

        public EstimationDbContext CreateDbContext() => new(_options);
    }

    private sealed class StubAuth : IJiraAuthService
    {
        public HashSet<string> Connected { get; } = new(StringComparer.Ordinal);
        public List<string> TokenRequests { get; } = [];

        public bool IsConfigured => true;

        public Task<JiraToken?> GetStoredTokenAsync(string userName)
        {
            TokenRequests.Add(userName);
            return Task.FromResult(Connected.Contains(userName)
                ? new JiraToken { UserName = userName, AccessToken = $"token-of-{userName}", AccessTokenSecret = "s" }
                : null);
        }

        public string BuildOAuthHeader(string httpMethod, string url, string accessToken) => $"OAuth {accessToken}";

        public Task<JiraRequestToken> GetRequestTokenAsync() => throw new NotSupportedException();
        public Task<(string AccessToken, string AccessTokenSecret)> ExchangeAccessTokenAsync(string requestToken, string requestTokenSecret, string verifier) => throw new NotSupportedException();
        public Task<bool> IsAuthenticatedAsync(string userName) => throw new NotSupportedException();
        public Task SaveTokenAsync(string userName, string accessToken, string accessTokenSecret) => throw new NotSupportedException();
        public Task LogoutAsync(string userName) => throw new NotSupportedException();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeJira : HttpMessageHandler
    {
        private static readonly Regex ProjectPattern = new("project = \"([^\"]*)\"");

        public Dictionary<string, List<string[]>> Issues { get; } = new(StringComparer.Ordinal);
        public int? PageCap { get; set; }
        public Func<int, HttpStatusCode?> FailAt { get; set; } = _ => null;
        public Queue<HttpStatusCode> StatusResponses { get; } = new();
        public TaskCompletionSource? Hold { get; set; }
        public List<string> Jqls { get; } = [];
        public List<int> StartAts { get; } = [];
        public int Requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (Hold is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }

            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("/statuses", StringComparison.Ordinal))
            {
                var status = StatusResponses.Count > 0 ? StatusResponses.Dequeue() : HttpStatusCode.OK;
                return status == HttpStatusCode.OK
                    ? Json("""[{"statuses":[{"name":"Done"},{"name":"Open"}]}]""")
                    : new HttpResponseMessage(status);
            }

            var query = ParseQuery(uri.Query);
            var jql = query["jql"];
            var startAt = int.Parse(query["startAt"]);
            var requested = int.Parse(query["maxResults"]);
            lock (Jqls)
            {
                Jqls.Add(jql);
                StartAts.Add(startAt);
            }

            if (FailAt(startAt) is { } failure)
            {
                return new HttpResponseMessage(failure);
            }

            var project = ProjectPattern.Match(jql).Groups[1].Value;
            var issues = Issues.TryGetValue(project, out var list) ? list : [];
            var pageSize = Math.Min(requested, PageCap ?? requested);
            var page = issues.Skip(startAt).Take(pageSize)
                .Select(labels => new { fields = new { labels } });

            return Json(JsonSerializer.Serialize(new { startAt, maxResults = pageSize, total = issues.Count, issues = page }));
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static Dictionary<string, string> ParseQuery(string query) =>
            query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
    }

    private void InJira(string project, params string[][] issues) => _jira.Issues[project] = issues.ToList();

    private Task ConnectServiceAccountAsync()
    {
        _auth.Connected.Add(ServiceUser);
        return _db.SeedAsync(db =>
        {
            db.JiraSyncSettings.Add(new JiraSyncSettings());
            db.JiraTokens.Add(new JiraToken { UserName = ServiceUser, AccessToken = "t", AccessTokenSecret = "s" });
        });
    }

    private Task SaveRowAsync(string key, DateTime? updatedAt, params string[] labels) => _db.SeedAsync(db =>
        db.JiraLabelCaches.Add(new JiraLabelCache
        {
            CacheKey = key,
            LabelsJson = JsonSerializer.Serialize(labels),
            UpdatedAt = updatedAt,
            LastAttemptAt = updatedAt,
        }));

    private Task<JiraLabelCache?> RowAsync(string key) =>
        _db.ReadAsync(db => db.JiraLabelCaches.AsNoTracking().SingleOrDefaultAsync(c => c.CacheKey == key));

    private static List<string> Names(IEnumerable<JiraLabel> labels) => labels.Select(l => l.Name).ToList();

    private static string[] Labels(params string[] labels) => labels;

    [Fact]
    public async Task A_key_never_fetched_is_fetched_with_the_service_account_and_saved()
    {
        await ConnectServiceAccountAsync();
        _auth.Connected.Add("alice");
        InJira("PAY", Labels("beta", "alpha"), Labels("alpha"));

        var labels = await _service.GetLabelsAsync("alice", "PAY");

        Assert.Equal(new[] { "alpha", "beta" }, Names(labels));
        Assert.Equal(new[] { ServiceUser }, _auth.TokenRequests);
        Assert.Equal("project = \"PAY\" AND labels is not EMPTY ORDER BY key ASC", Assert.Single(_jira.Jqls));
        var row = await RowAsync("PAY");
        Assert.NotNull(row);
        Assert.Equal(Now, row.UpdatedAt);
        Assert.Equal(Now, row.LastAttemptAt);
        Assert.Null(row.LastError);
        Assert.Equal(new[] { "alpha", "beta" }, JsonSerializer.Deserialize<string[]>(row.LabelsJson));
    }

    [Fact]
    public async Task A_saved_list_is_served_without_asking_jira()
    {
        await ConnectServiceAccountAsync();
        await SaveRowAsync("PAY", Earlier, "saved");

        var first = await _service.GetLabelsAsync("alice", "PAY");
        var second = await _service.GetLabelsAsync("alice", "PAY");

        Assert.Equal(new[] { "saved" }, Names(first));
        Assert.Equal(new[] { "saved" }, Names(second));
        Assert.Equal(0, _jira.Requests);
    }

    [Fact]
    public async Task A_row_left_by_a_failed_refresh_is_not_served_as_an_empty_list()
    {
        await ConnectServiceAccountAsync();
        await SaveRowAsync("PAY", updatedAt: null);
        InJira("PAY", Labels("alpha"));

        var labels = await _service.GetLabelsAsync("alice", "PAY");

        Assert.Equal(new[] { "alpha" }, Names(labels));
        Assert.Equal(1, _jira.Requests);
    }

    [Fact]
    public async Task Without_a_connected_service_account_the_reader_s_own_token_is_used()
    {
        _auth.Connected.Add("alice");
        await _db.SeedAsync(db => db.JiraTokens.Add(new JiraToken { UserName = "alice", AccessToken = "a", AccessTokenSecret = "s" }));
        InJira("PAY", Labels("alpha"));

        var labels = await _service.GetLabelsAsync("alice", "PAY");

        Assert.Equal(new[] { "alpha" }, Names(labels));
        Assert.Equal(new[] { "alice" }, _auth.TokenRequests);
    }

    [Fact]
    public async Task A_key_is_matched_whatever_its_case_or_spacing()
    {
        await ConnectServiceAccountAsync();
        InJira("PAY", Labels("alpha"));

        await _service.GetLabelsAsync("alice", " pay ");
        var again = await _service.GetLabelsAsync("alice", "Pay");

        Assert.Equal(new[] { "alpha" }, Names(again));
        Assert.Equal(1, _jira.Requests);
        Assert.NotNull(await RowAsync("PAY"));
    }

    [Theory]
    [InlineData("PAY\" OR project = \"HR")]
    [InlineData("1PAY")]
    [InlineData("")]
    public async Task A_value_that_is_not_a_project_key_never_reaches_jira(string projectKey)
    {
        await ConnectServiceAccountAsync();

        var labels = await _service.GetLabelsAsync("alice", projectKey);
        var refresh = await _service.RefreshLabelsAsync(projectKey);

        Assert.Empty(labels);
        Assert.False(refresh.Succeeded);
        Assert.Equal(0, _jira.Requests);
    }

    [Fact]
    public async Task A_failed_first_fetch_throws_and_saves_nothing()
    {
        await ConnectServiceAccountAsync();
        InJira("PAY", Labels("alpha"));
        _jira.FailAt = _ => HttpStatusCode.Forbidden;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetLabelsAsync("alice", "PAY"));

        Assert.Contains("403", error.Message);
        Assert.Null(await RowAsync("PAY"));
    }

    [Fact]
    public async Task Readers_right_after_a_failed_fetch_get_its_error_without_asking_jira_again()
    {
        await ConnectServiceAccountAsync();
        InJira("PAY", Labels("alpha"));
        _jira.FailAt = _ => HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetLabelsAsync("alice", "PAY"));
        var requests = _jira.Requests;
        _jira.FailAt = _ => null;
        _time.UtcNow = _time.UtcNow.AddSeconds(29);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetLabelsAsync("bob", "PAY"));

        Assert.Contains("503", error.Message);
        Assert.Equal(requests, _jira.Requests);
    }

    [Fact]
    public async Task A_failed_fetch_is_tried_again_after_half_a_minute()
    {
        await ConnectServiceAccountAsync();
        InJira("PAY", Labels("alpha"));
        _jira.FailAt = _ => HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetLabelsAsync("alice", "PAY"));
        _jira.FailAt = _ => null;
        _time.UtcNow = _time.UtcNow.AddSeconds(30);

        var labels = await _service.GetLabelsAsync("alice", "PAY");

        Assert.Equal(new[] { "alpha" }, Names(labels));
    }

    [Fact]
    public async Task One_reader_s_failure_does_not_block_a_reader_with_a_different_token()
    {
        _auth.Connected.Add("bob");
        InJira("PAY", Labels("alpha"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetLabelsAsync("alice", "PAY"));
        var labels = await _service.GetLabelsAsync("bob", "PAY");

        Assert.Equal(new[] { "alpha" }, Names(labels));
        Assert.Equal(new[] { "alice", "bob" }, _auth.TokenRequests);
    }

    [Fact]
    public async Task Pages_follow_the_page_size_jira_actually_returns()
    {
        await ConnectServiceAccountAsync();
        InJira("PAY", Labels("a"), Labels("b"), Labels("c"), Labels("d"), Labels("e"));
        _jira.PageCap = 2;

        var labels = await _service.GetLabelsAsync("alice", "PAY");

        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, Names(labels));
        Assert.Equal(new[] { 0, 2, 4 }, _jira.StartAts.OrderBy(s => s));
    }

    [Fact]
    public async Task A_refresh_that_loses_a_page_keeps_the_previous_list_and_records_the_error()
    {
        await ConnectServiceAccountAsync();
        await SaveRowAsync("PAY", Earlier, "old");
        InJira("PAY", Labels("a"), Labels("b"), Labels("c"));
        _jira.PageCap = 1;
        _jira.FailAt = startAt => startAt == 2 ? HttpStatusCode.InternalServerError : null;

        var result = await _service.RefreshLabelsAsync("PAY");

        Assert.False(result.Succeeded);
        Assert.Contains("500", result.Error);
        var row = await RowAsync("PAY");
        Assert.NotNull(row);
        Assert.Equal(new[] { "old" }, JsonSerializer.Deserialize<string[]>(row.LabelsJson));
        Assert.Equal(Earlier, row.UpdatedAt);
        Assert.Equal(Now, row.LastAttemptAt);
        Assert.Contains("500", row.LastError);
        Assert.Equal(new[] { "old" }, Names(await _service.GetLabelsAsync("alice", "PAY")));
    }

    [Fact]
    public async Task A_refresh_of_a_key_never_fetched_records_the_error_without_a_list()
    {
        await ConnectServiceAccountAsync();
        _jira.FailAt = _ => HttpStatusCode.BadRequest;

        var result = await _service.RefreshLabelsAsync("PAY");

        Assert.False(result.Succeeded);
        var row = await RowAsync("PAY");
        Assert.NotNull(row);
        Assert.Null(row.UpdatedAt);
        Assert.Equal(Now, row.LastAttemptAt);
        Assert.Contains("400", row.LastError);
    }

    [Fact]
    public async Task A_successful_refresh_replaces_the_list_clears_the_error_and_is_served_at_once()
    {
        await ConnectServiceAccountAsync();
        await _db.SeedAsync(db => db.JiraLabelCaches.Add(new JiraLabelCache
        {
            CacheKey = "PAY",
            LabelsJson = """["old"]""",
            UpdatedAt = Earlier,
            LastAttemptAt = Earlier,
            LastError = "Jira returned 500",
        }));
        Assert.Equal(new[] { "old" }, Names(await _service.GetLabelsAsync("alice", "PAY")));
        InJira("PAY", Labels("new"));

        var result = await _service.RefreshLabelsAsync("pay");
        var requestsAfterRefresh = _jira.Requests;
        var served = await _service.GetLabelsAsync("alice", "PAY");

        Assert.True(result.Succeeded);
        Assert.Equal("PAY", result.ProjectKey);
        Assert.Equal(new[] { "new" }, Names(result.Labels));
        Assert.Equal(new[] { "new" }, Names(served));
        Assert.Equal(requestsAfterRefresh, _jira.Requests);
        var row = await RowAsync("PAY");
        Assert.NotNull(row);
        Assert.Equal(Now, row.UpdatedAt);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task A_refresh_uses_only_the_service_account()
    {
        _auth.Connected.Add("alice");
        await _db.SeedAsync(db => db.JiraTokens.Add(new JiraToken { UserName = "alice", AccessToken = "a", AccessTokenSecret = "s" }));
        InJira("PAY", Labels("alpha"));

        var result = await _service.RefreshLabelsAsync("PAY");

        Assert.False(result.Succeeded);
        Assert.Equal(0, _jira.Requests);
        Assert.Null(await RowAsync("PAY"));
    }

    [Fact]
    public async Task Concurrent_first_reads_of_a_key_scan_jira_once()
    {
        await ConnectServiceAccountAsync();
        InJira("PAY", Labels("alpha"));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _jira.Hold = hold;

        var first = _service.GetLabelsAsync("alice", "PAY");
        var second = _service.GetLabelsAsync("bob", "PAY");
        while (Volatile.Read(ref _jira.Requests) == 0)
        {
            await Task.Delay(10);
        }
        hold.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, _jira.Requests);
        Assert.All(results, r => Assert.Equal(new[] { "alpha" }, Names(r)));
    }

    [Fact]
    public async Task A_first_read_during_a_refresh_waits_for_it_instead_of_scanning_again()
    {
        await ConnectServiceAccountAsync();
        InJira("PAY", Labels("alpha"));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _jira.Hold = hold;

        var refresh = _service.RefreshLabelsAsync("PAY");
        while (Volatile.Read(ref _jira.Requests) == 0)
        {
            await Task.Delay(10);
        }
        var read = _service.GetLabelsAsync("alice", "PAY");
        hold.SetResult();

        Assert.True((await refresh).Succeeded);
        Assert.Equal(new[] { "alpha" }, Names(await read));
        Assert.Equal(1, _jira.Requests);
    }

    [Fact]
    public async Task A_cancelled_refresh_records_no_failure()
    {
        await ConnectServiceAccountAsync();
        await SaveRowAsync("PAY", Earlier, "old");
        InJira("PAY", Labels("alpha"));
        _jira.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopping = new CancellationTokenSource();

        var refresh = _service.RefreshLabelsAsync("PAY", stopping.Token);
        while (Volatile.Read(ref _jira.Requests) == 0)
        {
            await Task.Delay(10);
        }
        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        var row = await RowAsync("PAY");
        Assert.NotNull(row);
        Assert.Equal(Earlier, row.LastAttemptAt);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task A_refresh_whose_labels_cannot_be_saved_is_reported_as_failed()
    {
        var name = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using (var seed = new EstimationDbContext(new DbContextOptionsBuilder<EstimationDbContext>().UseInMemoryDatabase(name, root).Options))
        {
            seed.JiraSyncSettings.Add(new JiraSyncSettings());
            seed.JiraTokens.Add(new JiraToken { UserName = ServiceUser, AccessToken = "t", AccessTokenSecret = "s" });
            await seed.SaveChangesAsync();
        }
        _auth.Connected.Add(ServiceUser);
        InJira("PAY", Labels("alpha"));

        var result = await ServiceOver(new ReadOnlyDatabase(name, root)).RefreshLabelsAsync("PAY");

        Assert.False(result.Succeeded);
        Assert.Contains("could not be saved", result.Error);
    }

    [Fact]
    public async Task A_failed_status_lookup_is_not_cached()
    {
        _auth.Connected.Add("alice");
        _jira.StatusResponses.Enqueue(HttpStatusCode.ServiceUnavailable);

        var failed = await _service.GetStatusesAsync("alice", "PAY");
        var retried = await _service.GetStatusesAsync("alice", "PAY");

        Assert.Empty(failed);
        Assert.Equal(new[] { "Done", "Open" }, retried.Select(s => s.Name));
    }
}
