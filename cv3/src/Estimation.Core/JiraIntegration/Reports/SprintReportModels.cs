using Estimation.Core.JiraIntegration.Client;

namespace Estimation.Core.JiraIntegration.Reports;

public enum SprintReportState
{
    Future,

    Active,

    Closed,
}

public sealed record SprintReportWindow(
    DateTimeOffset Start,
    DateTimeOffset End,
    DateTimeOffset? CompletedAt,
    SprintReportState State,
    bool DatesFromJira,
    int? JiraSprintId,
    string? Goal)
{
    public DateTimeOffset AsOf(DateTimeOffset now) => State switch
    {
        SprintReportState.Closed => CompletedAt ?? End,
        SprintReportState.Future => Start > now ? Start : now,
        _ => now,
    };

    public DateOnly FirstDay => DateOnly.FromDateTime(Start.DateTime);

    public DateOnly LastDay => DateOnly.FromDateTime(End.AddTicks(-1).ToOffset(Start.Offset).DateTime);
}

public sealed class SprintReportIssue
{
    public required JiraIssueResponse Issue { get; init; }

    public string Key => Issue.Key;

    public bool InSprintAtStart { get; init; }

    public bool InSprintAtEnd { get; init; }

    public DateTimeOffset? AddedAt { get; init; }

    public int? PointsAtStart { get; init; }

    public int? Points { get; init; }

    public string? Status { get; init; }

    public string? StatusCategory { get; init; }

    public bool IsDone { get; init; }

    public DateTimeOffset? DoneAt { get; init; }

    public bool IsBlocked { get; init; }

    public bool IsCommitted => InSprintAtStart;

    public bool IsAdded => !InSprintAtStart && InSprintAtEnd;

    public bool IsRemoved => InSprintAtStart && !InSprintAtEnd;

    public bool IsIgnored => !InSprintAtStart && !InSprintAtEnd;

    public bool IsCompleted => InSprintAtEnd && IsDone;

    public bool IsNotCompleted => InSprintAtEnd && !IsDone;

    public bool IsUnestimated => InSprintAtEnd && Points is null;
}

public sealed record SprintReportGroup(
    string Name,
    string? Title,
    string? AvatarUrl,
    int Issues,
    int CompletedIssues,
    int UnestimatedIssues,
    int CommittedPoints,
    int AddedPoints,
    int ScopePoints,
    int CompletedPoints)
{
    public int OpenPoints => ScopePoints - CompletedPoints;

    public double? Completion =>
        ScopePoints > 0 ? (double)CompletedPoints / ScopePoints
        : Issues > 0 ? (double)CompletedIssues / Issues
        : null;
}

public sealed record SprintStatusGroup(string Status, string? Category, int Issues, int Points);

public sealed record SprintBurndownPoint(
    DateOnly Date,
    bool IsStart,
    bool IsWorkingDay,
    bool HasActual,
    int RemainingPoints,
    int RemainingIssues,
    int ScopePoints,
    int ScopeIssues,
    double IdealPoints,
    double IdealIssues);

public sealed class SprintReport
{
    public const string ToDoCategory = "new";

    public const string InProgressCategory = "indeterminate";

    public const string DoneCategory = "done";

    public required string SprintName { get; init; }

    public required SprintReportWindow Window { get; init; }

    public required DateTimeOffset AsOf { get; init; }

    public required IReadOnlyList<SprintReportIssue> Issues { get; init; }

    public int CommittedIssues { get; init; }

    public int CommittedPoints { get; init; }

    public int AddedIssues { get; init; }

    public int AddedPoints { get; init; }

    public int RemovedIssues { get; init; }

    public int RemovedPoints { get; init; }

    public int IgnoredIssues { get; init; }

    public IReadOnlyList<string> IgnoredKeys { get; init; } = [];

    public int ScopeIssues { get; init; }

    public int ScopePoints { get; init; }

    public int CompletedIssues { get; init; }

    public int CompletedPoints { get; init; }

    public int CompletedCommittedIssues { get; init; }

    public int CompletedCommittedPoints { get; init; }

    public int NotCompletedIssues { get; init; }

    public int NotCompletedPoints { get; init; }

    public int UnestimatedIssues { get; init; }

    public int BlockedIssues { get; init; }

    public int WorkingDays { get; init; }

    public int WorkingDaysElapsed { get; init; }

    public required IReadOnlyList<SprintBurndownPoint> Burndown { get; init; }

    public required IReadOnlyList<SprintReportGroup> ByAssignee { get; init; }

    public required IReadOnlyList<SprintReportGroup> ByType { get; init; }

    public required IReadOnlyList<SprintReportGroup> ByFeature { get; set; }

    public required IReadOnlyList<SprintStatusGroup> ByStatus { get; init; }

    public bool HasEstimates => ScopePoints > 0 || CommittedPoints > 0;

    public double? CommitmentCompletion =>
        CommittedPoints > 0 ? (double)CompletedCommittedPoints / CommittedPoints
        : CommittedIssues > 0 ? (double)CompletedCommittedIssues / CommittedIssues
        : null;

    public double? ScopeCompletion =>
        ScopePoints > 0 ? (double)CompletedPoints / ScopePoints
        : ScopeIssues > 0 ? (double)CompletedIssues / ScopeIssues
        : null;

    public double? ScopeChange =>
        CommittedPoints > 0 ? (double)AddedPoints / CommittedPoints
        : CommittedIssues > 0 ? (double)AddedIssues / CommittedIssues
        : null;

    public int IssuesInCategory(string category) =>
        ByStatus.Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase)).Sum(s => s.Issues);

    public int PointsInCategory(string category) =>
        ByStatus.Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase)).Sum(s => s.Points);
}

public sealed class SprintReportInput
{
    public required string SprintName { get; init; }

    public required SprintReportWindow Window { get; init; }

    public required DateTimeOffset Now { get; init; }

    public required IReadOnlyList<JiraIssueResponse> Issues { get; init; }

    public IReadOnlyDictionary<string, string> StatusCategories { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> StoryPointsFieldNames { get; init; } = ["Story Points"];

    public string? StoryPointsFieldId { get; init; }

    public IReadOnlyCollection<string> SprintFieldNames { get; init; } = ["Sprint"];

    public string? SprintFieldId { get; init; }
}
