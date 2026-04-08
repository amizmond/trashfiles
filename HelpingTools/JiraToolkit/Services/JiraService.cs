using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JiraToolkit.Models;

namespace JiraToolkit.Services;

public class JiraService
{
    private readonly ILogger<JiraService> _logger;

    public JiraService(ILogger<JiraService> logger)
    {
        _logger = logger;
    }

    private static HttpClient CreateClient(JiraCredentials credentials)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        var client = new HttpClient(handler);
        client.BaseAddress = new Uri(credentials.BaseUrl.TrimEnd('/') + "/");

        var authBytes = Encoding.ASCII.GetBytes($"{credentials.Username}:{credentials.Password}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    public async Task<List<JiraFieldRow>> GetIssueFieldsAsync(JiraCredentials credentials, string issueKey)
    {
        using var client = CreateClient(credentials);
        var response = await client.GetAsync($"rest/api/2/issue/{issueKey}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var fieldNames = await GetFieldNamesAsync(credentials);
        var rows = new List<JiraFieldRow>();

        if (doc.RootElement.TryGetProperty("key", out var keyEl))
        {
            rows.Add(new JiraFieldRow { Field = "Key", FieldId = "key", Value = keyEl.GetString() ?? "" });
        }

        if (doc.RootElement.TryGetProperty("id", out var idEl))
        {
            rows.Add(new JiraFieldRow { Field = "Id", FieldId = "id", Value = idEl.GetString() ?? "" });
        }

        if (doc.RootElement.TryGetProperty("self", out var selfEl))
        {
            rows.Add(new JiraFieldRow { Field = "Self", FieldId = "self", Value = selfEl.GetString() ?? "" });
        }

        if (doc.RootElement.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateObject())
            {
                var fieldId = field.Name;
                var isCustom = fieldId.StartsWith("customfield_");
                var displayName = fieldNames.TryGetValue(fieldId, out var name) ? name : fieldId;
                var value = FormatJsonValue(field.Value);

                rows.Add(new JiraFieldRow
                {
                    Field = displayName,
                    FieldId = fieldId,
                    Value = value,
                    IsCustomField = isCustom
                });
            }
        }

        return rows.OrderBy(r => r.Field).ToList();
    }

    private async Task<Dictionary<string, string>> GetFieldNamesAsync(JiraCredentials credentials)
    {
        using var client = CreateClient(credentials);
        try
        {
            var response = await client.GetAsync("rest/api/2/field");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var fields = JsonDocument.Parse(json);
            var dict = new Dictionary<string, string>();

            foreach (var field in fields.RootElement.EnumerateArray())
            {
                var id = field.GetProperty("id").GetString();
                var name = field.GetProperty("name").GetString();
                if (id != null && name != null)
                    dict[id] = name;
            }

            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch field names from Jira");
            return new Dictionary<string, string>();
        }
    }

    public async Task<List<JiraProject>> GetProjectsAsync(JiraCredentials credentials)
    {
        using var client = CreateClient(credentials);
        var response = await client.GetAsync("rest/api/2/project");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var projects = new List<JiraProject>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            projects.Add(new JiraProject
            {
                Key = item.GetProperty("key").GetString() ?? "",
                Name = item.GetProperty("name").GetString() ?? "",
                Id = item.GetProperty("id").GetString() ?? ""
            });
        }

        return projects.OrderBy(p => p.Name).ToList();
    }

    public async Task<List<JiraIssueType>> GetIssueTypesAsync(JiraCredentials credentials)
    {
        using var client = CreateClient(credentials);
        var response = await client.GetAsync("rest/api/2/issuetype");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var types = new List<JiraIssueType>();

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            types.Add(new JiraIssueType
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Name = item.GetProperty("name").GetString() ?? "",
                Description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Subtask = item.TryGetProperty("subtask", out var sub) && sub.GetBoolean()
            });
        }

        return types.OrderBy(t => t.Name).ToList();
    }

    public async Task<List<JiraLabel>> GetLabelsAsync(JiraCredentials credentials)
    {
        using var client = CreateClient(credentials);

        // Jira Server uses the labels REST resource (paginated via jql or direct endpoint)
        var response = await client.GetAsync("rest/api/2/label?maxResults=1000");
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("values", out var values))
            {
                return values.EnumerateArray()
                    .Select(v => new JiraLabel { Name = v.GetString() ?? "" })
                    .OrderBy(l => l.Name)
                    .ToList();
            }
        }

        // Fallback: try jql-based approach to get labels from existing issues
        var jqlResponse = await client.GetAsync("rest/api/2/search?jql=labels+is+not+EMPTY&fields=labels&maxResults=100");
        if (jqlResponse.IsSuccessStatusCode)
        {
            var json = await jqlResponse.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var labelsSet = new HashSet<string>();

            if (doc.RootElement.TryGetProperty("issues", out var issues))
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    if (issue.TryGetProperty("fields", out var fields) &&
                        fields.TryGetProperty("labels", out var labels))
                    {
                        foreach (var label in labels.EnumerateArray())
                        {
                            var val = label.GetString();
                            if (!string.IsNullOrEmpty(val))
                                labelsSet.Add(val);
                        }
                    }
                }
            }

            return labelsSet.Select(l => new JiraLabel { Name = l }).OrderBy(l => l.Name).ToList();
        }

        return new List<JiraLabel>();
    }

    public async Task<List<JiraFieldRow>> GetCustomFieldOptionsAsync(JiraCredentials credentials, string customFieldId)
    {
        using var client = CreateClient(credentials);

        // Try to get custom field context and options
        // For Jira Server, we query issues that have this field populated
        var response = await client.GetAsync(
            $"rest/api/2/search?jql=%22{customFieldId}%22+is+not+EMPTY&fields={customFieldId}&maxResults=100");

        if (!response.IsSuccessStatusCode)
            return new List<JiraFieldRow>();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var rows = new List<JiraFieldRow>();
        var seen = new HashSet<string>();

        if (doc.RootElement.TryGetProperty("issues", out var issues))
        {
            foreach (var issue in issues.EnumerateArray())
            {
                if (issue.TryGetProperty("fields", out var fields) &&
                    fields.TryGetProperty(customFieldId, out var fieldValue))
                {
                    var value = FormatJsonValue(fieldValue);
                    if (!string.IsNullOrEmpty(value) && seen.Add(value))
                    {
                        rows.Add(new JiraFieldRow
                        {
                            Field = customFieldId,
                            FieldId = customFieldId,
                            Value = value,
                            IsCustomField = true
                        });
                    }
                }
            }
        }

        return rows.OrderBy(r => r.Value).ToList();
    }

    private static string FormatJsonValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return string.Empty;

            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;

            case JsonValueKind.Number:
                return element.GetRawText();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean().ToString();

            case JsonValueKind.Object:
                // Try to extract common Jira object patterns
                if (element.TryGetProperty("name", out var nameEl))
                    return nameEl.GetString() ?? element.GetRawText();
                if (element.TryGetProperty("displayName", out var displayEl))
                    return displayEl.GetString() ?? element.GetRawText();
                if (element.TryGetProperty("value", out var valueEl))
                    return valueEl.GetString() ?? element.GetRawText();
                if (element.TryGetProperty("key", out var keyEl))
                    return keyEl.GetString() ?? element.GetRawText();
                return element.GetRawText();

            case JsonValueKind.Array:
                var items = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    items.Add(FormatJsonValue(item));
                }
                return items.Count > 0 ? string.Join(", ", items) : string.Empty;

            default:
                return element.GetRawText();
        }
    }
}
