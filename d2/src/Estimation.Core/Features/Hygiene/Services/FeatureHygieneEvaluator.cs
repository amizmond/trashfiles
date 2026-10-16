using System.Globalization;
using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Models;

namespace Estimation.Core.Features.Hygiene.Services;

public sealed record HygieneConditionFailure(
    HygieneField Field,
    HygieneCheck Check,
    string Reason,
    string? ActualValue)
{
    public string FieldName => HygieneFieldCatalog.DisplayName(Field);

    public string Label => $"{FieldName}: {Reason}";
}

public sealed record HygieneFailure(
    int RuleId,
    HygieneField Field,
    HygieneCheck Check,
    string RuleText,
    string Reason,
    string? Message,
    string? ActualValue,
    IReadOnlyList<HygieneConditionFailure> Parts)
{
    public string FieldName => HygieneFieldCatalog.DisplayName(Field);

    public bool IsComposite => Parts.Count > 1;

    public string Label => IsComposite ? RuleText : $"{FieldName}: {Reason}";

    public string Display => Message ?? Label;
}

public static class FeatureHygieneEvaluator
{
    public static IReadOnlyList<PreparedHygieneRule> Prepare(IEnumerable<FeatureHygieneRule> rules) =>
        [.. rules.Select(PreparedHygieneRule.From)];

    public static IReadOnlyList<HygieneFailure> Evaluate(Feature feature, IEnumerable<FeatureHygieneRule> rules, DateOnly? today = null) =>
        Evaluate(feature, Prepare(rules.Where(r => r.IsEnabled)), today);

    public static IReadOnlyList<HygieneFailure> Evaluate(Feature feature, IReadOnlyList<PreparedHygieneRule> rules, DateOnly? today = null)
    {
        var failures = new List<HygieneFailure>();
        var asOf = today ?? Today();
        var texts = new TextMemo();

        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];

            if (!rule.IsEnabled)
            {
                continue;
            }

