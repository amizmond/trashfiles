namespace Estimation.Core.Features.Models;

public record FeatureFilterRef(string JiraId, string? Summary);

public interface IFeatureFilterRow
{
    string? JiraId { get; }
    string? Name { get; }
    string? Summary { get; }

    string? BusinessOutcomeJiraId { get; }
    string? BusinessOutcomeSummary { get; }

    string? PortfolioEpicJiraId { get; }
    string? PortfolioEpicSummary { get; }

    IReadOnlyList<FeatureFilterRef> StrategicObjectives { get; }

    string? Status { get; }
    string? RagStatus { get; }
    string? Labels { get; }
    string? RequirementStatus { get; }
    string? TechnicalApproval { get; }
    string? FundingStatus { get; }

    int? HygieneFailureCount => null;
}
