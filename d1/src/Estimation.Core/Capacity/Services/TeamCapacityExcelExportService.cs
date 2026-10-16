using System.Globalization;
using Estimation.Excel;

namespace Estimation.Core.Capacity.Services;

public sealed record TeamCapacityExportBoard(
    string TeamName,
    IReadOnlyList<TeamCapacityFeatureRow> Features,
    IReadOnlyDictionary<int, TeamFeatureDemand> Demands,
    IReadOnlyList<TeamMemberMeta> MemberMeta,
    decimal StoryPointsPerDay = 1m)
{
    public static TeamCapacityExportBoard From(string teamName, TeamCapacityResult board) =>
        new(teamName, board.Features, board.Demands, board.MemberMeta, board.StoryPointsPerDay);
}

public sealed record TeamCapacityExportInfo(
    string? ArtName,
    int PiId,
    string PiName,
    bool IncludeIpSprints,
    bool HasUnsavedChanges,
    DateTime ExportedUtc);

public static class TeamCapacityExcelExportService
{
    public const string FeaturesSheetName = "Features";
    public const string InfoSheetName = "Info";

    public const string AboveLine = "Above";
    public const string BelowLine = "Below";

    public const string FullyReserved = "Fully reserved";
    public const string PartlyReserved = "Partly reserved";
    public const string NotReserved = "Not reserved";
    public const string NoStackCapacity = "No stack capacity";

    public const string MappedByPi = "PI";
    public const string MappedByLabel = "Label";
    public const string NotMappedToPi = "Not mapped to PI";

    public const string UnsavedNote = "Includes changes on the team board that were not saved yet.";

    private const string RedFillColor = "#FDE8E8";
    private const string AmberFillColor = "#FFF4E5";
    private const string GreenFillColor = "#E6F4EA";

    public static readonly string[] Headers =
    [
        "Line", "Team", "Position", "Feature Jira ID", "Feature Name", "Status", "Effort", "Capacity", "Required",
        "Reserved", "Below the ART line", "Feature PI", "Mapped by", "ART Rank", "Ranking", "Business Outcome Jira Id",
        "Business Outcome Name", "Portfolio Epic Jira Id", "Portfolio Epic Name", "Teams", "Labels", "Rag Status",
        "Rag Explain", "Requirement Status", "Design Approval", "Funding Status", "Pi Objective", "Target Start",
        "Target End", "Dependencies", "External Dependencies", "Feature Summary", "Acceptance Criteria"
    ];

    private static readonly double[] Widths =
    [
        10, 22, 10, 18, 40, 18, 10, 18, 11, 11, 24, 18, 18, 10, 10, 22, 35, 22, 35, 30, 30, 14, 35, 20, 20, 20, 30, 14,
        14, 35, 20, 40, 40
    ];

    public static byte[] Build(TeamCapacityExportInfo info, IReadOnlyList<TeamCapacityExportBoard> boards)
    {
        var builder = new ExcelWorkbookBuilder();
        var belowStyle = builder.GetOrCreateFillStyle(RedFillColor);
        var capacityStyles = new Dictionary<string, uint>
        {
            [FullyReserved] = builder.GetOrCreateFillStyle(GreenFillColor),
            [PartlyReserved] = builder.GetOrCreateFillStyle(AmberFillColor),
            [NotReserved] = builder.GetOrCreateFillStyle(RedFillColor),
            [NoStackCapacity] = builder.GetOrCreateFillStyle(AmberFillColor)
        };

        var sheet = builder.AddSheet(FeaturesSheetName);
        sheet.SetColumnWidths(Widths);
        sheet.WriteColoredHeader(Headers).FreezeTopRow();
        sheet.SetAutoFilter(Headers.Length, boards.Sum(b => b.Features.Count));

        var above = 0;
        var below = 0;
        foreach (var board in boards)
        {
            var included = board.Features.Where(f => f.IsIncludedInCapacity).ToList();
            var allocations = TeamBoardAllocator.Allocate(included.Select(f => f.FeatureId), board.Demands, board.MemberMeta);

            for (var i = 0; i < included.Count; i++)
            {
                var allocation = allocations[included[i].FeatureId];
                var capacity = CapacityText(allocation);
                var row = sheet.AddRow().Text(AboveLine);
                WriteFeature(row, board.TeamName, i + 1, included[i], info.PiId, allocation, capacity, capacityStyles[capacity]);
            }

            var excluded = board.Features.Where(f => !f.IsIncludedInCapacity).ToList();
            for (var i = 0; i < excluded.Count; i++)
            {
                var row = sheet.AddRow().StyledText(BelowLine, belowStyle);
                WriteFeature(row, board.TeamName, i + 1, excluded[i], info.PiId, null, null, null);
            }

            above += included.Count;
            below += excluded.Count;
        }

        WriteInfo(builder, info, boards, above, below);
        return builder.ToArray();
    }

