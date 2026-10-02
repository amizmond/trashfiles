using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Estimation.Core.JiraIntegration.Client;
using Microsoft.Extensions.Options;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraIssueServiceRagStatusTests
{
    private const string RagField = "customfield_31400";

    private const string JiraOptions =
        "{\"fields\":{\"customfield_31400\":{\"allowedValues\":["
        + "{\"id\":\"501\",\"value\":\"Green - Everything going to plan\"},"
        + "{\"id\":\"502\",\"value\":\"Amber - Still going to plan but issues\"},"
        + "{\"id\":\"503\",\"value\":\"Red - Issues have affected the plan for this request\"}]}}}";

    private readonly FakeJira _jira = new();
    private readonly JiraIssueService _service;

    public JiraIssueServiceRagStatusTests()
    {
        _service = new JiraIssueService(
            new StubAuth(),
            Options.Create(new JiraSettings { Url = "https://jira.example.test" }),
            new StubHttpClientFactory(_jira));
    }

    private JsonObject SentFields() => (JsonObject)JsonNode.Parse(_jira.Bodies.Single())!["fields"]!;

    private Task UpdateRagStatusAsync(string? ragStatus) =>
        _service.UpdateIssueAsync("anna", "PROJ-1", new JiraUpdateIssueRequest
        {
            RagStatus = ragStatus,
            FieldsToUpdate = [JiraUpdateFields.RagStatus],
        });

    [Fact]
    public void The_search_asks_jira_for_the_rag_status()
    {
        Assert.Contains(RagField, JiraIssueFields.BuildFieldList(new JiraSettings()).Split(','));
    }

    [Fact]
    public async Task An_update_sends_the_jira_option_whose_colour_matches()
    {
        _jira.EditMeta = JiraOptions;

        await UpdateRagStatusAsync("Amber");

        Assert.Equal("502", SentFields()[RagField]!["id"]!.GetValue<string>());
        Assert.Equal("https://jira.example.test/rest/api/2/issue/PROJ-1/editmeta", _jira.Gets.Single());
    }

    [Fact]
    public async Task An_update_falls_back_to_the_option_text_when_jira_does_not_list_the_field()
    {
        _jira.EditMeta = "{\"fields\":{}}";

        await UpdateRagStatusAsync("Red");

        Assert.Equal("Red – Issues have affected the plan for this request", SentFields()[RagField]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_update_falls_back_to_the_option_text_when_the_edit_metadata_cannot_be_read()
    {
        _jira.EditMetaStatus = HttpStatusCode.Forbidden;

        await UpdateRagStatusAsync("Green");

        Assert.Equal("Green – Everything going to plan", SentFields()[RagField]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_update_to_a_colour_jira_does_not_offer_is_refused_before_anything_is_sent()
    {
        _jira.EditMeta = "{\"fields\":{\"customfield_31400\":{\"allowedValues\":[{\"id\":\"501\",\"value\":\"Green - Everything going to plan\"}]}}}";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateRagStatusAsync("Red"));

        Assert.Contains("Green - Everything going to plan", error.Message);
        Assert.Empty(_jira.Bodies);
    }

    [Fact]
    public async Task An_update_naming_no_rag_status_clears_it_without_asking_for_the_options()
    {
        await UpdateRagStatusAsync(null);

        Assert.True(SentFields().ContainsKey(RagField));
        Assert.Null(SentFields()[RagField]);
        Assert.Empty(_jira.Gets);
    }

    [Fact]
    public async Task An_update_that_does_not_name_the_rag_status_leaves_it_alone()
    {
        await _service.UpdateIssueAsync("anna", "PROJ-1", new JiraUpdateIssueRequest
        {
            Summary = "Changed",
            RagStatus = "Red",
            FieldsToUpdate = [JiraUpdateFields.Summary],
        });

        Assert.False(SentFields().ContainsKey(RagField));
        Assert.Empty(_jira.Gets);
    }

    private sealed class FakeJira : HttpMessageHandler
    {
        public string EditMeta { get; set; } = "{\"fields\":{}}";

        public HttpStatusCode EditMetaStatus { get; set; } = HttpStatusCode.OK;

        public List<string> Gets { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                Gets.Add(request.RequestUri!.ToString());
                return new HttpResponseMessage(EditMetaStatus)
                {
                    Content = new StringContent(EditMeta, Encoding.UTF8, "application/json"),
                };
            }

            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
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
