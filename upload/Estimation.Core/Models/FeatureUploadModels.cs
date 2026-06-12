namespace Estimation.Core.Models;

/// <summary>
/// Logical columns that can be included when exporting / importing features.
/// "TechStack" represents the whole block of per-tech-stack effort columns behind a single
/// switch (mirrors how "Skills" collapses the per-skill columns on the resources export).
/// </summary>
public enum FeatureUploadColumn
{
    ProjectKey,
    JiraId,
    FeatureName,
    Summary,
    Ranking,
    Description,
    AcceptanceCriteria,
    BusinessOutcome,
    Labels,
    Team,
    Status,
    RequirementStatus,
    Comments,
    TargetStart,
    TargetEnd,
    DateExpected,
    StoryPoints,
    RagExplain,
    Dependencies,
    TechStack
}

/// <summary>
/// State of a single column switch in the feature export / import picker dialog.
/// </summary>
public class FeatureColumnState
{
    public FeatureUploadColumn Column { get; set; }
    public string Label { get; set; } = "";

    /// <summary>Whether the column is currently selected (will be exported / applied on import).</summary>
    public bool Selected { get; set; } = true;

    /// <summary>Whether the switch can be toggled by the user. Disabled when a column is missing from an upload.</summary>
    public bool Editable { get; set; } = true;

    /// <summary>Main identity columns are always selected and cannot be turned off.</summary>
    public bool IsMain { get; set; }
}

/// <summary>
/// The set of columns the user chose to include for an export or an import.
/// </summary>
public class FeatureUploadColumnSelection
{
    public HashSet<FeatureUploadColumn> Columns { get; set; } = new();

    public bool Includes(FeatureUploadColumn column) => Columns.Contains(column);

    public static FeatureUploadColumnSelection All() => new()
    {
        Columns = Enum.GetValues<FeatureUploadColumn>().ToHashSet()
    };
}

/// <summary>
/// Filters applied to a feature export so the file matches what the user currently sees in the
/// list. All members are optional; null/empty means "no constraint". The list applies these
/// in-memory, so the export receives the already-resolved set of feature ids.
/// </summary>
public class FeatureExportFilter
{
    /// <summary>Feature ids to export, in display order. Null means "all features".</summary>
    public IReadOnlyList<int>? FeatureIds { get; set; }
}

/// <summary>
/// A tech-stack effort cell parsed from an upload, with the existing DB value for change detection.
/// </summary>
public class FeatureTechStackUploadItem
{
    public int TechStackId { get; set; }
    public string TechStackName { get; set; } = null!;
    public string? RawValue { get; set; }
    public int? NewEffort { get; set; }
    public int? OldEffort { get; set; }
    public bool IsChanged { get; set; }
    public bool IsRemoved { get; set; }

    /// <summary>Set when the raw cell could not be parsed as a whole number.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// One parsed feature row: the uploaded values for the selected columns, the matched DB record's
/// current values, and per-field "changed" flags used to render the preview and to decide whether
/// a record actually needs to be written.
/// </summary>
public class FeatureUploadRow
{
    public int? ExistingFeatureId { get; set; }
    public bool IsNew { get; set; }

    /// <summary>Which columns were applied (present in the file AND selected by the user).</summary>
    public FeatureUploadColumnSelection AppliedColumns { get; set; } = new();

    public string? ProjectKey { get; set; }
    public string? CurrentProjectKey { get; set; }
    public bool ProjectKeyChanged { get; set; }

    /// <summary>True when the existing DB record already has a Project Key, which the upload must not change.</summary>
    public bool ProjectKeyLocked { get; set; }

    public string? JiraId { get; set; }
    public string? CurrentJiraId { get; set; }

    public string? FeatureName { get; set; }
    public string? CurrentFeatureName { get; set; }
    public bool FeatureNameChanged { get; set; }

    public string? Summary { get; set; }
    public string? CurrentSummary { get; set; }
    public bool SummaryChanged { get; set; }

    public string? RankingRaw { get; set; }
    public int? Ranking { get; set; }
    public int? CurrentRanking { get; set; }
    public bool RankingChanged { get; set; }

