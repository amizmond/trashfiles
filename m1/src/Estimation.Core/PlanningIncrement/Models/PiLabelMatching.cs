using Estimation.Core.Train.Services;

namespace Estimation.Core.PlanningIncrement.Models;

public static class PiLabelMatching
{
    public static HashSet<string> ParseLabels(string? csv) => JiraListValues.Parse(csv);

    public static bool Matches(HashSet<string> ruleLabels, PiLabelMatchMode mode, string? featureLabels)
    {
        if (ruleLabels.Count == 0)
        {
            return false;
        }

        var fLabels = ParseLabels(featureLabels);
        if (fLabels.Count == 0)
        {
            return false;
        }

        return mode == PiLabelMatchMode.All
            ? ruleLabels.All(fLabels.Contains)
            : ruleLabels.Any(fLabels.Contains);
    }
}
