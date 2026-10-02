using System.Net;
using System.Text.Json;

namespace Estimation.Core.JiraIntegration.Client;

public class JiraRequestException : Exception
{
    private const int MaxReasonLength = 200;

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    public JiraRequestException(string method, HttpStatusCode statusCode, string responseBody)
        : base($"Jira {method} failed ({statusCode}): {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public bool IsTransient =>
        (int)StatusCode >= 500 || StatusCode == HttpStatusCode.RequestTimeout || StatusCode == (HttpStatusCode)429;

    public string Reason
    {
        get
        {
            var reason = JiraMessages(ResponseBody) ?? $"Jira answered {(int)StatusCode} {StatusCode}";
            return reason.Length > MaxReasonLength ? reason[..MaxReasonLength] + "…" : reason;
        }
    }

    private static string? JiraMessages(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var messages = new List<string>();
            if (doc.RootElement.TryGetProperty("errorMessages", out var errorMessages)
                && errorMessages.ValueKind == JsonValueKind.Array)
            {
                messages.AddRange(errorMessages.EnumerateArray()
                    .Where(m => m.ValueKind == JsonValueKind.String)
                    .Select(m => m.GetString()!));
            }
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                messages.AddRange(errors.EnumerateObject()
                    .Where(e => e.Value.ValueKind == JsonValueKind.String)
                    .Select(e => e.Value.GetString()!));
            }

            var text = string.Join(" ", messages.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()));
            return text.Length == 0 ? null : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
