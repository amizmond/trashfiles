using Estimation.Core.Features.DependencyMap.Services;

namespace Estimation.Core.DepartmentPlanning.Services;

public enum PlanningStatusClass
{
    NotStarted = 0,
    InProgress = 1,
    Blocked = 2,
    Done = 3,
}

public static class PlanningStatus
{
    private static readonly string[] NotStartedStatuses =
        ["Funnel", "Reviewing", "Analysis", "Analyzing", "Backlog", "To Do", "Open", "New"];

    public static PlanningStatusClass Classify(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return PlanningStatusClass.NotStarted;
        }

        var trimmed = status.Trim();
        if (DependencyRiskRule.IsFinished(trimmed) || DependencyRiskRule.IsRejected(trimmed))
        {
            return PlanningStatusClass.Done;
        }

        if (trimmed.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return PlanningStatusClass.Blocked;
        }

        return NotStartedStatuses.Any(s => s.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            ? PlanningStatusClass.NotStarted
            : PlanningStatusClass.InProgress;
    }

    public static string Label(PlanningStatusClass statusClass) => statusClass switch
    {
        PlanningStatusClass.Done => "Done",
        PlanningStatusClass.Blocked => "Blocked",
        PlanningStatusClass.InProgress => "In progress",
        _ => "Not started",
    };
}

public static class ArtAbbreviations
{
    public static IReadOnlyDictionary<int, string> For(IEnumerable<(int Id, string Name)> arts)
    {
        var result = new Dictionary<int, string>();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, name) in arts.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Id))
        {
            var abbreviation = Initials(name);
            if (used.TryGetValue(abbreviation, out var count))
            {
                used[abbreviation] = count + 1;
                abbreviation += (count + 1).ToString();
            }
            else
            {
                used[abbreviation] = 1;
            }

            result[id] = abbreviation;
        }

        return result;
    }

    public static string Initials(string name)
    {
        var words = name.Split([' ', '-', '_', '/', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "?";
        }

        if (words.Length == 1)
        {
            var word = words[0];
            return (word.Length <= 3 ? word : word[..3]).ToUpperInvariant();
        }

        return string.Concat(words.Take(4).Select(w => char.ToUpperInvariant(w[0])));
    }
}
