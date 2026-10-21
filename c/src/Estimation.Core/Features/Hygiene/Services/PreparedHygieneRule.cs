using System.Text.RegularExpressions;
using Estimation.Core.Features.Hygiene.Models;

namespace Estimation.Core.Features.Hygiene.Services;

public sealed class PreparedHygieneRule
{
    private PreparedHygieneRule(FeatureHygieneRule rule)
    {
        var logic = rule.Logic;
        var then = rule.ThenConditionsOf(logic);

        Id = rule.Id;
        Field = rule.Field;
        Check = rule.Check;
        IsEnabled = rule.IsEnabled;
        IsConditional = logic.IsConditional;
        RuleText = HygieneRuleText.Describe(logic, then);
        Message = string.IsNullOrWhiteSpace(rule.Message) ? null : rule.Message.Trim();
        ThenLogic = logic.ThenLogic;
        Then = Checkable(then);
        WhenLogic = logic.WhenLogic;
        When = Checkable(logic.When);
        NeedsParents = Then.Concat(When).Any(c => c.Check == HygieneCheck.ParentsExist);
    }

    public int Id { get; }

    public HygieneField Field { get; }

    public HygieneCheck Check { get; }

    public bool IsEnabled { get; }

    public bool IsConditional { get; }

    public bool NeedsParents { get; }

    public string RuleText { get; }

    public string? Message { get; }

    public HygieneLogic ThenLogic { get; }

    public HygieneLogic WhenLogic { get; }

    internal IReadOnlyList<PreparedHygieneCondition> Then { get; }

    internal IReadOnlyList<PreparedHygieneCondition> When { get; }

    public static PreparedHygieneRule From(FeatureHygieneRule rule) => new(rule);

    private static IReadOnlyList<PreparedHygieneCondition> Checkable(IEnumerable<HygieneCondition> conditions) =>
        [.. conditions.Where(c => HygieneChecks.IsAllowed(c.Field, c.Check)).Select(c => new PreparedHygieneCondition(c))];
}

internal sealed class PreparedHygieneCondition
{
    public PreparedHygieneCondition(HygieneCondition condition)
    {
        var parameters = condition.Parameters;

        Field = condition.Field;
        Check = condition.Check;
        Kind = HygieneFieldCatalog.KindOf(condition.Field);
        Words = [.. parameters.CleanWords];
        Mode = parameters.Mode;
        MinOtherWords = parameters.MinOtherWords;
        CaseSensitive = parameters.CaseSensitive;
        WordRegexes = [.. Words.Select(w => HygieneText.PhraseRegex(w, parameters.CaseSensitive))];
        Number = parameters.Number;
        Date = parameters.Date;
        Today = parameters.Today;
        Values = [.. parameters.CleanValues];
    }

    public HygieneField Field { get; }

    public HygieneCheck Check { get; }

    public HygieneFieldKind Kind { get; }

    public IReadOnlyList<string> Words { get; }

    public IReadOnlyList<Regex?> WordRegexes { get; }

    public HygieneWordMode Mode { get; }

    public int MinOtherWords { get; }

    public bool CaseSensitive { get; }

    public decimal? Number { get; }

    public DateOnly? Date { get; }

    public bool Today { get; }

    public IReadOnlyList<string> Values { get; }
}