    public string? Description { get; set; }
    public string? CurrentDescription { get; set; }
    public bool DescriptionChanged { get; set; }

    public string? AcceptanceCriteria { get; set; }
    public string? CurrentAcceptanceCriteria { get; set; }
    public bool AcceptanceCriteriaChanged { get; set; }

    public string? BusinessOutcome { get; set; }
    public int? BusinessOutcomeId { get; set; }
    public string? CurrentBusinessOutcome { get; set; }
    public bool BusinessOutcomeChanged { get; set; }

    public string? Labels { get; set; }
    public string? CurrentLabels { get; set; }
    public bool LabelsChanged { get; set; }

    /// <summary>Raw uploaded Team cell (one or more team names separated by ';').</summary>
    public string? Team { get; set; }
    /// <summary>Resolved ids for the uploaded team names that matched an existing team.</summary>
    public List<int> TeamIds { get; set; } = new();
    public string? CurrentTeam { get; set; }
    public bool TeamChanged { get; set; }

    public string? Status { get; set; }
    public string? CurrentStatus { get; set; }
    public bool StatusChanged { get; set; }

    /// <summary>
    /// Requirement Status name (a normalized lookup, not free text on the feature). An unmatched
    /// name auto-creates a new RequirementStatus on save; an empty cell clears the assignment.
    /// </summary>
    public string? RequirementStatus { get; set; }
    public string? CurrentRequirementStatus { get; set; }
    public bool RequirementStatusChanged { get; set; }

    public string? Comments { get; set; }
    public string? CurrentComments { get; set; }
    public bool CommentsChanged { get; set; }

    public string? TargetStartRaw { get; set; }
    public DateTime? TargetStart { get; set; }
    public DateTime? CurrentTargetStart { get; set; }
    public bool TargetStartChanged { get; set; }

    public string? TargetEndRaw { get; set; }
    public DateTime? TargetEnd { get; set; }
    public DateTime? CurrentTargetEnd { get; set; }
    public bool TargetEndChanged { get; set; }

    public string? DateExpectedRaw { get; set; }
    public DateTime? DateExpected { get; set; }
    public DateTime? CurrentDateExpected { get; set; }
    public bool DateExpectedChanged { get; set; }

    public string? StoryPointsRaw { get; set; }
    public int? StoryPoints { get; set; }
    public int? CurrentStoryPoints { get; set; }
    public bool StoryPointsChanged { get; set; }

    public string? RagExplain { get; set; }
    public string? CurrentRagExplain { get; set; }
    public bool RagExplainChanged { get; set; }

    public string? Dependencies { get; set; }
    public string? CurrentDependencies { get; set; }
    public bool DependenciesChanged { get; set; }

    public List<FeatureTechStackUploadItem> TechStacks { get; set; } = new();

    // --- Validation / Jira state (Jira state is populated by the preview page) ---

    /// <summary>Field-level validation errors keyed by property name (or "TS:&lt;name&gt;").</summary>
    public Dictionary<string, string> ValidationErrors { get; set; } = new();

    public string? Error { get; set; }
    public string? DetailedError { get; set; }

    public bool IsValid => Error is null && ValidationErrors.Count == 0;

    /// <summary>
    /// Whether the row carries any change to persist to the database. New rows always count.
    /// Unchanged rows are shown as "No change" and skipped on save (matching the resources upload).
    /// </summary>
    public bool HasDbChanges => IsNew
        || FeatureNameChanged
        || SummaryChanged
        || RankingChanged
        || DescriptionChanged
        || AcceptanceCriteriaChanged
        || BusinessOutcomeChanged
        || LabelsChanged
        || TeamChanged
        || StatusChanged
        || RequirementStatusChanged
        || CommentsChanged
        || ProjectKeyChanged
        || TargetStartChanged
        || TargetEndChanged
        || DateExpectedChanged
        || StoryPointsChanged
        || RagExplainChanged
        || DependenciesChanged
        || TechStacks.Any(ts => ts.IsChanged || ts.IsRemoved);
}
