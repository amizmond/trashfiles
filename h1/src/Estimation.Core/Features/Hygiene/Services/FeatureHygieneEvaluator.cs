using System.Globalization;
using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Models;

namespace Estimation.Core.Features.Hygiene.Services;

/// <summary>One failed rule on one feature.</summary>
/// <param name="RuleId">The rule that failed.</param>
/// <param name="RuleText">The rule in words, for example "Description contains Task Description, Result".</param>
/// <param name="Reason">Why it failed, short enough for a chip: "empty", "missing Result", "34 > 21".</param>
/// <param name="Message">The rule author's own wording, when they gave one.</param>
/// <param name="ActualValue">The value the feature has, as an excerpt.</param>
public sealed record HygieneFailure(
    int RuleId,
    HygieneField Field,
    HygieneCheck Check,
    string RuleText,
    string Reason,
    string? Message,
    string? ActualValue)
{
    public string FieldName => HygieneFieldCatalog.DisplayName(Field);

    /// <summary>"Description: missing Result"</summary>
    public string Label => $"{FieldName}: {Reason}";
}

/// <summary>
/// Pure evaluation of hygiene rules against a feature. No database, no services: it takes the
/// feature with its lookups loaded and the rules, and returns the failures.
/// </summary>
public static class FeatureHygieneEvaluator
{
    public static IReadOnlyList<HygieneFailure> Evaluate(Feature feature, IEnumerable<FeatureHygieneRule> rules)
    {
        var failures = new List<HygieneFailure>();

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled)
            {
                continue;
            }

            var failure = Check(feature, rule);

            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        return failures;
    }

    /// <summary>The failure for one rule, or null when the feature passes it.</summary>
    public static HygieneFailure? Check(Feature feature, FeatureHygieneRule rule)
    {
        if (!HygieneChecks.IsAllowed(rule.Field, rule.Check))
        {
            return null;
        }

        var parameters = rule.Parameters;
        var reason = Reason(feature, rule.Field, rule.Check, parameters);

        if (reason is null)
        {
            return null;
        }

        return new HygieneFailure(
            rule.Id,
            rule.Field,
            rule.Check,
            HygieneRuleText.Describe(rule.Field, rule.Check, parameters),
            reason,
            string.IsNullOrWhiteSpace(rule.Message) ? null : rule.Message.Trim(),
            HygieneFieldReader.Describe(feature, rule.Field));
    }

    private static string? Reason(Feature feature, HygieneField field, HygieneCheck check, HygieneRuleParameters parameters)
    {
        var kind = HygieneFieldCatalog.KindOf(field);

        return check switch
        {
            HygieneCheck.NotEmpty => NotEmpty(feature, field, kind),
            HygieneCheck.ContainsWords => ContainsWords(feature, field, parameters),
            HygieneCheck.NotOnlyWords => NotOnlyWords(feature, field, parameters),
            HygieneCheck.NotGreaterThan => kind == HygieneFieldKind.Date
                ? DateLimit(feature, field, parameters, after: true)
                : NumberLimit(feature, field, parameters, greater: true),
            HygieneCheck.NotLessThan => kind == HygieneFieldKind.Date
                ? DateLimit(feature, field, parameters, after: false)
                : NumberLimit(feature, field, parameters, greater: false),
            HygieneCheck.InValues => InValues(feature, field, parameters, mustBeIn: true),
            HygieneCheck.NotInValues => InValues(feature, field, parameters, mustBeIn: false),
            HygieneCheck.IsTrue => HygieneFieldReader.Flag(feature, field) ? null : "no",
            HygieneCheck.IsFalse => HygieneFieldReader.Flag(feature, field) ? "yes" : null,
            _ => null
        };
    }

    private static string? NotEmpty(Feature feature, HygieneField field, HygieneFieldKind kind)
    {
        var isEmpty = kind switch
        {
            HygieneFieldKind.Text => !HygieneText.HasContent(HygieneFieldReader.Text(feature, field)),
            HygieneFieldKind.Number => HygieneFieldReader.Number(feature, field) is null,
            HygieneFieldKind.Date => HygieneFieldReader.Date(feature, field) is null,
            HygieneFieldKind.Reference => HygieneFieldReader.ReferenceIsEmpty(feature, field),
            HygieneFieldKind.Choice => string.IsNullOrWhiteSpace(HygieneFieldReader.Choice(feature, field)),
            _ => false
        };

        return isEmpty ? "empty" : null;
    }

    private static string? ContainsWords(Feature feature, HygieneField field, HygieneRuleParameters parameters)
    {
        var words = parameters.CleanWords;

        if (words.Count == 0)
        {
            return null;
        }

        var text = HygieneText.Normalize(HygieneFieldReader.Text(feature, field));
        var missing = HygieneText.MissingPhrases(text, words);

        if (missing.Count == 0)
        {
            return null;
        }

        if (parameters.Mode == HygieneWordMode.Or && missing.Count < words.Count)
        {
            return null;
        }

        return parameters.Mode == HygieneWordMode.Or
            ? $"none of {HygieneRuleParameters.JoinPhrases(words)}"
            : $"missing {HygieneRuleParameters.JoinPhrases(missing)}";
    }

    private static string? NotOnlyWords(Feature feature, HygieneField field, HygieneRuleParameters parameters)
    {
        var minimum = Math.Max(1, parameters.MinOtherWords);
        var text = HygieneText.Normalize(HygieneFieldReader.Text(feature, field));
        var count = HygieneText.CountOtherWords(text, parameters.CleanWords);

        if (count >= minimum)
        {
            return null;
        }

        return count == 0
            ? "no other words"
            : $"only {count} other word{(count == 1 ? string.Empty : "s")}, {minimum} needed";
    }

    private static string? NumberLimit(Feature feature, HygieneField field, HygieneRuleParameters parameters, bool greater)
    {
        var value = HygieneFieldReader.Number(feature, field);

        if (value is null || parameters.Number is not { } limit)
        {
            return null;
        }

        if (greater ? value <= limit : value >= limit)
        {
            return null;
        }

        return $"{HygieneRuleParameters.FormatNumber(value.Value)} {(greater ? ">" : "<")} {HygieneRuleParameters.FormatNumber(limit)}";
    }

    private static string? DateLimit(Feature feature, HygieneField field, HygieneRuleParameters parameters, bool after)
    {
        var value = HygieneFieldReader.Date(feature, field);

        if (value is null || parameters.Date is not { } limit)
        {
            return null;
        }

        var day = DateOnly.FromDateTime(value.Value);

        if (after ? day <= limit : day >= limit)
        {
            return null;
        }

        return $"{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is {(after ? "after" : "before")} {HygieneRuleParameters.FormatDate(limit)}";
    }

    private static string? InValues(Feature feature, HygieneField field, HygieneRuleParameters parameters, bool mustBeIn)
    {
        var values = parameters.CleanValues;

        if (values.Count == 0)
        {
            return null;
        }

        var actual = HygieneFieldReader.Choice(feature, field)?.Trim();
        var isEmpty = string.IsNullOrEmpty(actual);
        var listed = isEmpty
            ? values.Contains(HygieneRuleParameters.EmptyValue, StringComparer.OrdinalIgnoreCase)
            : values.Contains(actual!, StringComparer.OrdinalIgnoreCase);

        if (listed == mustBeIn)
        {
            return null;
        }

        return isEmpty ? "empty" : actual;
    }
}
