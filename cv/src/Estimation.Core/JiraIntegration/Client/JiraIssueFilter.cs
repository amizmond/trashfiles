namespace Estimation.Core.JiraIntegration.Client;

public sealed class JiraIssueFilter
{
    public const string UnassignedLabel = "Unassigned";

    public const string NoComponentLabel = "(no component)";

    public const string NoLabelLabel = "(no label)";

    public const string NoFixVersionLabel = "(no fix version)";

    public const string NoFeatureLinkLabel = "(no feature)";

    public string? Search { get; set; }

    public IReadOnlyCollection<string> Types { get; set; } = [];

    public IReadOnlyCollection<string> Statuses { get; set; } = [];

    public IReadOnlyCollection<string> Priorities { get; set; } = [];

    public IReadOnlyCollection<string> Assignees { get; set; } = [];

    public IReadOnlyCollection<string> Components { get; set; } = [];

    public IReadOnlyCollection<string> Labels { get; set; } = [];

    public IReadOnlyCollection<string> FixVersions { get; set; } = [];

    public IReadOnlyCollection<string> FeatureLinks { get; set; } = [];

    public bool OnlyWithoutStoryPoints { get; set; }

    public int ActiveCount =>
        new[]
        {
            !string.IsNullOrWhiteSpace(Search),
            Types.Count > 0,
            Statuses.Count > 0,
            Priorities.Count > 0,
            Assignees.Count > 0,
            Components.Count > 0,
            Labels.Count > 0,
            FixVersions.Count > 0,
            FeatureLinks.Count > 0,
            OnlyWithoutStoryPoints,
        }.Count(active => active);

    public bool IsActive => ActiveCount > 0;

    public void Clear()
    {
        Search = null;
        Types = [];
        Statuses = [];
        Priorities = [];
        Assignees = [];
        Components = [];
        Labels = [];
        FixVersions = [];
        FeatureLinks = [];
        OnlyWithoutStoryPoints = false;
    }

    public List<JiraIssueResponse> Apply(IEnumerable<JiraIssueResponse> issues)
    {
        return issues.Where(Matches).ToList();
    }

    public bool Matches(JiraIssueResponse issue)
    {
        var search = Search?.Trim();
        if (!string.IsNullOrEmpty(search)
            && !issue.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
            && issue.Summary?.Contains(search, StringComparison.OrdinalIgnoreCase) != true)
        {
            return false;
        }

        return Selected(Types, issue.IssueType)
               && Selected(Statuses, issue.Status)
               && Selected(Priorities, issue.PriorityName)
               && Selected(Assignees, AssigneeLabel(issue))
               && SelectedAny(Components, issue.Components, NoComponentLabel)
               && SelectedAny(Labels, issue.Labels, NoLabelLabel)
               && SelectedAny(FixVersions, issue.FixVersions, NoFixVersionLabel)
               && Selected(FeatureLinks, FeatureLinkLabel(issue))
               && (!OnlyWithoutStoryPoints || issue.StoryPoints is null);
    }

    public static string AssigneeLabel(JiraIssueResponse issue)
    {
        return string.IsNullOrWhiteSpace(issue.AssigneeDisplayName) ? UnassignedLabel : issue.AssigneeDisplayName!;
    }

    public static string FeatureLinkLabel(JiraIssueResponse issue)
    {
        return string.IsNullOrWhiteSpace(issue.FeatureLink) ? NoFeatureLinkLabel : issue.FeatureLink!;
    }

    public static string ComponentsLabel(JiraIssueResponse issue)
    {
        return issue.Components is { Count: > 0 } components ? string.Join(", ", components) : string.Empty;
    }

    public static List<string> Options(IEnumerable<JiraIssueResponse> issues, Func<JiraIssueResponse, string?> value)
    {
        return DistinctSorted(issues.Select(value));
    }

    public static List<string> Options(
        IEnumerable<JiraIssueResponse> issues,
        Func<JiraIssueResponse, IEnumerable<string>?> values,
        string noValueLabel)
    {
        var list = issues.ToList();
        var options = DistinctSorted(list.SelectMany(i => values(i) ?? []));
        if (list.Any(i => !HasAny(values(i))))
        {
            options.Insert(0, noValueLabel);
        }
        return options;
    }

    private static List<string> DistinctSorted(IEnumerable<string?> values)
    {
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool Selected(IReadOnlyCollection<string> selection, string? value)
    {
        return selection.Count == 0
               || (value is not null && selection.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    private static bool SelectedAny(IReadOnlyCollection<string> selection, IEnumerable<string>? values, string noValueLabel)
    {
        if (selection.Count == 0)
        {
            return true;
        }

        return HasAny(values)
            ? values!.Any(v => selection.Contains(v, StringComparer.OrdinalIgnoreCase))
            : selection.Contains(noValueLabel, StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasAny(IEnumerable<string>? values)
    {
        return values?.Any(v => !string.IsNullOrWhiteSpace(v)) == true;
    }
}
