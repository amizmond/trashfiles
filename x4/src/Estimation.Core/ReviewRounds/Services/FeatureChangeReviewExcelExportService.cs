using Estimation.Core.Features.Services;
using Estimation.Core.ReviewRounds.Models;
using Estimation.Excel;

namespace Estimation.Core.ReviewRounds.Services;

public static class FeatureChangeReviewExcelExportService
{
    private const string DecisionsSheetName = "Decisions";
    private const string FieldChangesSheetName = "Field changes";
    private const string InfoSheetName = "Info";

    private const string ApprovedFillColor = "#E6F4EA";
    private const string RejectedFillColor = "#FDE8E8";
    private const string PendingFillColor = "#FFF4CE";

    public record ReviewInfo(
        string ReviewName,
        string ArtName,
        string PiName,
        string Status,
        string BaselineLabel,
        string ReviewSnapshotLabel,
        string CreatedAt,
        string? CreatedBy,
        string? CompletedAt,
        string? CompletedBy);

    public static byte[] Build(
        IReadOnlyList<FeatureChangeReviewItem> items,
        ReviewInfo info,
        Func<DateTime, string> formatTime)
    {
        var builder = new ExcelWorkbookBuilder();

        var approvedStyle = builder.GetOrCreateFillStyle(ApprovedFillColor);
        var rejectedStyle = builder.GetOrCreateFillStyle(RejectedFillColor);
        var pendingStyle = builder.GetOrCreateFillStyle(PendingFillColor);

        uint? StyleFor(FeatureChangeDecision decision) => decision switch
        {
            FeatureChangeDecision.Approved => approvedStyle,
            FeatureChangeDecision.Rejected => rejectedStyle,
            _ => pendingStyle
        };

        var decisionHeaders = new[]
        {
            "Decision", "Change", "Jira ID", "Name", "Summary", "Changed fields", "Comment", "Decided by", "Decided at"
        };

        var decisions = builder.AddSheet(DecisionsSheetName);
        decisions.SetColumnWidths(12, 12, 18, 35, 45, 40, 40, 28, 20);
        decisions.WriteColoredHeader(decisionHeaders).FreezeTopRow();
        decisions.SetAutoFilter(decisionHeaders.Length, items.Count);

        foreach (var item in items)
        {
            var changes = FeatureChangeReviewService.ParseChanges(item.ChangesJson);

            decisions.AddRow()
                .StyledText(DecisionLabel(item.Decision), StyleFor(item.Decision)!.Value)
                .Text(FeatureSnapshotDeltaService.KindLabel(item.ChangeKind))
                .Text(item.JiraId)
                .Text(item.FeatureName)
                .Text(item.Summary)
                .Text(string.Join(", ", changes.Select(c => c.Field)))
                .Text(item.Comment)
                .Text(item.DecidedBy)
                .Text(item.DecidedAt is { } decidedAt ? formatTime(decidedAt) : null);
        }

        var changeHeaders = new[] { "Jira ID", "Name", "Decision", "Field", "Baseline value", "Review value" };
        var fieldChanges = builder.AddSheet(FieldChangesSheetName);
        fieldChanges.SetColumnWidths(18, 35, 12, 22, 50, 50);

        var changeRows = items
            .SelectMany(item => FeatureChangeReviewService.ParseChanges(item.ChangesJson)
                .Select(change => (Item: item, Change: change)))
            .ToList();

        fieldChanges.WriteColoredHeader(changeHeaders).FreezeTopRow();
        fieldChanges.SetAutoFilter(changeHeaders.Length, changeRows.Count);

        foreach (var (item, change) in changeRows)
        {
            fieldChanges.AddRow()
                .Text(item.JiraId)
                .Text(item.FeatureName)
                .StyledText(DecisionLabel(item.Decision), StyleFor(item.Decision)!.Value)
                .Text(change.Field)
                .Text(change.OldValue)
                .Text(change.NewValue);
        }

        var infoRows = new (string Field, string? Text, int? Number)[]
        {
            ("Review", info.ReviewName, null),
            ("ART", info.ArtName, null),
            ("PI", info.PiName, null),
            ("Status", info.Status, null),
            ("Baseline (A)", info.BaselineLabel, null),
            ("Review snapshot (B)", info.ReviewSnapshotLabel, null),
            ("Created at", info.CreatedAt, null),
            ("Created by", info.CreatedBy, null),
            ("Completed at", info.CompletedAt, null),
            ("Completed by", info.CompletedBy, null),
            ("Changes reviewed", null, items.Count),
            ("Approved", null, items.Count(i => i.Decision == FeatureChangeDecision.Approved)),
            ("Rejected", null, items.Count(i => i.Decision == FeatureChangeDecision.Rejected)),
            ("Pending", null, items.Count(i => i.Decision == FeatureChangeDecision.Pending))
        };

        var infoHeaders = new[] { "Field", "Value" };

        var info2 = builder.AddSheet(InfoSheetName);
        info2.SetColumnWidths(24, 60);
        info2.WriteColoredHeader(infoHeaders).FreezeTopRow();
        info2.SetAutoFilter(infoHeaders.Length, infoRows.Length);

        foreach (var (field, text, number) in infoRows)
        {
            var infoRow = info2.AddRow().Text(field);

            if (number.HasValue)
            {
                infoRow.Number(number.Value);
            }
            else
            {
                infoRow.Text(text);
            }
        }

        return builder.ToArray();
    }

    public static string DecisionLabel(FeatureChangeDecision decision) => decision switch
    {
        FeatureChangeDecision.Approved => "APPROVED",
        FeatureChangeDecision.Rejected => "REJECTED",
        _ => "PENDING"
    };
}
