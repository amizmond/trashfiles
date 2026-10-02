using System.Net;
using System.Text;
using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraMetadataServiceLinkTypesTests
{
    private const string ServiceUser = JiraSyncSettings.DefaultServiceAccountUserName;

    private const string LinkTypes = """
        {"issueLinkTypes":[
          {"id":"10000","name":"Blocks","inward":"is blocked by","outward":"blocks"},
          {"id":"10003","name":"Relates","inward":"relates to","outward":"relates to"},
          {"id":"10009","inward":"x","outward":"y"}
        ]}
        """;

    private readonly InMemoryDatabase _db = new();
    private readonly FakeJira _jira = new();
    private readonly StubAuth _auth = new();
    private readonly JiraMetadataService _service;

    public JiraMetadataServiceLinkTypesTests()
    {
        _service = new JiraMetadataService(
            _auth,
            Options.Create(new JiraSettings { Url = "https://jira.example.test" }),
            new MemoryCache(new MemoryCacheOptions()),
            new StubHttpClientFactory(_jira),
            _db,
            TimeProvider.System);
    }

    [Fact]
    public async Task The_link_types_are_read_with_both_phrases()
    {
        _auth.Connected.Add("alice");
        _jira.Body = LinkTypes;

        var types = await _service.GetIssueLinkTypesAsync("alice");

        Assert.Equal("/rest/api/2/issueLinkType", Assert.Single(_jira.Paths));
        Assert.Equal(new[] { "Blocks", "Relates" }, types.Select(t => t.Name));
        Assert.Equal("blocks", types[0].Outward);
        Assert.Equal("is blocked by", types[0].Inward);
        Assert.Equal("10000", types[0].Id);
    }

    [Fact]
    public async Task The_link_types_are_read_with_the_users_own_token_even_when_the_service_account_is_connected()
    {
        _auth.Connected.Add(ServiceUser);
        _auth.Connected.Add("alice");
        await _db.SeedAsync(db =>
        {
            db.JiraSyncSettings.Add(new JiraSyncSettings());
            db.JiraTokens.Add(new JiraToken { UserName = ServiceUser, AccessToken = "t", AccessTokenSecret = "s" });
        });
        _jira.Body = LinkTypes;

        await _service.GetIssueLinkTypesAsync("alice");

        Assert.Equal(new[] { "alice" }, _auth.TokenRequests);
    }

    [Fact]
    public async Task An_empty_answer_is_not_cached()
    {
        _auth.Connected.Add("alice");
        _jira.Body = "{\"issueLinkTypes\":[]}";

        Assert.Empty(await _service.GetIssueLinkTypesAsync("alice"));

        _jira.Body = LinkTypes;
        Assert.Equal(2, (await _service.GetIssueLinkTypesAsync("alice")).Count);
    }

    [Fact]
    public async Task A_second_read_is_served_from_the_cache()
    {
        _auth.Connected.Add("alice");
        _jira.Body = LinkTypes;

        await _service.GetIssueLinkTypesAsync("alice");
        var again = await _service.GetIssueLinkTypesAsync("alice");

        Assert.Equal(2, again.Count);
        Assert.Single(_jira.Paths);
    }

    [Fact]
    public async Task A_failed_read_throws_and_is_not_cached()
    {
        _auth.Connected.Add("alice");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetIssueLinkTypesAsync("alice"));

        _jira.Body = LinkTypes;
        Assert.Equal(2, (await _service.GetIssueLinkTypesAsync("alice")).Count);
    }

    [Fact]
    public async Task Without_any_token_the_read_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.GetIssueLinkTypesAsync("alice"));
        Assert.Empty(_jira.Paths);
    }

    private sealed class FakeJira : HttpMessageHandler
    {
        public string? Body { get; set; }
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(Body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
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
}
