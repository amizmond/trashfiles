namespace Estimation.Core.Features.Hygiene.Models;

/// <summary>Human wording for rules and their parameters, shared by the pages and the Excel export.</summary>
public static class HygieneRuleText
{
    /// <summary>The whole rule in one line, for example "Description contains Task Description, Result".</summary>
    public static string Describe(FeatureHygieneRule rule) => Describe(rule.Field, rule.Check, rule.Parameters);

    public static string Describe(HygieneField field, HygieneCheck check, HygieneRuleParameters parameters)
    {
        var name = HygieneFieldCatalog.DisplayName(field);
        var kind = HygieneFieldCatalog.KindOf(field);

        return check switch
        {
            HygieneCheck.NotEmpty => $"{name} is not empty",
            HygieneCheck.ContainsWords => $"{name} contains {DescribeWords(parameters)}",
            HygieneCheck.NotOnlyWords =>
                $"{name} has at least {parameters.MinOtherWords} other word{Plural(parameters.MinOtherWords)} besides {Join(parameters.CleanWords)}",
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

    /// <summary>Only the parameters, for the rules table: "Task Description, Result (all)".</summary>
    public static string DescribeParameters(HygieneField field, HygieneCheck check, HygieneRuleParameters parameters)
    {
        var kind = HygieneFieldCatalog.KindOf(field);

        return check switch
        {
            HygieneCheck.ContainsWords => DescribeWords(parameters),
            HygieneCheck.NotOnlyWords =>
                $"{Join(parameters.CleanWords)} · at least {parameters.MinOtherWords} other word{Plural(parameters.MinOtherWords)}",
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

        if (words.Count <= 1)
        {
            return Join(words);
        }

        return $"{Join(words)} ({(parameters.Mode == HygieneWordMode.Or ? "any" : "all")})";
    }

    private static string DescribeDate(HygieneRuleParameters parameters) =>
        parameters.Date is { } date ? HygieneRuleParameters.FormatDate(date) : "?";

    private static string DescribeNumber(HygieneRuleParameters parameters) =>
        parameters.Number is { } number ? HygieneRuleParameters.FormatNumber(number) : "?";

    private static string Join(IEnumerable<string> items)
    {
        var text = HygieneRuleParameters.JoinPhrases(items);
        return text.Length == 0 ? "?" : text;
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
