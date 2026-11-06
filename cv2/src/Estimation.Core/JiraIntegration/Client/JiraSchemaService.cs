using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;

namespace Estimation.Core.JiraIntegration.Client;

public sealed class JiraField
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? SchemaType { get; set; }

    public string? SchemaCustom { get; set; }
}

public sealed class JiraSchema
{
    public const string SprintFieldSchema = "com.pyxis.greenhopper.jira:gh-sprint";

    public static JiraSchema Empty { get; } = new([], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public JiraSchema(IReadOnlyList<JiraField> fields, IReadOnlyDictionary<string, string> statusCategories)
    {
        Fields = fields;
        StatusCategories = statusCategories;
    }

    public IReadOnlyList<JiraField> Fields { get; }

    public IReadOnlyDictionary<string, string> StatusCategories { get; }

    public bool IsEmpty => Fields.Count == 0 && StatusCategories.Count == 0;

    public string? SprintFieldId =>
        Fields.FirstOrDefault(f => string.Equals(f.SchemaCustom, SprintFieldSchema, StringComparison.OrdinalIgnoreCase))?.Id;

    public string? FieldName(string? fieldId) =>
        string.IsNullOrEmpty(fieldId)
            ? null
            : Fields.FirstOrDefault(f => string.Equals(f.Id, fieldId, StringComparison.OrdinalIgnoreCase))?.Name;
}

public interface IJiraSchemaService
{
    Task<JiraSchema> GetAsync(string userName, CancellationToken cancellationToken = default);
}

public class JiraSchemaService : IJiraSchemaService
{
    private const string CacheKey = "jira_schema";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(1);

    private readonly IJiraAuthService _authService;
    private readonly JiraSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;

    public JiraSchemaService(
        IJiraAuthService authService,
        IOptions<JiraSettings> settings,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory)
    {
        _authService = authService;
        _settings = settings.Value;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<JiraSchema> GetAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out JiraSchema? cached) && cached is not null)
        {
            return cached;
        }

        var token = await _authService.GetStoredTokenAsync(userName)
                    ?? throw new InvalidOperationException("Not authenticated to Jira.");

        var fields = ParseFields(await GetAsync("/rest/api/2/field", token.AccessToken, cancellationToken));
        var statuses = ParseStatuses(await GetAsync("/rest/api/2/status", token.AccessToken, cancellationToken));
        var schema = new JiraSchema(fields, statuses);
        if (!schema.IsEmpty)
        {
            _cache.Set(CacheKey, schema, CacheDuration);
        }
        return schema;
    }

    private async Task<string?> GetAsync(string path, string accessToken, CancellationToken cancellationToken)
    {
        var url = $"{_settings.Url.TrimEnd('/')}{path}";
        var httpClient = _httpClientFactory.CreateClient(JiraIssueService.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(JiraOAuthSigningHandler.SignerKey,
            () => _authService.BuildOAuthHeader("GET", url, accessToken));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Jira returned {StatusCode} for {Path}", (int)response.StatusCode, path);
            return null;
        }
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static List<JiraField> ParseFields(string? json)
    {
        var fields = new List<JiraField>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return fields;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return fields;
        }

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var id = StringProperty(element, "id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            string? schemaType = null;
            string? schemaCustom = null;
            if (element.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.Object)
            {
                schemaType = StringProperty(schema, "type");
                schemaCustom = StringProperty(schema, "custom");
            }

            fields.Add(new JiraField
            {
                Id = id,
                Name = StringProperty(element, "name") ?? id,
                SchemaType = schemaType,
                SchemaCustom = schemaCustom,
            });
        }
        return fields;
    }

    private static Dictionary<string, string> ParseStatuses(string? json)
    {
        var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return categories;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return categories;
        }

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var name = StringProperty(element, "name")?.Trim();
            if (string.IsNullOrEmpty(name)
                || !element.TryGetProperty("statusCategory", out var category)
                || category.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var key = StringProperty(category, "key");
            if (!string.IsNullOrEmpty(key))
            {
                categories[name] = key;
            }
        }
        return categories;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
