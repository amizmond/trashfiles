using System.Net;
using System.Text;
using System.Web;
using Estimation.Core.JiraIntegration.Client;
using Microsoft.Extensions.Options;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraIssueServiceIssueLinksTests
{
    private readonly RecordingJira _jira = new();
    private readonly JiraIssueService _service;

    public JiraIssueServiceIssueLinksTests()
    {
        _service = new JiraIssueService(
            new StubAuth(),
            Options.Create(new JiraSettings { Url = "https://jira.example.test" }),
            new StubHttpClientFactory(_jira));
    }

    private string[] RequestedFields() =>
        HttpUtility.ParseQueryString(_jira.Requests.Single().Query)["fields"]!.Split(',');

    [Fact]
    public async Task A_search_asking_for_links_requests_the_linked_issues_field()
    {
        await _service.SearchIssuesAsync("anna", "project = PAY", includeLinks: true);

        Assert.Contains(JiraIssueFields.IssueLinks, RequestedFields());
    }

    [Fact]
    public async Task A_plain_search_leaves_the_linked_issues_field_out()
    {
        await _service.SearchIssuesAsync("anna", "project = PAY");

        Assert.DoesNotContain(JiraIssueFields.IssueLinks, RequestedFields());
    }

    [Fact]
    public async Task A_paged_search_leaves_the_linked_issues_field_out()
    {
        await _service.SearchIssuesPageAsync("anna", "project = PAY", 10);

        Assert.DoesNotContain(JiraIssueFields.IssueLinks, RequestedFields());
    }

    [Fact]
    public async Task Fetching_issues_by_key_leaves_the_linked_issues_field_out()
    {
        await _service.GetIssuesByKeysAsync("anna", ["PAY-1", "PAY-2"]);

        Assert.DoesNotContain(JiraIssueFields.IssueLinks, RequestedFields());
    }

    private sealed class RecordingJira : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"total\":0,\"issues\":[]}", Encoding.UTF8, "application/json"),
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
