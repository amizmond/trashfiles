namespace Estimation.Core.JiraIntegration.Models;

public enum SprintIssueOutcome
{
    /// <summary>In the sprint at start and done at close.</summary>
    CompletedFromCommitted = 0,

    /// <summary>Added after sprint start and done at close.</summary>
    CompletedAdded = 1,

    /// <summary>In the sprint at start, still in it at close, not done.</summary>
    NotCompletedFromCommitted = 2,

    /// <summary>Added after sprint start, still in it at close, not done.</summary>
    NotCompletedAdded = 3,

    /// <summary>In the sprint at start but no longer in it at close (descoped, deleted, or moved).</summary>
    Removed = 4,
}
