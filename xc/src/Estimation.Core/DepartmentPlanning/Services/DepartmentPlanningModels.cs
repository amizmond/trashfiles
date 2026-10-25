using Estimation.Core.Capacity.Services;
using Estimation.Core.Train.Models;

namespace Estimation.Core.DepartmentPlanning.Services;

public sealed record DepartmentPlanningScope(int? DepartmentId, IReadOnlyCollection<int> ArtIds, int PiId, bool IncludeIpSprints = false);

public sealed record PlanningArt(
    int Id,
    string Name,
    string Abbreviation,
    int? DepartmentId,
    string? DepartmentName,
    ArtPrioritization? Level,
    ArtOrderMode OrderMode,
    IReadOnlyList<int> TeamIds,
    IReadOnlyList<ArtOrderEntry> Order)
{
    public IReadOnlyDictionary<int, ArtOrderEntry> OrderByItem { get; } =
        Order.DistinctBy(e => e.ItemId).ToDictionary(e => e.ItemId);
}

public sealed record PlanningTeam(
    int Id,
    string Name,
    IReadOnlyList<int> ArtIds,
    decimal StoryPointsPerDay,
    double CapacityDays,
    decimal CapacitySp,
    int MemberCount,
    string WindowMode);

public sealed record PlanningFeatureTeam(
    int TeamId,
    int DemandSp,
    bool IsIncluded,
    IReadOnlyList<ArtLineRef> BelowArtLine,
    int BoardPosition,
    int? ArtRank,
    decimal ReservedSp)
{
    public bool IsPlanned => IsIncluded && BelowArtLine.Count == 0;
}

public sealed record PlanningFeature(
    int Id,
    string? JiraId,
    string Title,
    string? Status,
    string? RagStatus,
    string? RagExplain,
    int? Ranking,
    int? PiId,
    string? PiName,
    int? BusinessOutcomeId,
    int? PortfolioEpicId,
    DateTime? TargetStart,
    DateTime? TargetEnd,
    string? FundingStatus,
    IReadOnlyList<PlanningFeatureTeam> Teams)
{
    public int DependencyCount { get; init; }

    public int DependenciesAtRisk { get; init; }

    public int HygieneFailures { get; init; }

    public PlanningStatusClass StatusClass => PlanningStatus.Classify(Status);

    public bool IsDone => StatusClass == PlanningStatusClass.Done;

    public int DemandSp => Teams.Sum(t => t.DemandSp);

    public int PlannedSp => Teams.Where(t => t.IsPlanned).Sum(t => t.DemandSp);

    public int UnplannedSp => DemandSp - PlannedSp;
}

public sealed record PlanningOutcome(
    int Id,
    string? JiraId,
    string Summary,
    string? Status,
    string? RagStatus,
    string? RagExplain,
    int? PortfolioEpicId,
    DateTime? TargetStart,
    DateTime? TargetEnd);

public sealed record PlanningEpic(
    int Id,
    string? JiraId,
    string Summary,
    string? Status,
    string? RagStatus,
    string? FundingStatus,
    DateTime? TargetStart,
    DateTime? TargetEnd,
    IReadOnlyList<int> StrategicObjectiveIds);

public sealed record PlanningObjective(
    int Id,
    string? JiraId,
    string Summary,
    string? Status,
    string? RagStatus,
    IReadOnlyList<int> ArtIds);

public sealed record DepartmentPlanningData(
    DepartmentPlanningScope Scope,
    int PiId,
    string PiName,
    bool PiLocked,
    DateTime? PiStart,
    DateTime? PiEnd,
    IReadOnlyList<PlanningArt> Arts,
    IReadOnlyList<PlanningTeam> Teams,
    IReadOnlyList<PlanningFeature> Features,
    IReadOnlyList<PlanningObjective> Objectives,
    IReadOnlyList<PlanningEpic> Epics,
    IReadOnlyList<PlanningOutcome> Outcomes)
{
    public bool DependenciesLoaded { get; init; } = true;

    public bool HygieneLoaded { get; init; } = true;

    public DateTime LoadedUtc { get; init; } = DateTime.UtcNow;
}

public sealed record PlanningItemDetails(
    string? Description,
    string? AcceptanceCriteria,
    string? Labels,
    string? Components,
    string? FixVersions,
    DateTime? JiraUpdated);
