namespace Estimation.Core.Features.Hygiene.Models;

/// <summary>
/// The feature fields a hygiene rule can check. Stored as text, so add new members at the end and
/// never rename one that has been saved.
/// </summary>
public enum HygieneField
{
    Summary,
    Name,
    Description,
    AcceptanceCriteria,
    RagExplain,
    Dependencies,
    NavigatorId,
    Labels,

    StoryPoints,
    Ranking,
    ConfidencePercentage,

    TargetStart,
    TargetEnd,
    DateExpected,

    BusinessOutcome,
    Pi,
    PiObjective,
    Teams,

    RequirementStatus,
    TechnicalApproval,
    FundingStatus,
    Status,

    ExternalDependencies,
    IsLinkedToJira
}

/// <summary>The kind of a field decides which checks apply and how their parameters are edited.</summary>
public enum HygieneFieldKind
{
    Text,
    Number,
    Date,
    Reference,
    Choice,
    Flag
}

public sealed record HygieneFieldInfo(HygieneField Field, HygieneFieldKind Kind, string DisplayName);

public static class HygieneFieldCatalog
{
    public static readonly IReadOnlyList<HygieneFieldInfo> All =
    [
        new(HygieneField.Summary, HygieneFieldKind.Text, "Summary"),
        new(HygieneField.Name, HygieneFieldKind.Text, "Feature name"),
        new(HygieneField.Description, HygieneFieldKind.Text, "Description"),
        new(HygieneField.AcceptanceCriteria, HygieneFieldKind.Text, "Acceptance criteria"),
        new(HygieneField.RagExplain, HygieneFieldKind.Text, "RAG explain"),
        new(HygieneField.Dependencies, HygieneFieldKind.Text, "Dependencies"),
        new(HygieneField.NavigatorId, HygieneFieldKind.Text, "Navigator ID"),
        new(HygieneField.Labels, HygieneFieldKind.Text, "Labels"),

        new(HygieneField.StoryPoints, HygieneFieldKind.Number, "Story points"),
        new(HygieneField.Ranking, HygieneFieldKind.Number, "Ranking"),
        new(HygieneField.ConfidencePercentage, HygieneFieldKind.Number, "Confidence %"),

        new(HygieneField.TargetStart, HygieneFieldKind.Date, "Target start"),
        new(HygieneField.TargetEnd, HygieneFieldKind.Date, "Target end"),
        new(HygieneField.DateExpected, HygieneFieldKind.Date, "Date expected"),

        new(HygieneField.BusinessOutcome, HygieneFieldKind.Reference, "Business outcome"),
        new(HygieneField.Pi, HygieneFieldKind.Reference, "PI"),
        new(HygieneField.PiObjective, HygieneFieldKind.Reference, "PI objective"),
        new(HygieneField.Teams, HygieneFieldKind.Reference, "Teams"),

        new(HygieneField.RequirementStatus, HygieneFieldKind.Choice, "Requirement status"),
        new(HygieneField.TechnicalApproval, HygieneFieldKind.Choice, "Technical approval"),
        new(HygieneField.FundingStatus, HygieneFieldKind.Choice, "Funding status"),
        new(HygieneField.Status, HygieneFieldKind.Choice, "Jira status"),

        new(HygieneField.ExternalDependencies, HygieneFieldKind.Flag, "External dependencies"),
        new(HygieneField.IsLinkedToJira, HygieneFieldKind.Flag, "Linked to Jira")
    ];

    private static readonly Dictionary<HygieneField, HygieneFieldInfo> ByField = All.ToDictionary(i => i.Field);

    public static HygieneFieldInfo Info(HygieneField field) =>
        ByField.TryGetValue(field, out var info)
            ? info
            : new HygieneFieldInfo(field, HygieneFieldKind.Text, field.ToString());

    public static HygieneFieldKind KindOf(HygieneField field) => Info(field).Kind;

    public static string DisplayName(HygieneField field) => Info(field).DisplayName;
}
