namespace Estimation.Core.Models;

/// <summary>
/// Overall PI capacity calculation result.
/// </summary>
public class PiCapacityResult
{
    public List<FeatureCapacityResult> FeatureResults { get; set; } = new();
    public PiCapacitySummary Summary { get; set; } = new();
}

/// <summary>
/// Summary of PI capacity across all features.
/// </summary>
public class PiCapacitySummary
{
    public int TotalFeatures { get; set; }
    public int FeaturesFullyReserved { get; set; }
    public int FeaturesPartiallyReserved { get; set; }
    public int FeaturesNotReserved { get; set; }
    public int TotalRequiredCapacity { get; set; }
    public int TotalReservedCapacity { get; set; }
    public int TotalUnreservedCapacity { get; set; }
}

/// <summary>
/// Capacity calculation result for a single feature.
/// </summary>
public class FeatureCapacityResult
{
    public int FeatureId { get; set; }
    public string? FeatureName { get; set; }
    public string? JiraId { get; set; }
    public int? Ranking { get; set; }
    public bool IsFullyReserved { get; set; }
    public List<TechnologyStackCapacityAllocation> TechnologyStackAllocations { get; set; } = new();
    public int TotalRequired { get; set; }
    public int TotalReserved { get; set; }
}

/// <summary>
/// Capacity allocation for a single TechnologyStack requirement of a feature.
/// </summary>
public class TechnologyStackCapacityAllocation
{
    public int TechnologyStackId { get; set; }
    public string TechnologyStackName { get; set; } = null!;
    public int Required { get; set; }
    public int Reserved { get; set; }
    public bool IsReserved => Reserved >= Required;
    public string? MatchMethod { get; set; } // "AssignedTeam", "MatchingTeam"
    public List<TeamAllocation> TeamAllocations { get; set; } = new();
}

/// <summary>
/// How much capacity was reserved from a specific team.
/// </summary>
public class TeamAllocation
{
    public int TeamId { get; set; }
    public string TeamName { get; set; } = null!;
    public int AllocatedCapacity { get; set; }
    public int RemainingCapacity { get; set; }
}
