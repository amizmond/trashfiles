using Estimation.Core.JiraIntegration.Client;

namespace Estimation.Core.Administration.Models;

public enum HousekeepingTable
{
    StrategicObjectives,
    PortfolioEpics,
    BusinessOutcomes,
    Features,
}

public sealed record HousekeepingItem(
    HousekeepingTable Table,
    int Id,
    string JiraKey,
    string? CurrentJiraKey,
    string Summary,
    string IssueType,
    string? Status,
    HousekeepingTable? IssueTypeTable,
    bool ExistsInIssueTypeTable)
{
    public string ExpectedIssueType => HousekeepingTables.ExpectedIssueType(Table);
}

public sealed record HousekeepingItemKey(HousekeepingTable Table, int Id, string JiraKey);

public sealed record HousekeepingRemoveResult(int Removed, int Skipped);

public sealed record HousekeepingMovedKey(string StoredKey, string CurrentKey);

public sealed record HousekeepingCheckProgress(int Checked, int Total);

public sealed record HousekeepingCheckResult(
    HousekeepingTable Table,
    int Total,
    int Checked,
    IReadOnlyList<HousekeepingItem> Items,
    IReadOnlyList<string> NotFoundKeys,
    IReadOnlyList<HousekeepingMovedKey> MovedKeys,
    bool Cancelled,
    string? Error)
{
    public bool Completed => !Cancelled && Error is null;
}

public static class HousekeepingTables
{
    public static IReadOnlyList<HousekeepingTable> All { get; } =
    [
        HousekeepingTable.StrategicObjectives,
        HousekeepingTable.PortfolioEpics,
        HousekeepingTable.BusinessOutcomes,
        HousekeepingTable.Features,
    ];

    public static string ExpectedIssueType(HousekeepingTable table) => table switch
    {
        HousekeepingTable.StrategicObjectives => JiraIssueTypes.StrategicObjective,
        HousekeepingTable.PortfolioEpics => JiraIssueTypes.PortfolioEpic,
        HousekeepingTable.BusinessOutcomes => JiraIssueTypes.BusinessOutcome,
        HousekeepingTable.Features => JiraIssueTypes.Feature,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    public static HousekeepingTable? ForIssueType(string? issueType)
    {
        if (string.IsNullOrWhiteSpace(issueType))
        {
            return null;
        }

        var trimmed = issueType.Trim();
        return All.Cast<HousekeepingTable?>()
            .FirstOrDefault(t => string.Equals(ExpectedIssueType(t!.Value), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsWrongIssueType(HousekeepingTable table, string? issueType) =>
        !string.IsNullOrWhiteSpace(issueType)
        && !string.Equals(issueType.Trim(), ExpectedIssueType(table), StringComparison.OrdinalIgnoreCase);
}
