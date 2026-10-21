namespace Estimation.Core.Features.Hygiene.Models;

public static class HygieneRuleText
{
    public static string Describe(FeatureHygieneRule rule)
    {
        var logic = rule.Logic;
        return Describe(logic, rule.ThenConditionsOf(logic));
    }

    public static string Title(FeatureHygieneRule rule) =>
        string.IsNullOrWhiteSpace(rule.Message) ? Describe(rule) : rule.Message.Trim();

    internal static string Describe(HygieneRuleLogic logic, IEnumerable<HygieneCondition> thenConditions)
    {
        var then = DescribeConditions(thenConditions, logic.ThenLogic);

        return logic.When.Count == 0
            ? then
            : $"If {DescribeConditions(logic.When, logic.WhenLogic)}, then {then}";
    }

    public static string DescribeConditions(IEnumerable<HygieneCondition> conditions, HygieneLogic logic)
    {
        var parts = conditions.Select(Describe).ToList();

        return parts.Count == 0
            ? "?"
            : string.Join(logic == HygieneLogic.Any ? " or " : " and ", parts);
    }

    public static string Describe(HygieneCondition condition) =>
        Describe(condition.Field, condition.Check, condition.Parameters);

    public static string DescribeShape(FeatureHygieneRule rule)
    {
        var logic = rule.Logic;
        var side = logic.MoreThen.Count == 0
            ? HygieneChecks.DisplayName(rule.Check, HygieneFieldCatalog.KindOf(rule.Field))
            : logic.ThenLogic == HygieneLogic.Any ? "Any of" : "All of";

        return logic.When.Count == 0 ? side : $"Only when · {side}";
    }

    public static string Describe(HygieneField field, HygieneCheck check, HygieneRuleParameters parameters)
    {
        if (HygieneChecks.Specific(check) is { } specific)
        {
            return specific.Title;
        }

        var name = HygieneFieldCatalog.DisplayName(field);
        var kind = HygieneFieldCatalog.KindOf(field);

        return check switch
        {
            HygieneCheck.NotEmpty => $"{name} is not empty",
            HygieneCheck.IsEmpty => $"{name} is empty",
            HygieneCheck.ContainsWords => $"{name} contains {DescribeWords(parameters)}",
            HygieneCheck.NotOnlyWords =>
                $"{name} has at least {parameters.MinOtherWords} other word{Plural(parameters.MinOtherWords)} besides {Join(parameters.CleanWords)}"
                + (parameters.CaseSensitive ? $" ({CaseSensitiveNote})" : string.Empty),
            HygieneCheck.NotGreaterThan => kind == HygieneFieldKind.Date
                ? $"{name} is not after {DescribeDate(parameters)}"
                : $"{name} is not greater than {DescribeNumber(parameters)}",
            HygieneCheck.NotLessThan => kind == HygieneFieldKind.Date
                ? $"{name} is not before {DescribeDate(parameters)}"
                : $"{name} is not less than {DescribeNumber(parameters)}",
            HygieneCheck.InValues => $"{name} is one of {Join(parameters.CleanValues)}",
            HygieneCheck.NotInValues => $"{name} is none of {Join(parameters.CleanValues)}",
            HygieneCheck.IsTrue => $"{name} is yes",
            HygieneCheck.IsFalse => $"{name} is no",
            _ => $"{name} {check}"
        };
    }

    public static string DescribeParameters(HygieneField field, HygieneCheck check, HygieneRuleParameters parameters)
    {
        var kind = HygieneFieldCatalog.KindOf(field);

        return check switch
        {
            HygieneCheck.ContainsWords => DescribeWords(parameters),
            HygieneCheck.NotOnlyWords =>
                $"{Join(parameters.CleanWords)} · at least {parameters.MinOtherWords} other word{Plural(parameters.MinOtherWords)}"
                + DescribeCase(parameters),
            HygieneCheck.NotGreaterThan or HygieneCheck.NotLessThan => kind == HygieneFieldKind.Date
                ? DescribeDate(parameters)
                : DescribeNumber(parameters),
            HygieneCheck.InValues or HygieneCheck.NotInValues => Join(parameters.CleanValues),
            _ => string.Empty
        };
    }

    private static string DescribeWords(HygieneRuleParameters parameters)
    {
        var words = parameters.CleanWords;
        var notes = new List<string>();

        if (words.Count > 1)
        {
            notes.Add(parameters.Mode == HygieneWordMode.Or ? "any" : "all");
        }

        if (parameters.CaseSensitive)
        {
            notes.Add(CaseSensitiveNote);
        }

        return notes.Count == 0 ? Join(words) : $"{Join(words)} ({string.Join(", ", notes)})";
    }

    private const string CaseSensitiveNote = "case-sensitive";

    private static string DescribeCase(HygieneRuleParameters parameters) =>
        parameters.CaseSensitive ? $" · {CaseSensitiveNote}" : string.Empty;

    private static string DescribeDate(HygieneRuleParameters parameters) =>
        parameters.Today ? "today" : parameters.Date is { } date ? HygieneRuleParameters.FormatDate(date) : "?";

    private static string DescribeNumber(HygieneRuleParameters parameters) =>
        parameters.Number is { } number ? HygieneRuleParameters.FormatNumber(number) : "?";

    private static string Join(IEnumerable<string> items)
    {
        var text = HygieneRuleParameters.JoinPhrases(items);
        return text.Length == 0 ? "?" : text;
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
