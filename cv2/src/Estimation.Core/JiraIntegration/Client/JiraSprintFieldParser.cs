using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Estimation.Core.JiraIntegration.Client;

public static class JiraSprintFieldParser
{
    private static readonly Regex EntrySeparator = new(@",(?=[A-Za-z]\w*=)", RegexOptions.Compiled);

    public static List<JiraSprintValue>? Parse(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var values = new List<JiraSprintValue>();
        if (node is JsonArray array)
        {
            foreach (var element in array)
            {
                var value = ParseOne(element);
                if (value is not null)
                {
                    values.Add(value);
                }
            }
        }
        else if (ParseOne(node) is { } single)
        {
            values.Add(single);
        }

        return values;
    }

    public static JiraSprintValue? ParseOne(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            return new JiraSprintValue
            {
                Id = ParseInt(Text(obj["id"])),
                Name = Text(obj["name"]),
                State = Text(obj["state"]),
                BoardId = ParseInt(Text(obj["boardId"])) ?? ParseInt(Text(obj["rapidViewId"])),
                Goal = Text(obj["goal"]),
                StartDate = JiraTimestamps.Parse(Text(obj["startDate"])),
                EndDate = JiraTimestamps.Parse(Text(obj["endDate"])),
                CompleteDate = JiraTimestamps.Parse(Text(obj["completeDate"])),
            };
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return ParseText(text);
        }

        return null;
    }

    public static JiraSprintValue? ParseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var open = text.IndexOf('[');
        var close = text.LastIndexOf(']');
        if (open < 0 || close <= open)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in EntrySeparator.Split(text.Substring(open + 1, close - open - 1)))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            map[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }

        string? Get(string key) =>
            map.TryGetValue(key, out var v) && v.Length > 0 && !v.Equals("<null>", StringComparison.OrdinalIgnoreCase) ? v : null;

        return new JiraSprintValue
        {
            Id = ParseInt(Get("id")),
            Name = Get("name"),
            State = Get("state"),
            BoardId = ParseInt(Get("rapidViewId")),
            Goal = Get("goal"),
            StartDate = JiraTimestamps.Parse(Get("startDate")),
            EndDate = JiraTimestamps.Parse(Get("endDate")),
            CompleteDate = JiraTimestamps.Parse(Get("completeDate")),
        };
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;

    private static string? Text(JsonNode? node)
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
            return node.ToJsonString().Trim('"');
        }
    }
}
