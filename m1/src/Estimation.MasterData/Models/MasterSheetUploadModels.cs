namespace Estimation.MasterData.Models;

public class Result
{
    public bool IsSuccess { get; protected set; }
    public string? ErrorMessage { get; protected set; }
    public string? SuccessMessage { get; protected set; }
    public static Result Success(string sucessMessage = "") => new Result
    {
        IsSuccess = true,
        SuccessMessage = sucessMessage
    };

    public static Result Failure(string errorMessage) => new Result
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}

public class FeatureMasterSheet
{
    public int? ExistingFeatureId { get; set; }
    public string? JiraId { get; set; }
    public string ProjectKey { get; set; } = string.Empty;
    public string? FeatureName { get; set; }
    public string? FeatureSummary { get; set; }
    public int? BusinessOutcomeId { get; set; }
    public DateTime? TargetEndDate { get; set; }
    public int? PiId { get; set; }
    public int? TeamId { get; set; }
    public string? Dependencies { get; set; }
    public int? ConfidencePercentage { get; set; }
    public bool ConnectToJira { get; set; }
    public Dictionary<int, int?> TechStackEfforts { get; set; } = new();
    public string? FeatureLabel { get; set; }
    public int? StoryPoints { get; set; }
}

public class MasterSheetExportRow
{
    /// <summary>
    /// Exsting Feature Table ID
    /// </summary>
    public int MasterId { get; set; }
    public string? ProjectKey { get; set; }
    /// <summary>
    /// Strategic Objective Name
    /// </summary>
    public string? ProgramJiraId { get; set; }
    public string? ProgramName { get; set; }
    public string? EpicJiraId { get; set; }
    public string? EpicName { get; set; }
    public int? EpicRanking { get; set; }
    public string? UnfundedOption { set; get; }
    public string? BusinessOutcomeJiraId { get; set; }
    public string? BusinessOutcome { get; set; }
    public string? PIName { get; set; }
    public string? FeatureJiraId { get; set; }
    public string? FeatureName { get; set; }
    public string? FeatureLabels { get; set; }
    public string? FeatureSummary { get; set; }
    public string? TeamName { get; set; }
    public int? EstimatedDays { get; set; }
    public int? DevEstimates { get; set; }
    public DateTime? TargetEndDate { get; set; }
    public string? Dependencies { get; set; }
    public int? ConfidencePercentage { get; set; }
    public string? L6Owner { get; set; }
    public List<int?>? TechStacks { get; set; }
}

public class MasterSheetExportLookups
{
    public List<string> ProjectKeys { get; set; } = [];
    public List<string> TeamNames { get; set; } = [];
    public List<string> UnfundedOptions { get; set; } = [];
    public List<string> PIOptions { get; set; } = [];
    public List<string> TechStackNames { get; set; } = [];
    public List<(string JiraId, string Name)> BusinessOutcomeOptions { get; set; } = new();
    public List<(string JiraId, string Name)> EpicOptions { get; set; } = new();
    public List<(string JiraId, string Name)> ProgramOptions { get; set; } = new();
}

public class MasterSheetParseResult
{
    public List<MasterSheetUploadRow> Rows { get; set; } = new();
    public List<string> TechStackNames { get; set; } = new();
    public MasterSheetUploadColumnSelection AppliedColumns { get; set; } = new();
}

public enum MasterSheetUploadColumn
{
    MasterId,
    ProjectKey,
    ProgramJiraId,
    ProgramName,
    EpicJiraId,
    EpicName,
    EpicRanking,
    UnfundedOption,
    BusinessOutcomeJiraId,
    BusinessOutcome,
    PI,
    FeatureJiraId,
    FeatureName,
    FeatureLabels,
    FeatureSummary,
    Team,
    EstimatedDays,
    DevEstimates,
    TargetEndDate,
    Dependencies,
    ConfidencePercentage,
    L6Owner,
    TechStacks
}

public class MasterSheetUploadColumnSelection
{
    public HashSet<MasterSheetUploadColumn> Columns { get; set; } = new();

    public bool Includes(MasterSheetUploadColumn column) => Columns.Contains(column);

    public static MasterSheetUploadColumnSelection All() => new()
    {
        Columns = Enum.GetValues<MasterSheetUploadColumn>().ToHashSet()
    };
}

public class MasterSheetColumnState
{
    public MasterSheetUploadColumn Column { get; set; }

    public string Label { get; set; } = "";

    public bool Selected { get; set; } = true;

    public bool Editable { get; set; } = true;

    public bool IsMain { get; set; }
}

