using System.Net;
using System.Text;
using System.Text.Json.Nodes;
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

    [Fact]
    public async Task Creating_a_link_posts_the_type_and_both_issues()
    {
        _jira.Status = HttpStatusCode.Created;

        await _service.CreateIssueLinkAsync("anna", "Blocks", "PAY-1", "PAY-2");

        Assert.Equal("POST https://jira.example.test/rest/api/2/issueLink", _jira.Sent.Single());
        var body = JsonNode.Parse(_jira.Bodies.Single()!)!;
        Assert.Equal("Blocks", body["type"]!["name"]!.GetValue<string>());
        Assert.Equal("PAY-1", body["inwardIssue"]!["key"]!.GetValue<string>());
        Assert.Equal("PAY-2", body["outwardIssue"]!["key"]!.GetValue<string>());
    }

    [Fact]
    public async Task Deleting_a_link_sends_its_id_and_no_body()
    {
        _jira.Status = HttpStatusCode.NoContent;

        await _service.DeleteIssueLinkAsync("anna", "501");

        Assert.Equal("DELETE https://jira.example.test/rest/api/2/issueLink/501", _jira.Sent.Single());
        Assert.Null(_jira.Bodies.Single());
    }

    [Fact]
    public async Task Link_calls_ask_for_a_single_attempt()
    {
        _jira.Status = HttpStatusCode.NoContent;

        await _service.CreateIssueLinkAsync("anna", "Blocks", "PAY-1", "PAY-2");
        await _service.DeleteIssueLinkAsync("anna", "501");

        Assert.Equal(new[] { true, true }, _jira.SingleAttempt);
    }

    [Fact]
    public async Task A_refused_link_call_throws_with_the_status_and_jiras_reason()
    {
        _jira.Status = HttpStatusCode.Unauthorized;
        _jira.Response = "{\"errorMessages\":[\"No Link Issue Permission for issue 'PAY-1'\"],\"errors\":{}}";

        var refused = await Assert.ThrowsAsync<JiraRequestException>(
            () => _service.CreateIssueLinkAsync("anna", "Blocks", "PAY-1", "PAY-2"));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.False(refused.IsTransient);
        Assert.Equal("No Link Issue Permission for issue 'PAY-1'", refused.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData((HttpStatusCode)429, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task A_refusal_says_whether_jira_itself_is_in_trouble(HttpStatusCode status, bool transient)
    {
        _jira.Status = status;
        _jira.Response = "<html>not json</html>";

        var refused = await Assert.ThrowsAsync<JiraRequestException>(() => _service.DeleteIssueLinkAsync("anna", "501"));

        Assert.Equal(transient, refused.IsTransient);
        Assert.Equal($"Jira answered {(int)status} {status}", refused.Reason);
    }

    [Fact]
    public void Field_errors_of_jira_are_part_of_the_reason()
    {
        var refused = new JiraRequestException("POST", HttpStatusCode.BadRequest,
            "{\"errorMessages\":[],\"errors\":{\"issuelinks\":\"Link type not found\"}}");

        Assert.Equal("Link type not found", refused.Reason);
    }

    private sealed class RecordingJira : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> Sent { get; } = [];
        public List<string?> Bodies { get; } = [];
        public List<bool> SingleAttempt { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Response { get; set; } = "{\"total\":0,\"issues\":[]}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            Sent.Add($"{request.Method} {request.RequestUri}");
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            SingleAttempt.Add(request.Options.TryGetValue(JiraResilienceHandler.SingleAttemptKey, out var single) && single);
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json"),
            };
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
