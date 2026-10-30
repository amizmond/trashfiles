using System.Text.Json.Nodes;
using Estimation.Core.JiraIntegration.Models;
using Microsoft.Extensions.Options;

namespace Estimation.Core.JiraIntegration.Client;

public sealed record JiraIssueTypeInfo(string Key, string? IssueType, string? Status);

public interface IJiraIssueTypeLookup
{
    Task<IReadOnlyList<JiraIssueTypeInfo>> SearchAsync(string userName, IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    Task<JiraIssueTypeInfo?> GetAsync(string userName, string key, CancellationToken cancellationToken);
}

public sealed class JiraIssueTypeLookup : IJiraIssueTypeLookup
{
    private const string Fields = "issuetype,status";

    private readonly IJiraAuthService _authService;
    private readonly JiraSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;

    public JiraIssueTypeLookup(IJiraAuthService authService, IOptions<JiraSettings> settings, IHttpClientFactory httpClientFactory)
    {
        _authService = authService;
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<JiraIssueTypeInfo>> SearchAsync(string userName, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var token = await TokenAsync(userName);
        var jql = $"key in ({string.Join(", ", keys.Select(k => "\"" + k.Replace("\"", "\\\"") + "\""))})";
        var baseUrl = $"{_settings.Url.TrimEnd('/')}/rest/api/2/search";
        var results = new List<JiraIssueTypeInfo>();
        var startAt = 0;

        while (true)
        {
            var url = $"{baseUrl}?jql={Uri.EscapeDataString(jql)}&fields={Fields}&maxResults={keys.Count}&startAt={startAt}&validateQuery=false";
            var json = await GetJsonAsync(url, token, cancellationToken);
            if (json is null)
            {
                throw new JiraSearchException(System.Net.HttpStatusCode.NotFound, "Jira search endpoint not found.");
            }

            var page = json["issues"] is JsonArray issues
                ? issues.Where(i => i is not null).Select(i => Parse(i!)).OfType<JiraIssueTypeInfo>().ToList()
                : [];
            results.AddRange(page);

            var total = json["total"]?.GetValue<int>() ?? 0;
            startAt += page.Count;
            if (page.Count == 0 || startAt >= total)
            {
                return results;
            }
        }
    }

    public async Task<JiraIssueTypeInfo?> GetAsync(string userName, string key, CancellationToken cancellationToken)
    {
        var token = await TokenAsync(userName);
        var url = $"{_settings.Url.TrimEnd('/')}/rest/api/2/issue/{Uri.EscapeDataString(key)}?fields={Fields}";
        var json = await GetJsonAsync(url, token, cancellationToken);
        return json is null ? null : Parse(json);
    }

    private async Task<JiraToken> TokenAsync(string userName) =>
        await _authService.GetStoredTokenAsync(userName)
        ?? throw new InvalidOperationException("Not authenticated to Jira. Please log in first.");

    private async Task<JsonNode?> GetJsonAsync(string url, JiraToken token, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(JiraIssueService.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, token.AccessToken));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new JiraSearchException(response.StatusCode, body);
        }

        return JsonNode.Parse(body);
    }

    private static JiraIssueTypeInfo? Parse(JsonNode issue)
    {
        var key = issue["key"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var fields = issue["fields"];
        return new JiraIssueTypeInfo(
            key.Trim(),
            fields?["issuetype"]?["name"]?.GetValue<string>(),
            fields?["status"]?["name"]?.GetValue<string>());
    }
}