public class MasterSheetTechStackUploadItem
{
    public int TechStackId { get; set; }
    public string TechStackName { get; set; } = null!;
    public string? RawValue { get; set; }
    public int? NewEffort { get; set; }
    public int? OldEffort { get; set; }
    public bool IsChanged { get; set; }
    public bool IsRemoved { get; set; }

    public string? Error { get; set; }
}

public class MasterSheetUploadRow
{
    public int? ExistingFeatureId { get; set; }
    public bool IsNew { get; set; }

    public MasterSheetUploadColumnSelection AppliedColumns { get; set; } = new();

    public string? ProjectKey { get; set; }
    public string? CurrentProjectKey { get; set; }
    public bool ProjectKeyChanged { get; set; }
    public bool ProjectKeyLocked { get; set; }

    public string? JiraId { get; set; }
    public string? CurrentJiraId { get; set; }

    public string? FeatureName { get; set; }
    public string? CurrentFeatureName { get; set; }
    public bool FeatureNameChanged { get; set; }

    public string? Summary { get; set; }
    public string? CurrentSummary { get; set; }
    public bool SummaryChanged { get; set; }

    public string? ConfidencePercentageRaw { get; set; }
    public int? ConfidencePercentage { get; set; }
    public int? CurrentConfidencePercentage { get; set; }
    public bool ConfidencePercentageChanged { get; set; }

    public string? BusinessOutcome { get; set; }
    public int? BusinessOutcomeId { get; set; }
    public string? CurrentBusinessOutcome { get; set; }
    public bool BusinessOutcomeChanged { get; set; }

    public string? Labels { get; set; }
    public string? CurrentLabels { get; set; }
    public bool LabelsChanged { get; set; }

    public string? Team { get; set; }
    public List<int> TeamIds { get; set; } = new();
    public string? CurrentTeam { get; set; }
    public bool TeamChanged { get; set; }

    public string? PIIdRaw { get; set; }
    public int? PIId { get; set; }
    public int? CurrentPIId { get; set; }
    public bool PIIdChanged { get; set; }

    public string? TargetEndRaw { get; set; }
    public DateTime? TargetEnd { get; set; }
    public DateTime? CurrentTargetEnd { get; set; }
    public bool TargetEndChanged { get; set; }

    public string? StoryPointsRaw { get; set; }
    public int? StoryPoints { get; set; }
    public int? CurrentStoryPoints { get; set; }
    public bool StoryPointsChanged { get; set; }

    public string? EpicRankingRaw { get; set; }
    public int? EpicRanking { get; set; }
    public int? CurrentEpicRanking { get; set; }
    public bool EpicRankingChanged { get; set; }

    public string? Dependencies { get; set; }
    public string? CurrentDependencies { get; set; }
    public bool DependenciesChanged { get; set; }

    public string? L6Owner { get; set; }
    public string? CurrentL6Owner { get; set; }
    public bool L6OwnerChanged { get; set; }

    public string? UnfundedOption { get; set; }
    public int? UnfundedOptionId { get; set; }
    public string? CurrentUnfundedOption { get; set; }
    public bool UnfundedOptionChanged { get; set; }

    public List<MasterSheetTechStackUploadItem> TechStacks { get; set; } = new();

    public string? ArtMove { get; set; }

    public Dictionary<string, string> ValidationErrors { get; set; } = new();

    public string? Error { get; set; }
    public string? DetailedError { get; set; }

    public bool IsValid => Error is null && ValidationErrors.Count == 0;

    public bool HasDbChanges => IsNew
        || FeatureNameChanged
        || SummaryChanged
        || ConfidencePercentageChanged
        || BusinessOutcomeChanged
        || LabelsChanged
        || TeamChanged
        || ProjectKeyChanged
        || TargetEndChanged
        || StoryPointsChanged
        || DependenciesChanged
        || L6OwnerChanged
        || EpicRankingChanged
        || UnfundedOptionChanged
        || TechStacks.Any(ts => ts.IsChanged || ts.IsRemoved);
}

public class MasterSheetUploadData
{
    public int? ExistingFeatureId { get; set; }
    public string? JiraId { get; set; }
    public string? ProjectKey { get; set; }
    public string? FeatureName { get; set; }
    public string? Summary { get; set; }
    public int? Ranking { get; set; }
    public int? BusinessOutcomeId { get; set; }
    public string? Labels { get; set; }
    public List<int> TeamIds { get; set; } = new();
    public DateTime? TargetEnd { get; set; }
    public int? StoryPoints { get; set; }
    public string? Dependencies { get; set; }
    public bool ConnectToJira { get; set; }

    public MasterSheetUploadColumnSelection AppliedColumns { get; set; } = MasterSheetUploadColumnSelection.All();

    public Dictionary<int, int?> TechStackEfforts { get; set; } = new();

    public const string DefaultStatus = "Backlog";
}
