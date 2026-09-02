namespace Estimation.Core.Features.Hygiene.Models;

/// <summary>
/// The checks a rule can make. Stored as text, so add new members at the end and never rename one
/// that has been saved.
/// </summary>
public enum HygieneCheck
{
    /// <summary>Text with at least one letter or digit, a number, a date, a reference or a choice that is set.</summary>
    NotEmpty,

    /// <summary>Text contains the given phrases, all of them or any of them.</summary>
    ContainsWords,

    /// <summary>Text has at least N words besides the given phrases, markup and symbols not counted.</summary>
    NotOnlyWords,

    /// <summary>A number is at most the limit, or a date is not after the limit. Empty values pass.</summary>
    NotGreaterThan,

    /// <summary>A number is at least the limit, or a date is not before the limit. Empty values pass.</summary>
    NotLessThan,

    /// <summary>A choice is one of the listed values. "(empty)" may be listed.</summary>
    InValues,

    /// <summary>A choice is none of the listed values. "(empty)" may be listed.</summary>
    NotInValues,

    /// <summary>A flag is set.</summary>
    IsTrue,

    /// <summary>A flag is not set.</summary>
    IsFalse
}

public static class HygieneChecks
{
    private static readonly IReadOnlyList<HygieneCheck> TextChecks =
        [HygieneCheck.NotEmpty, HygieneCheck.ContainsWords, HygieneCheck.NotOnlyWords];

    private static readonly IReadOnlyList<HygieneCheck> RangeChecks =
        [HygieneCheck.NotEmpty, HygieneCheck.NotGreaterThan, HygieneCheck.NotLessThan];

    private static readonly IReadOnlyList<HygieneCheck> ReferenceChecks = [HygieneCheck.NotEmpty];

    private static readonly IReadOnlyList<HygieneCheck> ChoiceChecks =
        [HygieneCheck.NotEmpty, HygieneCheck.InValues, HygieneCheck.NotInValues];

    private static readonly IReadOnlyList<HygieneCheck> FlagChecks = [HygieneCheck.IsTrue, HygieneCheck.IsFalse];

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

    public static bool IsAllowed(HygieneField field, HygieneCheck check) =>
        AllowedFor(HygieneFieldCatalog.KindOf(field)).Contains(check);

    public static string DisplayName(HygieneCheck check, HygieneFieldKind kind) => check switch
    {
        HygieneCheck.NotEmpty => "Not empty",
        HygieneCheck.ContainsWords => "Contains words",
        HygieneCheck.NotOnlyWords => "Not only these words",
        HygieneCheck.NotGreaterThan => kind == HygieneFieldKind.Date ? "Not after" : "Not greater than",
        HygieneCheck.NotLessThan => kind == HygieneFieldKind.Date ? "Not before" : "Not less than",
        HygieneCheck.InValues => "In values",
        HygieneCheck.NotInValues => "Not in values",
        HygieneCheck.IsTrue => "Must be yes",
        HygieneCheck.IsFalse => "Must be no",
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
