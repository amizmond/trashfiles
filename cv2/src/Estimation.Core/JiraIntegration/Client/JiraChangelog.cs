using System.Globalization;
using System.Text.RegularExpressions;

namespace Estimation.Core.JiraIntegration.Client;

public sealed class JiraChangelogEntry
{
    public string Id { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; }

    public List<JiraChangelogItem> Items { get; set; } = [];
}

public sealed class JiraChangelogItem
{
    public string Field { get; set; } = string.Empty;

    public string? FieldId { get; set; }

    public string? FieldType { get; set; }

    public string? From { get; set; }

    public string? FromText { get; set; }

    public string? To { get; set; }

    public string? ToText { get; set; }
}

public sealed class JiraSprintValue
{
    public int? Id { get; set; }

    public string? Name { get; set; }

    public string? State { get; set; }

    public int? BoardId { get; set; }

    public string? Goal { get; set; }

    public DateTimeOffset? StartDate { get; set; }

    public DateTimeOffset? EndDate { get; set; }

    public DateTimeOffset? CompleteDate { get; set; }
}

public sealed record JiraSearchOptions
{
    public static JiraSearchOptions Default { get; } = new();

    public bool IncludeLinks { get; init; }

    public bool IncludeChangelog { get; init; }

    public string? SprintFieldId { get; init; }

    public static JiraSearchOptions History(string? sprintFieldId) =>
        new() { IncludeChangelog = true, SprintFieldId = sprintFieldId };
}

public static class JiraTimestamps
{
    private static readonly Regex CompactOffset = new(@"([+-]\d{2})(\d{2})$", RegexOptions.Compiled);

    public static DateTimeOffset? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Equals("<null>", StringComparison.OrdinalIgnoreCase) || text.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        text = CompactOffset.Replace(text, "$1:$2");
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : null;
    }
}