            var failure = Check(feature, rule, asOf, texts);

            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        return failures;
    }

    public static bool Applies(Feature feature, FeatureHygieneRule rule, DateOnly? today = null) =>
        Applies(feature, PreparedHygieneRule.From(rule), today);

    public static bool Applies(Feature feature, PreparedHygieneRule rule, DateOnly? today = null) =>
        Applies(feature, rule, today ?? Today(), new TextMemo());

    public static HygieneFailure? Check(Feature feature, FeatureHygieneRule rule, DateOnly? today = null) =>
        Check(feature, PreparedHygieneRule.From(rule), today);

    public static HygieneFailure? Check(Feature feature, PreparedHygieneRule rule, DateOnly? today = null) =>
        Check(feature, rule, today ?? Today(), new TextMemo());

    private static HygieneFailure? Check(Feature feature, PreparedHygieneRule rule, DateOnly asOf, TextMemo texts)
    {
        if (!Applies(feature, rule, asOf, texts))
        {
            return null;
        }

        var conditions = rule.Then;

        if (conditions.Count == 0)
        {
            return null;
        }

        var failures = new List<HygieneConditionFailure>();

        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            var reason = Reason(feature, condition, asOf, texts);

            if (reason is not null)
            {
                failures.Add(new HygieneConditionFailure(
                    condition.Field,
                    condition.Check,
                    reason,
                    Describe(feature, condition, texts)));
            }
        }

        var passed = rule.ThenLogic == HygieneLogic.Any
            ? failures.Count < conditions.Count
            : failures.Count == 0;

        if (passed)
        {
            return null;
        }

        return new HygieneFailure(
            rule.Id,
            rule.Field,
            rule.Check,
            rule.RuleText,
            Reason(failures),
            rule.Message,
            ActualValue(failures),
            failures);
    }

    private static bool Applies(Feature feature, PreparedHygieneRule rule, DateOnly today, TextMemo texts)
    {
        var conditions = rule.When;

        if (conditions.Count == 0)
        {
            return true;
        }

        var met = 0;

        for (var index = 0; index < conditions.Count; index++)
        {
            if (Reason(feature, conditions[index], today, texts) is null)
            {
                met++;
            }
        }

        return rule.WhenLogic == HygieneLogic.Any ? met > 0 : met == conditions.Count;
    }

    private static string? Describe(Feature feature, PreparedHygieneCondition condition, TextMemo texts) =>
        condition.Kind == HygieneFieldKind.Text
            ? HygieneText.ExcerptOfNormalized(texts.Normalized(HygieneFieldReader.Text(feature, condition.Field)))
            : HygieneFieldReader.Describe(feature, condition.Field);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);

    private static string Reason(IReadOnlyList<HygieneConditionFailure> failures) =>
        failures.Count == 1
            ? failures[0].Reason
            : string.Join(" and ", failures.Select(f => $"{f.FieldName} {f.Reason}"));

    private static string? ActualValue(IReadOnlyList<HygieneConditionFailure> failures) =>
        failures.Count == 1
            ? failures[0].ActualValue
            : string.Join(" · ", failures.Select(f => $"{f.FieldName}: {f.ActualValue ?? "empty"}"));

    private static string? Reason(Feature feature, PreparedHygieneCondition condition, DateOnly today, TextMemo texts)
    {
        var field = condition.Field;
        var kind = condition.Kind;

        return condition.Check switch
        {
            HygieneCheck.NotEmpty => NotEmpty(feature, field, kind, texts),
            HygieneCheck.IsEmpty => NotEmpty(feature, field, kind, texts) is null ? "not empty" : null,
            HygieneCheck.ContainsWords => ContainsWords(feature, condition, texts),
            HygieneCheck.NotOnlyWords => NotOnlyWords(feature, condition, texts),
            HygieneCheck.NotGreaterThan => kind == HygieneFieldKind.Date
                ? DateLimit(feature, condition, today, after: true)
                : NumberLimit(feature, condition, greater: true),
            HygieneCheck.NotLessThan => kind == HygieneFieldKind.Date
                ? DateLimit(feature, condition, today, after: false)
                : NumberLimit(feature, condition, greater: false),
            HygieneCheck.InValues => InValues(feature, condition, mustBeIn: true),
            HygieneCheck.NotInValues => InValues(feature, condition, mustBeIn: false),
            HygieneCheck.IsTrue => HygieneFieldReader.Flag(feature, field) ? null : "no",
            HygieneCheck.IsFalse => HygieneFieldReader.Flag(feature, field) ? "yes" : null,
            HygieneCheck.PrimaryTeamMarked => PrimaryTeamMarked(feature),
            HygieneCheck.StoryPointsMatchTeams => StoryPointsMatchTeams(feature),
            _ => null
        };
    }

    private static string? PrimaryTeamMarked(Feature feature)
    {
        var teams = feature.FeatureTeams ?? [];

        if (teams.Count < 2)
        {
            return null;
        }

        return teams.Any(t => t.IsPrimary == true)
            ? null
            : $"{teams.Count} teams, none marked primary";
    }

    private static string? StoryPointsMatchTeams(Feature feature)
    {
        var teams = feature.FeatureTeams ?? [];

        if (teams.Count == 0)
        {
            return null;
        }

        var total = teams.Sum(TeamEffectiveStoryPoints);
        var featurePoints = feature.StoryPoints ?? 0;

        return featurePoints == total
            ? null
            : $"{featurePoints} ≠ teams total {total}";
    }

    private static int TeamEffectiveStoryPoints(FeatureTeam team) =>
        team.TechnologyStacks is { Count: > 0 } stacks
            ? stacks.Sum(s => s.StoryPoints ?? 0)
            : team.StoryPoints ?? 0;

    private static string? NotEmpty(Feature feature, HygieneField field, HygieneFieldKind kind, TextMemo texts)
    {
        if (kind == HygieneFieldKind.Text)
        {
            var text = HygieneFieldReader.Text(feature, field);

            return HygieneText.HasLetterOrDigit(texts.Body(text))
                ? null
                : HygieneText.HasLetterOrDigit(texts.Normalized(text)) ? "only a heading" : "empty";
        }

        var isEmpty = kind switch
        {
            HygieneFieldKind.Number => HygieneFieldReader.Number(feature, field) is null,
            HygieneFieldKind.Date => HygieneFieldReader.Date(feature, field) is null,
            HygieneFieldKind.Reference => HygieneFieldReader.ReferenceIsEmpty(feature, field),
            HygieneFieldKind.Choice => string.IsNullOrWhiteSpace(HygieneFieldReader.Choice(feature, field)),
            _ => false
        };

        return isEmpty ? "empty" : null;
    }

    private static string? ContainsWords(Feature feature, PreparedHygieneCondition condition, TextMemo texts)
    {
        var words = condition.Words;

        if (words.Count == 0)
        {
            return null;
        }

        var text = texts.Normalized(HygieneFieldReader.Text(feature, condition.Field));
        var missing = HygieneText.MissingPhrases(text, words, condition.WordRegexes);

        if (missing.Count == 0)
        {
            return null;
        }

        if (condition.Mode == HygieneWordMode.Or && missing.Count < words.Count)
        {
            return null;
        }

        return condition.Mode == HygieneWordMode.Or
            ? $"none of {HygieneRuleParameters.JoinPhrases(words)}"
            : $"missing {HygieneRuleParameters.JoinPhrases(missing)}";
    }

    private static string? NotOnlyWords(Feature feature, PreparedHygieneCondition condition, TextMemo texts)
    {
        var minimum = Math.Max(1, condition.MinOtherWords);
        var text = texts.Normalized(HygieneFieldReader.Text(feature, condition.Field));
        var count = HygieneText.CountOtherWords(text, condition.WordRegexes);

        if (count >= minimum)
        {
            return null;
        }

        return count == 0
            ? "no other words"
            : $"only {count} other word{(count == 1 ? string.Empty : "s")}, {minimum} needed";
    }

    private static string? NumberLimit(Feature feature, PreparedHygieneCondition condition, bool greater)
    {
        var value = HygieneFieldReader.Number(feature, condition.Field);

        if (value is null || condition.Number is not { } limit)
        {
            return null;
        }

        if (greater ? value <= limit : value >= limit)
        {
            return null;
        }

        return $"{HygieneRuleParameters.FormatNumber(value.Value)} {(greater ? ">" : "<")} {HygieneRuleParameters.FormatNumber(limit)}";
    }

    private static string? DateLimit(Feature feature, PreparedHygieneCondition condition, DateOnly today, bool after)
    {
        var value = HygieneFieldReader.Date(feature, condition.Field);

        if (value is null || (condition.Today ? today : condition.Date) is not { } limit)
        {
            return null;
        }

        var day = DateOnly.FromDateTime(value.Value);

        if (after ? day <= limit : day >= limit)
        {
            return null;
        }

        var limitText = condition.Today ? "today" : HygieneRuleParameters.FormatDate(limit);

        return $"{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is {(after ? "after" : "before")} {limitText}";
    }

    private static string? InValues(Feature feature, PreparedHygieneCondition condition, bool mustBeIn)
    {
        var values = condition.Values;

        if (values.Count == 0)
        {
            return null;
        }

        var actual = HygieneFieldReader.Choice(feature, condition.Field)?.Trim();
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

    private sealed class TextMemo
    {
        private List<Entry>? _entries;

        public string Normalized(string? text) =>
            string.IsNullOrWhiteSpace(text) ? string.Empty : EntryFor(text).Normalized ??= HygieneText.Normalize(text);

        public string Body(string? text) =>
            string.IsNullOrWhiteSpace(text) ? string.Empty : EntryFor(text).Body ??= HygieneText.Body(text);

        private Entry EntryFor(string text)
        {
            _entries ??= [];

            foreach (var entry in _entries)
            {
                if (ReferenceEquals(entry.Text, text))
                {
                    return entry;
                }
            }

            var added = new Entry(text);
            _entries.Add(added);
            return added;
        }

        private sealed class Entry(string text)
        {
            public string Text { get; } = text;

            public string? Normalized { get; set; }

            public string? Body { get; set; }
        }
    }
}
