using System.Text.Json.Nodes;

namespace Estimation.Core.JiraIntegration.Client;

/// <summary>
/// Parses a Jira REST issue JSON node into a <see cref="JiraIssueResponse"/>. Shared by
/// the single-issue GET, search, and batch-by-keys code paths so that field mapping lives
/// in exactly one place.
/// </summary>
public sealed class JiraIssueParser
{
    private readonly JiraSettings _settings;

    public JiraIssueParser(JiraSettings settings)
    {
        _settings = settings;
    }

    public JiraIssueResponse Parse(JsonNode issueNode, string? fallbackKey = null)
    {
        var fields = issueNode["fields"];

        var labels = fields?["labels"] is JsonArray arr
            ? arr.Select(l => l?.GetValue<string>())
                 .Where(l => !string.IsNullOrEmpty(l))
                 .Cast<string>()
                 .ToList()
            : null;

        var components = fields?["components"] is JsonArray componentArr
            ? componentArr.Select(c => c?["name"]?.GetValue<string>())
                          .Where(c => !string.IsNullOrEmpty(c))
                          .Cast<string>()
                          .ToList()
            : null;

        DateTime? updated = null;
        var updatedStr = SafeString(fields?["updated"]);
        if (!string.IsNullOrEmpty(updatedStr) && DateTime.TryParse(updatedStr, out var parsedUpdated))
        {
            updated = parsedUpdated;
        }

        return new JiraIssueResponse
        {
            Key = SafeString(issueNode["key"]) ?? fallbackKey ?? string.Empty,
            Summary = SafeString(fields?["summary"]),
            Description = SafeString(fields?["description"]),
            AcceptanceCriteria = SafeString(CustomField(fields, _settings.AcceptanceCriteriaCustomFieldId)),
            NavigatorId = SafeString(CustomField(fields, _settings.NavigatorIdCustomFieldId)),
            Status = SafeString(fields?["status"]?["name"]),
            IssueType = SafeString(fields?["issuetype"]?["name"]),
            FeatureName = SafeString(CustomField(fields, _settings.FeatureNameCustomFieldId)),
            RagExplain = SafeString(CustomField(fields, _settings.RagExplainCustomFieldId)),
            Labels = labels,
            Components = components,
            ParentLink = SafeString(CustomField(fields, _settings.ParentLinkCustomFieldId)),
            Updated = updated,
            TargetStart = ParseJiraDate(CustomField(fields, _settings.TargetStartCustomFieldId)),
            TargetEnd = ParseJiraDate(CustomField(fields, _settings.TargetEndCustomFieldId)),
            StoryPoints = ParseJiraInt(CustomField(fields, _settings.StoryPointsCustomFieldId)),
            GfedTeam = ParseJiraMultiOption(CustomField(fields, _settings.GfedTeamCustomFieldId)),
            PlanningIncrement = ParseJiraSingleOption(CustomField(fields, _settings.PlanningIncrementCustomFieldId)),
            AssigneeDisplayName = SafeString(fields?["assignee"]?["displayName"]),
            AssigneeUserName = SafeString(fields?["assignee"]?["name"]),
            AssigneeKey = SafeString(fields?["assignee"]?["key"]),
            AssigneeAvatarUrl = SafeString(fields?["assignee"]?["avatarUrls"]?["48x48"]),
            PriorityName = SafeString(fields?["priority"]?["name"]),
            PriorityIconUrl = SafeString(fields?["priority"]?["iconUrl"]),
            FeatureLink = SafeString(CustomField(fields, _settings.FeatureLinkCustomFieldId)),
        };
    }

    private static JsonNode? CustomField(JsonNode? fields, string? customFieldId)
    {
        if (fields is null || string.IsNullOrEmpty(customFieldId))
        {
            return null;
        }
        return fields[customFieldId];
    }

    private static DateTime? ParseJiraDate(JsonNode? node)
    {
        var s = SafeString(node);
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }
        return DateTime.TryParse(s, out var dt) ? dt : null;
    }

    private static int? ParseJiraInt(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        try
        {
            return (int)Math.Round(node.GetValue<double>());
        }
        catch
        {
            var s = SafeString(node);
            return int.TryParse(s, out var i) ? i : null;
        }
    }

    private static string? ParseJiraSingleOption(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonArray arr)
        {
            foreach (var n in arr)
            {
                var v = ParseJiraSingleOption(n);
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }
            return null;
        }

        if (node is JsonObject obj)
        {
            var v = SafeString(obj["value"]) ?? SafeString(obj["name"]);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        var s = SafeString(node);
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        return ExtractNameFromSprintString(s) ?? s;
    }

    private static string? ParseJiraMultiOption(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonArray arr)
        {
            var values = arr
                .Select(ParseJiraSingleOption)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToList();
            return values.Count == 0 ? null : string.Join(", ", values);
        }

        return ParseJiraSingleOption(node);
    }

    // SAFe Planning Increment fields serialize as:
    //   com.atlassian.greenhopper.service.sprint.Sprint@<hash>[id=...,name=PI 2026.1,state=...]
    private static string? ExtractNameFromSprintString(string s)
    {
        const string marker = "name=";
        var i = s.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0 || !s.Contains('['))
        {
            return null;
        }

        var start = i + marker.Length;
        var end = s.IndexOfAny(new[] { ',', ']' }, start);
        if (end < 0)
        {
            end = s.Length;
        }
        var name = s.Substring(start, end - start).Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? SafeString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            // Fall through to generic stringification.
        }
        try
        {
            return node.ToJsonString().Trim('"');
        }
        catch
        {
            return null;
        }
    }
}
