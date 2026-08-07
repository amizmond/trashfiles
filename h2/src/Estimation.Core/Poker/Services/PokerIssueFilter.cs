using Estimation.Core.JiraIntegration.Client;

namespace Estimation.Core.Poker.Services;

public sealed class PokerIssueFilter
{
    public const string UnassignedLabel = "Unassigned";

    public string? Search { get; set; }

    public IReadOnlyCollection<string> Types { get; set; } = [];

    public IReadOnlyCollection<string> Statuses { get; set; } = [];

    public IReadOnlyCollection<string> Priorities { get; set; } = [];

    public IReadOnlyCollection<string> Assignees { get; set; } = [];

    public IReadOnlyCollection<string> Components { get; set; } = [];

    public bool OnlyWithoutStoryPoints { get; set; }

    public bool IsActive =>
        !string.IsNullOrWhiteSpace(Search)
        || Types.Count > 0
        || Statuses.Count > 0
        || Priorities.Count > 0
        || Assignees.Count > 0
        || Components.Count > 0
        || OnlyWithoutStoryPoints;

    public void Clear()
    {
        Search = null;
        Types = [];
        Statuses = [];
        Priorities = [];
        Assignees = [];
        Components = [];
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
               && SelectedAny(Components, issue.Components)
               && (!OnlyWithoutStoryPoints || issue.StoryPoints is null);
    }

    public static string AssigneeLabel(JiraIssueResponse issue)
    {
        return string.IsNullOrWhiteSpace(issue.AssigneeDisplayName) ? UnassignedLabel : issue.AssigneeDisplayName!;
    }

    public static string ComponentsLabel(JiraIssueResponse issue)
    {
        return issue.Components is { Count: > 0 } components ? string.Join(", ", components) : string.Empty;
    }

    private static bool Selected(IReadOnlyCollection<string> selection, string? value)
    {
        return selection.Count == 0
               || (value is not null && selection.Contains(value, StringComparer.OrdinalIgnoreCase));
    }

    private static bool SelectedAny(IReadOnlyCollection<string> selection, IEnumerable<string>? values)
    {
        return selection.Count == 0
               || (values is not null && values.Any(v => selection.Contains(v, StringComparer.OrdinalIgnoreCase)));
    }
}
