namespace Estimation.Core.Features.Models;

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
    BusinessOutcomeName,
    PortfolioEpic,
    PortfolioEpicName,
    StrategicObjective,
    StrategicObjectiveName,
    Labels,
    Team,
    Status,
    RequirementStatus,
    TechnicalApproval,
    FundingStatus,
    PiObjective,
    Pi,
    Comments,
    TargetStart,
    TargetEnd,
    DateExpected,
    StoryPoints,
    RagExplain,
    Dependencies,
    ExternalDependencies,
    TechStack,
    TechStackEstimation,
    TeamStoryPoints,
    FixVersions,
    RagStatus
}

public class FeatureColumnState
{
    public FeatureUploadColumn Column { get; set; }
    public string Label { get; set; } = "";

    public bool Selected { get; set; } = true;

    public bool Editable { get; set; } = true;

    public bool IsMain { get; set; }
}

public class FeatureUploadColumnSelection
{
    public HashSet<FeatureUploadColumn> Columns { get; set; } = new();

    public bool Includes(FeatureUploadColumn column) => Columns.Contains(column);

    public static FeatureUploadColumnSelection All() => new()
    {
        Columns = Enum.GetValues<FeatureUploadColumn>().ToHashSet()
    };
}

public class FeatureExportFilter
{
    public IReadOnlyList<int>? FeatureIds { get; set; }
}

public class FeatureTechStackUploadItem
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

public class FeatureUploadRow
{
    public int? ExistingFeatureId { get; set; }
    public bool IsNew { get; set; }

    public FeatureUploadColumnSelection AppliedColumns { get; set; } = new();

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

    public string? Team { get; set; }

    public List<int> TeamIds { get; set; } = new();
    public string? CurrentTeam { get; set; }
    public bool TeamChanged { get; set; }

    public string? Status { get; set; }
    public string? CurrentStatus { get; set; }
    public bool StatusChanged { get; set; }

    public string? RequirementStatus { get; set; }
    public string? CurrentRequirementStatus { get; set; }
    public bool RequirementStatusChanged { get; set; }

    public string? TechnicalApproval { get; set; }
    public int? TechnicalApprovalId { get; set; }
    public string? CurrentTechnicalApproval { get; set; }
    public bool TechnicalApprovalChanged { get; set; }

    public string? FundingStatus { get; set; }
    public int? FundingStatusId { get; set; }
    public string? CurrentFundingStatus { get; set; }
    public bool FundingStatusChanged { get; set; }

    public string? PiObjective { get; set; }
    public string? CurrentPiObjective { get; set; }
    public bool PiObjectiveChanged { get; set; }

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

    public string? RagStatus { get; set; }
    public string? CurrentRagStatus { get; set; }
    public bool RagStatusChanged { get; set; }

    public string? Dependencies { get; set; }
    public string? CurrentDependencies { get; set; }
    public bool DependenciesChanged { get; set; }

    public string? ExternalDependenciesRaw { get; set; }
    public bool? ExternalDependencies { get; set; }
    public bool? CurrentExternalDependencies { get; set; }
    public bool ExternalDependenciesChanged { get; set; }

    public List<FeatureTechStackUploadItem> TechStacks { get; set; } = new();
    public Dictionary<string, string> ValidationErrors { get; set; } = new();

    public string? Error { get; set; }
    public string? DetailedError { get; set; }

    public bool IsValid => Error is null && ValidationErrors.Count == 0;

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
        || TechnicalApprovalChanged
        || FundingStatusChanged
        || PiObjectiveChanged
        || ProjectKeyChanged
        || TargetStartChanged
        || TargetEndChanged
        || DateExpectedChanged
        || StoryPointsChanged
        || RagExplainChanged
        || RagStatusChanged
        || DependenciesChanged
        || ExternalDependenciesChanged
        || TechStacks.Any(ts => ts.IsChanged || ts.IsRemoved);
}
