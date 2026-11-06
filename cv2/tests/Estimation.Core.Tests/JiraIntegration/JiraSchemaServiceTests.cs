using System.Net;
using System.Text;
using Estimation.Core.JiraIntegration.Client;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraSchemaServiceTests
{
    private const string Fields = """
        [
          {"id":"summary","name":"Summary","schema":{"type":"string","system":"summary"}},
          {"id":"customfield_10004","name":"Sprint","schema":{"type":"array","items":"string","custom":"com.pyxis.greenhopper.jira:gh-sprint","customId":10004}},
          {"id":"customfield_10003","name":"Story Points","schema":{"type":"number","custom":"com.atlassian.jira.plugin.system.customfieldtypes:float","customId":10003}},
          {"name":"No id"}
        ]
        """;

    private const string Statuses = """
        [
          {"id":"1","name":"Open","statusCategory":{"id":2,"key":"new","name":"To Do"}},
          {"id":"3","name":"In Progress","statusCategory":{"key":"indeterminate"}},
          {"id":"6","name":" Closed ","statusCategory":{"key":"done"}},
          {"id":"7","name":"No category"}
        ]
        """;

    private readonly FakeJira _jira = new();
    private readonly JiraSchemaService _service;

    public JiraSchemaServiceTests()
    {
        _service = new JiraSchemaService(
            new StubAuth(),
            Options.Create(new JiraSettings { Url = "https://jira.example.test/" }),
            new MemoryCache(new MemoryCacheOptions()),
            new StubHttpClientFactory(_jira));
    }

    [Fact]
    public async Task Fields_and_status_categories_are_read_and_the_sprint_field_is_found_by_its_schema()
    {
        _jira.Bodies["/rest/api/2/field"] = Fields;
        _jira.Bodies["/rest/api/2/status"] = Statuses;

        var schema = await _service.GetAsync("alice");

        Assert.Equal(["/rest/api/2/field", "/rest/api/2/status"], _jira.Paths);
        Assert.Equal("customfield_10004", schema.SprintFieldId);
        Assert.Equal("Story Points", schema.FieldName("customfield_10003"));
        Assert.Null(schema.FieldName("customfield_99999"));
        Assert.Null(schema.FieldName(null));
        Assert.Equal(3, schema.Fields.Count);
        Assert.Equal("done", schema.StatusCategories["closed"]);
        Assert.Equal("new", schema.StatusCategories["Open"]);
        Assert.Equal(3, schema.StatusCategories.Count);
    }

    [Fact]
    public async Task A_second_read_is_served_from_the_cache()
    {
        _jira.Bodies["/rest/api/2/field"] = Fields;
        _jira.Bodies["/rest/api/2/status"] = Statuses;

        await _service.GetAsync("alice");
        await _service.GetAsync("bob");

        Assert.Equal(2, _jira.Paths.Count);
    }

    [Fact]
    public async Task Missing_endpoints_give_an_empty_schema_that_is_not_cached()
    {
        var first = await _service.GetAsync("alice");
        Assert.True(first.IsEmpty);
        Assert.Null(first.SprintFieldId);

        _jira.Bodies["/rest/api/2/status"] = Statuses;
        var second = await _service.GetAsync("alice");

        Assert.False(second.IsEmpty);
        Assert.Equal(4, _jira.Paths.Count);
    }

    private sealed class FakeJira : HttpMessageHandler
    {
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);

        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            return Task.FromResult(Bodies.TryGetValue(path, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
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
            Task.FromResult<JiraToken?>(new JiraToken { UserName = userName, AccessToken = "t", AccessTokenSecret = "s" });

        public string BuildOAuthHeader(string httpMethod, string url, string accessToken) => $"OAuth {accessToken}";

        public Task<JiraRequestToken> GetRequestTokenAsync() => throw new NotSupportedException();
        public Task<(string AccessToken, string AccessTokenSecret)> ExchangeAccessTokenAsync(string requestToken, string requestTokenSecret, string verifier) => throw new NotSupportedException();
        public Task<bool> IsAuthenticatedAsync(string userName) => throw new NotSupportedException();
        public Task SaveTokenAsync(string userName, string accessToken, string accessTokenSecret) => throw new NotSupportedException();
        public Task LogoutAsync(string userName) => throw new NotSupportedException();
    }
}
