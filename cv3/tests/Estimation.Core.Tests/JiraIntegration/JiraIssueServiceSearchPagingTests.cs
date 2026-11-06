using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using Estimation.Core.JiraIntegration.Client;
using Microsoft.Extensions.Options;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraIssueServiceSearchPagingTests
{
    private static JiraIssueService Service(HttpMessageHandler handler) =>
        new(new StubAuth(), Options.Create(new JiraSettings { Url = "https://jira.example.test" }), new StubHttpClientFactory(handler));

    [Fact]
    public async Task A_search_keeps_paging_by_the_number_of_issues_jira_actually_sent()
    {
        var jira = new PagingJira(total: 45, pageSize: 20);

        var issues = await Service(jira).SearchIssuesAsync("anna", "sprint = \"ARG 26.09.4\"", JiraSearchOptions.History("customfield_10004"));

        Assert.Equal(45, issues.Count);
        Assert.Equal(45, issues.Select(i => i.Key).Distinct().Count());
        Assert.Equal([0, 20, 40], jira.StartAts);
        Assert.All(jira.Requests, r => Assert.Contains("expand=changelog", r.Query));
    }

    [Fact]
    public async Task A_search_stops_when_jira_sends_an_empty_page_before_the_total()
    {
        var jira = new PagingJira(total: 30, pageSize: 20) { EmptyFrom = 20 };

        var issues = await Service(jira).SearchIssuesAsync("anna", "project = PAY");

        Assert.Equal(20, issues.Count);
        Assert.Equal([0, 20], jira.StartAts);
    }

    private sealed class PagingJira : HttpMessageHandler
    {
        private readonly int _total;
        private readonly int _pageSize;

        public PagingJira(int total, int pageSize)
        {
            _total = total;
            _pageSize = pageSize;
        }

        public int? EmptyFrom { get; init; }

        public List<Uri> Requests { get; } = [];

        public List<int> StartAts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var startAt = int.Parse(HttpUtility.ParseQueryString(request.RequestUri!.Query)["startAt"]!);
            StartAts.Add(startAt);

            var keys = EmptyFrom is { } empty && startAt >= empty
                ? []
                : Enumerable.Range(startAt, Math.Max(0, Math.Min(_pageSize, _total - startAt))).Select(i => $"PAY-{i + 1}");
            var body = JsonSerializer.Serialize(new
            {
                startAt,
                maxResults = _pageSize,
                total = _total,
                issues = keys.Select(k => new { key = k, fields = new { summary = k } }),
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
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

    private sealed class StubAuth : IJiraAuthService
    {
        public bool IsConfigured => true;

        public Task<JiraToken?> GetStoredTokenAsync(string userName) =>
            Task.FromResult<JiraToken?>(new JiraToken { UserName = userName, AccessToken = "token", AccessTokenSecret = "s" });

        public string BuildOAuthHeader(string httpMethod, string url, string accessToken) => $"OAuth {accessToken}";

        public Task<JiraRequestToken> GetRequestTokenAsync() => throw new NotSupportedException();
        public Task<(string AccessToken, string AccessTokenSecret)> ExchangeAccessTokenAsync(string requestToken, string requestTokenSecret, string verifier) => throw new NotSupportedException();
        public Task<bool> IsAuthenticatedAsync(string userName) => throw new NotSupportedException();
        public Task SaveTokenAsync(string userName, string accessToken, string accessTokenSecret) => throw new NotSupportedException();
        public Task LogoutAsync(string userName) => throw new NotSupportedException();
    }
}
