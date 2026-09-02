namespace Estimation.Core.Features.Hygiene.Models;

public static class HygieneRuleValidation
{
    /// <summary>What stops a rule from being saved; empty when the rule is complete.</summary>
    public static IReadOnlyList<string> Problems(HygieneField field, HygieneCheck check, HygieneRuleParameters parameters)
    {
        var problems = new List<string>();
        var kind = HygieneFieldCatalog.KindOf(field);
        var fieldName = HygieneFieldCatalog.DisplayName(field);

        if (!HygieneChecks.IsAllowed(field, check))
        {
            problems.Add($"{HygieneChecks.DisplayName(check, kind)} cannot be applied to {fieldName}.");
            return problems;
        }

        if (HygieneChecks.NeedsWords(check) && parameters.CleanWords.Count == 0)
        {
            problems.Add($"{fieldName}: enter at least one word or phrase.");
        }

        if (check == HygieneCheck.NotOnlyWords && parameters.MinOtherWords < 1)
        {
            problems.Add($"{fieldName}: the minimum number of other words must be at least 1.");
        }

        if (HygieneChecks.NeedsNumber(check, kind) && parameters.Number is null)
        {
            problems.Add($"{fieldName}: enter a number.");
        }

        if (HygieneChecks.NeedsDate(check, kind) && parameters.Date is null)
        {
            problems.Add($"{fieldName}: enter a date.");
        }

        if (HygieneChecks.NeedsValues(check) && parameters.CleanValues.Count == 0)
        {
            problems.Add($"{fieldName}: choose at least one value.");
        }

        return problems;
    }

    public static IReadOnlyList<string> Problems(FeatureHygieneRule rule) =>
        Problems(rule.Field, rule.Check, rule.Parameters);
}
