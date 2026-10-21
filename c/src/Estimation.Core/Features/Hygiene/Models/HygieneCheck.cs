namespace Estimation.Core.Features.Hygiene.Models;

public enum HygieneCheck
{
    NotEmpty,

    ContainsWords,

    NotOnlyWords,

    NotGreaterThan,

    NotLessThan,

    InValues,

    NotInValues,

    IsTrue,

    IsFalse,

    PrimaryTeamMarked,

    StoryPointsMatchTeams,

    IsEmpty,

    ParentsExist
}

public sealed record HygieneSpecificRule(HygieneCheck Check, HygieneField Field, string Title, string Description);

public static class HygieneChecks
{
    private static readonly IReadOnlyList<HygieneCheck> TextChecks =
        [HygieneCheck.NotEmpty, HygieneCheck.IsEmpty, HygieneCheck.ContainsWords, HygieneCheck.NotOnlyWords];

    private static readonly IReadOnlyList<HygieneCheck> RangeChecks =
        [HygieneCheck.NotEmpty, HygieneCheck.IsEmpty, HygieneCheck.NotGreaterThan, HygieneCheck.NotLessThan];

    private static readonly IReadOnlyList<HygieneCheck> ReferenceChecks =
        [HygieneCheck.NotEmpty, HygieneCheck.IsEmpty];

    private static readonly IReadOnlyList<HygieneCheck> ChoiceChecks =
        [HygieneCheck.NotEmpty, HygieneCheck.IsEmpty, HygieneCheck.InValues, HygieneCheck.NotInValues];

    private static readonly IReadOnlyList<HygieneCheck> FlagChecks = [HygieneCheck.IsTrue, HygieneCheck.IsFalse];

    public static readonly IReadOnlyList<HygieneSpecificRule> SpecificRules =
    [
        new(HygieneCheck.PrimaryTeamMarked, HygieneField.Teams,
            "Shared features have a primary team",
            "A feature with more than one team must have one of them marked as primary."),
        new(HygieneCheck.StoryPointsMatchTeams, HygieneField.StoryPoints,
            "Story points match the teams total",
            "The feature's story points must equal the total of its teams, the same comparison the feature page "
            + "shows. A team with technology stacks counts the stacks' story points. A feature without teams passes."),
        new(HygieneCheck.ParentsExist, HygieneField.BusinessOutcome,
            "All parents exist",
            "Feature → Business outcome → Portfolio epic → Strategic objective: the feature has a business outcome, "
            + "the business outcome has a portfolio epic and the portfolio epic has a strategic objective, as stored "
            + "in the tool after the last Jira sync.")
    ];

    public static IReadOnlyList<HygieneCheck> AllowedFor(HygieneFieldKind kind) => kind switch
    {
        HygieneFieldKind.Text => TextChecks,
        HygieneFieldKind.Number => RangeChecks,
        HygieneFieldKind.Date => RangeChecks,
        HygieneFieldKind.Reference => ReferenceChecks,
        HygieneFieldKind.Choice => ChoiceChecks,
        HygieneFieldKind.Flag => FlagChecks,
        _ => []
    };

    public static bool IsSpecific(HygieneCheck check) => SpecificRules.Any(s => s.Check == check);

    public static HygieneSpecificRule? Specific(HygieneCheck check) => SpecificRules.FirstOrDefault(s => s.Check == check);

    public static bool IsAllowed(HygieneField field, HygieneCheck check) =>
        Specific(check) is { } specific
            ? specific.Field == field
            : AllowedFor(HygieneFieldCatalog.KindOf(field)).Contains(check);

    public static string DisplayName(HygieneCheck check, HygieneFieldKind kind) => check switch
    {
        HygieneCheck.NotEmpty => "Not empty",
        HygieneCheck.IsEmpty => "Is empty",
        HygieneCheck.ContainsWords => "Contains words",
        HygieneCheck.NotOnlyWords => "Not only these words",
        HygieneCheck.NotGreaterThan => kind == HygieneFieldKind.Date ? "Not after" : "Not greater than",
        HygieneCheck.NotLessThan => kind == HygieneFieldKind.Date ? "Not before" : "Not less than",
        HygieneCheck.InValues => "In values",
        HygieneCheck.NotInValues => "Not in values",
        HygieneCheck.IsTrue => "Must be yes",
        HygieneCheck.IsFalse => "Must be no",
        HygieneCheck.PrimaryTeamMarked => "Primary team marked",
        HygieneCheck.StoryPointsMatchTeams => "Matches the teams total",
        HygieneCheck.ParentsExist => "All parents exist",
        _ => check.ToString()
    };

    public static bool NeedsWords(HygieneCheck check) =>
        check is HygieneCheck.ContainsWords or HygieneCheck.NotOnlyWords;

    public static bool NeedsNumber(HygieneCheck check, HygieneFieldKind kind) =>
        kind == HygieneFieldKind.Number && check is HygieneCheck.NotGreaterThan or HygieneCheck.NotLessThan;

    public static bool NeedsDate(HygieneCheck check, HygieneFieldKind kind) =>
        kind == HygieneFieldKind.Date && check is HygieneCheck.NotGreaterThan or HygieneCheck.NotLessThan;

    public static bool NeedsValues(HygieneCheck check) =>
        check is HygieneCheck.InValues or HygieneCheck.NotInValues;
}