    public static string FileName(string scopeName, string piName, DateTime exportedUtc) =>
        $"capacity-{FileNamePart(scopeName)}-{FileNamePart(piName)}-{exportedUtc:yyyyMMdd}.xlsx";

    private static string FileNamePart(string value)
    {
        var name = new string(value.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return name.Length == 0 ? "export" : name;
    }

    public static string CapacityText(TeamBoardFeatureAllocation allocation) => allocation switch
    {
        { IsValid: false } => NoStackCapacity,
        { IsFullyReserved: true } => FullyReserved,
        { Reserved: > 0m } => PartlyReserved,
        _ => NotReserved
    };

    public static string MappedBy(TeamCapacityFeatureRow feature, int piId) =>
        feature.FeaturePiId == piId ? MappedByPi
        : feature.MatchedByLabel ? MappedByLabel
        : NotMappedToPi;

    private static void WriteFeature(
        ExcelRowBuilder row,
        string teamName,
        int position,
        TeamCapacityFeatureRow feature,
        int piId,
        TeamBoardFeatureAllocation? allocation,
        string? capacity,
        uint? capacityStyle)
    {
        row.Text(teamName)
            .Number(position)
            .Text(feature.JiraId)
            .Text(feature.Name)
            .Text(feature.Status)
            .Number(feature.Effort);

        if (capacity is not null && capacityStyle is { } style)
        {
            row.StyledText(capacity, style);
        }
        else
        {
            row.Text(null);
        }

        var valid = allocation is { IsValid: true };
        row.Number(valid ? (double?)allocation!.Required : null)
            .Number(valid ? (double?)allocation!.Reserved : null)
            .Text(string.Join(", ", feature.BelowArtLine.Select(a => a.ArtName)))
            .Text(feature.FeaturePiName)
            .Text(MappedBy(feature, piId))
            .Number(feature.ArtBoardPosition)
            .Number(feature.FeatureRanking)
            .Text(feature.BusinessOutcomeJiraId)
            .Text(feature.BusinessOutcomeSummary)
            .Text(feature.PortfolioEpicJiraId)
            .Text(feature.PortfolioEpicSummary)
            .Text(string.Join("; ", (feature.Teams ?? []).Select(t => t.Name)))
            .Text(feature.Labels)
            .Text(feature.RagStatus)
            .Text(feature.RagExplain)
            .Text(feature.RequirementStatus)
            .Text(feature.TechnicalApproval)
            .Text(feature.FundingStatus)
            .Text(feature.PiObjective)
            .Date(feature.TargetStart)
            .Date(feature.TargetEnd)
            .Text(feature.Dependencies)
            .Text(feature.ExternalDependencies ? "Yes" : "No")
            .Text(feature.Summary)
            .Text(feature.AcceptanceCriteria);
    }

    private static void WriteInfo(
        ExcelWorkbookBuilder builder, TeamCapacityExportInfo info, IReadOnlyList<TeamCapacityExportBoard> boards, int above, int below)
    {
        var rows = new List<(string Field, string? Text, int? Number)>();
        if (info.ArtName is not null)
        {
            rows.Add(("ART", info.ArtName, null));
        }

        rows.Add((boards.Count == 1 ? "Team" : "Teams", string.Join(", ", boards.Select(b => b.TeamName)), null));
        rows.Add(("Story points per day", StoryPointsPerDayText(boards), null));
        rows.Add(("PI", info.PiName, null));
        rows.Add(("IP sprint in the calculation", info.IncludeIpSprints ? "Yes" : "No", null));
        rows.Add(("Above the line", null, above));
        rows.Add(("Below the line", null, below));
        rows.Add(("Exported (UTC)", info.ExportedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), null));
        if (info.HasUnsavedChanges)
        {
            rows.Add(("Note", UnsavedNote, null));
        }

        var headers = new[] { "Field", "Value" };
        var sheet = builder.AddSheet(InfoSheetName);
        sheet.SetColumnWidths(28, 80);
        sheet.WriteColoredHeader(headers).FreezeTopRow();
        sheet.SetAutoFilter(headers.Length, rows.Count);

        foreach (var (field, text, number) in rows)
        {
            var row = sheet.AddRow().Text(field);
            if (number.HasValue)
            {
                row.Number(number.Value);
            }
            else
            {
                row.Text(text);
            }
        }
    }

    private static string StoryPointsPerDayText(IReadOnlyList<TeamCapacityExportBoard> boards)
    {
        var rates = boards.Select(b => b.StoryPointsPerDay).Distinct().ToList();
        return rates.Count == 1
            ? Format(rates[0])
            : string.Join(", ", boards.Select(b => $"{b.TeamName} {Format(b.StoryPointsPerDay)}"));

        static string Format(decimal rate) => rate.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
