using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JiraToolkit.Models;

namespace JiraToolkit.Services;

public class JiraService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JiraService> _logger;

    public JiraService(IHttpClientFactory httpClientFactory, ILogger<JiraService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private HttpClient CreateClient(JiraCredentials credentials)
    {
        var client = _httpClientFactory.CreateClient("JiraClient");
        client.BaseAddress = new Uri(credentials.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(30);

        var authBytes = Encoding.ASCII.GetBytes($"{credentials.Username}:{credentials.Password}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var message = $"Jira API error {(int)response.StatusCode} {response.ReasonPhrase}";

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("errorMessages", out var msgs))
                {
                    var errors = string.Join("; ", msgs.EnumerateArray()
                        .Select(m => m.GetString())
                        .Where(m => !string.IsNullOrEmpty(m)));
                    if (!string.IsNullOrEmpty(errors))
                        message += $": {errors}";
                }
                else if (doc.RootElement.TryGetProperty("message", out var msg))
                {
                    message += $": {msg.GetString()}";
                }
            }
            catch
            {
                message += $": {(body.Length > 200 ? body[..200] + "..." : body)}";
            }
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    public async Task<List<JiraFieldRow>> GetIssueFieldsAsync(JiraCredentials credentials, string issueKey, CancellationToken ct = default)
    {
        using var client = CreateClient(credentials);
        var response = await client.GetAsync($"rest/api/2/issue/{issueKey}", ct);
        await EnsureSuccessAsync(response, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var fieldNames = await GetFieldNamesAsync(credentials, ct);
        var rows = new List<JiraFieldRow>();

        if (doc.RootElement.TryGetProperty("key", out var keyEl))
            rows.Add(new JiraFieldRow { Field = "Key", FieldId = "key", Value = keyEl.GetString() ?? "" });

        if (doc.RootElement.TryGetProperty("id", out var idEl))
            rows.Add(new JiraFieldRow { Field = "Id", FieldId = "id", Value = idEl.GetString() ?? "" });

        if (doc.RootElement.TryGetProperty("self", out var selfEl))
            rows.Add(new JiraFieldRow { Field = "Self", FieldId = "self", Value = selfEl.GetString() ?? "" });

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

    private async Task<Dictionary<string, string>> GetFieldNamesAsync(JiraCredentials credentials, CancellationToken ct = default)
    {
        using var client = CreateClient(credentials);
        try
        {
            var response = await client.GetAsync("rest/api/2/field", ct);
            await EnsureSuccessAsync(response, ct);

            var json = await response.Content.ReadAsStringAsync(ct);
            using var fields = JsonDocument.Parse(json);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to fetch field names from Jira");
            return new Dictionary<string, string>();
        }
    }

    public async Task<List<JiraProject>> GetProjectsAsync(JiraCredentials credentials, CancellationToken ct = default)
    {
        using var client = CreateClient(credentials);
        var response = await client.GetAsync("rest/api/2/project", ct);
        await EnsureSuccessAsync(response, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
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

    public async Task<List<JiraIssueType>> GetIssueTypesAsync(JiraCredentials credentials, CancellationToken ct = default)
    {
        using var client = CreateClient(credentials);
        var response = await client.GetAsync("rest/api/2/issuetype", ct);
        await EnsureSuccessAsync(response, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
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

    public async Task<List<JiraLabel>> GetLabelsAsync(JiraCredentials credentials, string? projectKey = null, CancellationToken ct = default)
    {
        using var client = CreateClient(credentials);

        // When a project is specified, go straight to JQL (the /rest/api/2/label endpoint is global)
        if (!string.IsNullOrWhiteSpace(projectKey))
            return await GetLabelsByJql(client, $"project = \"{projectKey}\" AND labels is not EMPTY", ct);

        // Primary: paginate through /rest/api/2/label
        var allLabels = new List<string>();
        var startAt = 0;
        const int pageSize = 1000;

        var firstResponse = await client.GetAsync($"rest/api/2/label?maxResults={pageSize}&startAt=0", ct);
        if (firstResponse.IsSuccessStatusCode)
        {
            var json = await firstResponse.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("values", out var values))
            {
                foreach (var v in values.EnumerateArray())
                {
                    var name = v.GetString();
                    if (!string.IsNullOrEmpty(name))
                        allLabels.Add(name);
                }

                var total = doc.RootElement.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : allLabels.Count;
                startAt += pageSize;

                while (startAt < total)
                {
                    ct.ThrowIfCancellationRequested();
                    var pageResponse = await client.GetAsync($"rest/api/2/label?maxResults={pageSize}&startAt={startAt}", ct);
                    if (!pageResponse.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Labels pagination failed at startAt={StartAt} with {StatusCode}",
                            startAt, (int)pageResponse.StatusCode);
                        break;
                    }

                    var pageJson = await pageResponse.Content.ReadAsStringAsync(ct);
                    using var pageDoc = JsonDocument.Parse(pageJson);

                    if (pageDoc.RootElement.TryGetProperty("values", out var pageValues))
                    {
                        foreach (var v in pageValues.EnumerateArray())
                        {
                            var name = v.GetString();
                            if (!string.IsNullOrEmpty(name))
                                allLabels.Add(name);
                        }
                    }

                    startAt += pageSize;
                }

                _logger.LogInformation("Loaded {Count} labels from /rest/api/2/label (total reported: {Total})",
                    allLabels.Count, total);

                return allLabels
                    .Select(l => new JiraLabel { Name = l })
                    .OrderBy(l => l.Name)
                    .ToList();
            }
        }
        else
        {
            _logger.LogWarning("Primary labels endpoint returned {StatusCode}, falling back to JQL",
                (int)firstResponse.StatusCode);
        }

        // Fallback: JQL-based approach
        return await GetLabelsByJql(client, "labels is not EMPTY", ct);
    }

    private async Task<List<JiraLabel>> GetLabelsByJql(HttpClient client, string jql, CancellationToken ct)
    {
        var labelsSet = new HashSet<string>();
        var startAt = 0;
        const int pageSize = 100;
        var encodedJql = Uri.EscapeDataString(jql);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await client.GetAsync(
                $"rest/api/2/search?jql={encodedJql}&fields=labels&maxResults={pageSize}&startAt={startAt}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Labels JQL query failed with {StatusCode} for JQL: {Jql}",
                    (int)response.StatusCode, jql);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var total = doc.RootElement.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : 0;

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

            startAt += pageSize;
            if (startAt >= total)
                break;
        }

        if (labelsSet.Count == 0)
            _logger.LogWarning("JQL label query returned 0 labels for: {Jql}", jql);

        return labelsSet.Select(l => new JiraLabel { Name = l }).OrderBy(l => l.Name).ToList();
    }

    public async Task<List<JiraFieldRow>> GetCustomFieldOptionsAsync(JiraCredentials credentials, string customFieldId, CancellationToken ct = default)
    {
        using var client = CreateClient(credentials);
        var rows = new List<JiraFieldRow>();
        var seen = new HashSet<string>();
        var startAt = 0;
        const int pageSize = 100;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await client.GetAsync(
                $"rest/api/2/search?jql=%22{customFieldId}%22+is+not+EMPTY&fields={customFieldId}&maxResults={pageSize}&startAt={startAt}", ct);

            if (!response.IsSuccessStatusCode)
            {
                if (startAt == 0)
                    _logger.LogWarning("Custom field {FieldId} query returned {StatusCode}",
                        customFieldId, (int)response.StatusCode);
                break;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var total = doc.RootElement.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : 0;

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

            startAt += pageSize;
            if (startAt >= total)
                break;
        }

        return rows.OrderBy(r => r.Value).ToList();
    }

    public async Task<List<JiraChildIssue>> GetChildIssuesAsync(JiraCredentials credentials, string parentKey, CancellationToken ct = default)
    {
        using var client = CreateClient(credentials);
        var issues = new List<JiraChildIssue>();
        var startAt = 0;
        const int pageSize = 100;
        var encodedJql = Uri.EscapeDataString($"parent = {parentKey}");

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await client.GetAsync(
                $"rest/api/2/search?jql={encodedJql}&fields=summary,status,issuetype,assignee,priority&maxResults={pageSize}&startAt={startAt}", ct);
            await EnsureSuccessAsync(response, ct);

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var total = doc.RootElement.TryGetProperty("total", out var totalEl) ? totalEl.GetInt32() : 0;

            if (doc.RootElement.TryGetProperty("issues", out var issuesArray))
            {
                foreach (var issue in issuesArray.EnumerateArray())
                {
                    var key = issue.GetProperty("key").GetString() ?? "";
                    var fields = issue.GetProperty("fields");

                    issues.Add(new JiraChildIssue
                    {
                        Key = key,
                        Summary = fields.TryGetProperty("summary", out var summary)
                            ? summary.GetString() ?? "" : "",
                        Status = fields.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object
                            ? (status.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "") : "",
                        IssueType = fields.TryGetProperty("issuetype", out var issueType) && issueType.ValueKind == JsonValueKind.Object
                            ? (issueType.TryGetProperty("name", out var itn) ? itn.GetString() ?? "" : "") : "",
                        Assignee = fields.TryGetProperty("assignee", out var assignee) && assignee.ValueKind == JsonValueKind.Object
                            ? (assignee.TryGetProperty("displayName", out var an) ? an.GetString() ?? "" : "") : "Unassigned",
                        Priority = fields.TryGetProperty("priority", out var priority) && priority.ValueKind == JsonValueKind.Object
                            ? (priority.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "") : ""
                    });
                }
            }

            startAt += pageSize;
            if (startAt >= total)
                break;
        }

        return issues;
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
