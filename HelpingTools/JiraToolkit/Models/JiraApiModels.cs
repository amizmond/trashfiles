namespace JiraToolkit.Models;

public enum JiraApiParamKind
{
    /// <summary>Substituted into the URL template, e.g. {boardId}.</summary>
    Path,
    /// <summary>Appended to the query string when a value is supplied.</summary>
    Query
}

public enum JiraApiParamType
{
    Text,
    Number,
    Boolean,
    Select
}

public class JiraApiParameter
{
    public string Name { get; init; } = string.Empty;
    public JiraApiParamKind Kind { get; init; } = JiraApiParamKind.Query;
    public JiraApiParamType Type { get; init; } = JiraApiParamType.Text;
    public bool Required { get; init; }
    public string Default { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Options { get; init; } = Array.Empty<string>();
}

public class JiraApiDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Method { get; init; } = "GET";

    /// <summary>Relative path with {placeholders}, e.g. rest/agile/1.0/board/{boardId}/backlog.</summary>
    public string PathTemplate { get; init; } = string.Empty;

    /// <summary>Optional caveat shown in the section header (deployment differences, deprecations, ...).</summary>
    public string? Note { get; init; }

    public IReadOnlyList<JiraApiParameter> Parameters { get; init; } = Array.Empty<JiraApiParameter>();

    public IEnumerable<JiraApiParameter> PathParameters => Parameters.Where(p => p.Kind == JiraApiParamKind.Path);
    public IEnumerable<JiraApiParameter> QueryParameters => Parameters.Where(p => p.Kind == JiraApiParamKind.Query);

    public Dictionary<string, string> CreateDefaultValues() =>
        Parameters.ToDictionary(p => p.Name, p => p.Default, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the relative URL passed to HttpClient. Unfilled path placeholders are left verbatim so the
    /// preview stays readable; execution is blocked separately by <see cref="GetMissingRequired"/>.
    /// </summary>
    public string BuildRelativeUrl(IReadOnlyDictionary<string, string> values)
    {
        var path = PathTemplate;

        foreach (var p in PathParameters)
        {
            values.TryGetValue(p.Name, out var raw);
            var value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
                continue;
            path = path.Replace("{" + p.Name + "}", Uri.EscapeDataString(value));
        }

        var query = new List<string>();
        foreach (var p in QueryParameters)
        {
            if (!values.TryGetValue(p.Name, out var raw))
                continue;
            var value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
                continue;
            query.Add($"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(value)}");
        }

        return query.Count == 0 ? path : $"{path}?{string.Join("&", query)}";
    }

    public IReadOnlyList<string> GetMissingRequired(IReadOnlyDictionary<string, string> values) =>
        Parameters
            .Where(p => p.Required && (!values.TryGetValue(p.Name, out var v) || string.IsNullOrWhiteSpace(v)))
            .Select(p => p.Name)
            .ToList();
}

public class JiraApiResponse
{
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ReasonPhrase { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Body { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }
}
