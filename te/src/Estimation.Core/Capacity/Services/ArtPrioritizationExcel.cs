using System.Globalization;
using Estimation.Core.Train.Models;
using Estimation.Excel;

namespace Estimation.Core.Capacity.Services;

public sealed record ArtOrderPosition(int ItemId, string? JiraId, int Rank, bool IsIncluded);

public enum ArtUploadStatus
{
    Updated,
    Unchanged,
    NotOnList,
    Invalid,
    Duplicate,
    Skipped,
}

public sealed record ArtUploadRow(string JiraId, string? Summary, string? RankingText, ArtUploadStatus Status)
{
    public int? ItemId { get; init; }
    public int? CurrentRank { get; init; }
    public bool? IsIncluded { get; init; }
    public int? Ranking { get; init; }

    public bool IsApplicable => Status is ArtUploadStatus.Updated or ArtUploadStatus.Unchanged;
}

public sealed record ArtUploadResult(
    IReadOnlyList<ArtUploadRow> Rows,
    string? FileArtName,
    string? FileLevel,
    string? FileScope,
    IReadOnlyList<string> Warnings)
{
    public bool HasChanges => Rows.Any(r => r.Status == ArtUploadStatus.Updated);

    public int Count(ArtUploadStatus status) => Rows.Count(r => r.Status == status);
}

public static class ArtPrioritizationExcel
{
    public const string GeneralScopeName = "General";

    public static string BucketName(ArtPrioritization level) =>
        level == ArtPrioritization.PortfolioEpic ? "No Portfolio Epic" : "No Business Outcome";

    public static string SheetName(ArtPrioritization level) =>
        ArtPrioritizationExportService.SheetName(level == ArtPrioritization.BusinessOutcome);

    public static string ScopeName(string? piName) =>
        string.IsNullOrWhiteSpace(piName) ? GeneralScopeName : piName.Trim();

    public static string FileName(string artName, string scopeName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string($"ART Prioritization - {artName} - {scopeName}"
            .Select(c => invalid.Contains(c) ? '_' : c)
            .ToArray());
        return $"{name}.xlsx";
    }

    public static List<ArtOrderPosition> PositionsOf(IEnumerable<ArtBoardItem> items) =>
        items.Select(i => new ArtOrderPosition(i.Id, i.JiraId, i.Rank, i.IsIncluded)).ToList();

    public static byte[] Export(
        string artName,
        ArtPrioritization level,
        string scopeName,
        IEnumerable<ArtBoardItem> items,
        DateTime exportedUtc)
    {
        var isBusinessOutcome = level == ArtPrioritization.BusinessOutcome;
        var rows = InCurrentOrder(items, i => i.Rank, i => i.IsIncluded)
            .Select(i => new ArtPrioritizationExportRow(
                i.Rank,
                i.IsIncluded,
                i.IsNone ? null : i.JiraId,
                i.IsNone ? BucketName(level) : i.Summary,
                i.IsNone ? null : i.Status,
                isBusinessOutcome && !i.IsNone ? i.ParentJiraId : null,
                isBusinessOutcome && !i.IsNone ? i.ParentSummary : null))
            .ToList();

        var info = new ArtPrioritizationExportInfo(
            artName,
            ArtPrioritizations.Label(level),
            scopeName,
            exportedUtc,
            isBusinessOutcome);

        return ArtPrioritizationExportService.Generate(info, rows);
    }

    public static ArtUploadResult Parse(
        Stream fileStream,
        ArtPrioritization level,
        IReadOnlyList<ArtOrderPosition> current,
        string? artName = null,
        string? scopeName = null)
    {
        var stream = Seekable(fileStream);
        var (headers, dataRows) = ExcelSheetReader.Read(stream, SheetName(level));
        var columns = ExcelSheetReader.BuildColumnMap(headers);
        var jiraIdHeader = ArtPrioritizationExportService.JiraIdHeader;
        var rankingHeader = ArtPrioritizationExportService.RankingHeader;
        if (!columns.ContainsKey(jiraIdHeader) || !columns.ContainsKey(rankingHeader))
        {
            throw new InvalidOperationException(
                $"The file needs a \"{jiraIdHeader}\" column and a \"{rankingHeader}\" column, like the ART Prioritization export.");
        }

        var byJiraId = current
            .Where(p => !string.IsNullOrWhiteSpace(p.JiraId))
            .GroupBy(p => p.JiraId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ArtUploadRow>();
        foreach (var dataRow in dataRows)
        {
            var jiraId = ExcelSheetReader.GetCell(dataRow, columns, jiraIdHeader)?.Trim() ?? string.Empty;
            var rankingText = NullIfBlank(ExcelSheetReader.GetCell(dataRow, columns, rankingHeader));
            var summary = NullIfBlank(ExcelSheetReader.GetCell(dataRow, columns, ArtPrioritizationExportService.SummaryHeader));
            rows.Add(ParseRow(jiraId, summary, rankingText, byJiraId, seen));
        }

        var info = ReadInfo(stream);
        info.TryGetValue(ArtPrioritizationExportService.InfoArt, out var fileArt);
        info.TryGetValue(ArtPrioritizationExportService.InfoLevel, out var fileLevel);
        info.TryGetValue(ArtPrioritizationExportService.InfoScope, out var fileScope);

        return new ArtUploadResult(rows, fileArt, fileLevel, fileScope, Warnings(level, artName, scopeName, fileArt, fileLevel, fileScope));
    }

    public static List<ArtOrderPosition> Apply(IReadOnlyList<ArtOrderPosition> current, IEnumerable<ArtUploadRow> rows)
    {
        var uploaded = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            if (row.IsApplicable && row.ItemId is { } itemId && row.Ranking is { } ranking)
            {
                uploaded.TryAdd(itemId, ranking);
            }
        }

        var ordered = InCurrentOrder(current, p => p.Rank, p => p.IsIncluded)
            .DistinctBy(p => p.ItemId)
            .ToList();

        var result = new List<ArtOrderPosition>(ordered.Count);
        foreach (var included in new[] { true, false })
        {
            var side = ordered
                .Select((position, index) => (Position: position, Index: index))
                .Where(x => x.Position.IsIncluded == included)
                .Select(x => uploaded.TryGetValue(x.Position.ItemId, out var number)
                    ? (x.Position, x.Index, HasUpload: true, Key: number)
                    : (x.Position, x.Index, HasUpload: false, Key: x.Position.Rank))
                .OrderBy(x => x.Key)
                .ThenBy(x => x.HasUpload ? 0 : 1)
                .ThenBy(x => x.Index);

            foreach (var x in side)
            {
                result.Add(x.Position with { Rank = result.Count + 1 });
            }
        }

        return result;
    }

    public static List<ArtBoardItem> ApplyTo(IReadOnlyList<ArtBoardItem> items, IEnumerable<ArtUploadRow> rows)
    {
        var byId = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        return Apply(PositionsOf(items), rows)
            .Select(p => byId[p.ItemId] with { Rank = p.Rank, IsIncluded = p.IsIncluded })
            .ToList();
    }

    public static bool TryParseRanking(string? text, out int ranking)
    {
        ranking = 0;
        if (string.IsNullOrWhiteSpace(text)
            || !decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || number != decimal.Truncate(number)
            || number < 1
            || number > int.MaxValue)
        {
            return false;
        }

        ranking = (int)number;
        return true;
    }

    private static ArtUploadRow ParseRow(
        string jiraId,
        string? summary,
        string? rankingText,
        IReadOnlyDictionary<string, ArtOrderPosition> byJiraId,
        HashSet<string> seen)
    {
        if (jiraId.Length == 0)
        {
            return new ArtUploadRow(jiraId, summary, rankingText, ArtUploadStatus.Skipped);
        }

        if (!seen.Add(jiraId))
        {
            return new ArtUploadRow(jiraId, summary, rankingText, ArtUploadStatus.Duplicate);
        }

        if (!byJiraId.TryGetValue(jiraId, out var position))
        {
            return new ArtUploadRow(jiraId, summary, rankingText, ArtUploadStatus.NotOnList);
        }

        var known = new ArtUploadRow(jiraId, summary, rankingText, ArtUploadStatus.Skipped)
        {
            ItemId = position.ItemId,
            CurrentRank = position.Rank,
            IsIncluded = position.IsIncluded,
        };

        if (rankingText is null)
        {
            return known;
        }

        if (!TryParseRanking(rankingText, out var ranking))
        {
            return known with { Status = ArtUploadStatus.Invalid };
        }

        return known with
        {
            Ranking = ranking,
            Status = ranking == position.Rank ? ArtUploadStatus.Unchanged : ArtUploadStatus.Updated,
        };
    }

    private static Dictionary<string, string> ReadInfo(Stream stream)
    {
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ExcelSheetReader.ReadSheet(stream, ArtPrioritizationExportService.InfoSheetName) is not { } sheet)
        {
            return info;
        }

        foreach (var row in sheet.Rows.Prepend(sheet.Headers))
        {
            if (row.Count >= 2 && !string.IsNullOrWhiteSpace(row[0]) && !string.IsNullOrWhiteSpace(row[1]))
            {
                info.TryAdd(row[0].Trim(), row[1].Trim());
            }
        }

        return info;
    }

    private static List<string> Warnings(
        ArtPrioritization level,
        string? artName,
        string? scopeName,
        string? fileArt,
        string? fileLevel,
        string? fileScope)
    {
        var warnings = new List<string>();
        if (fileArt is not null && !string.IsNullOrWhiteSpace(artName) && !Same(fileArt, artName))
        {
            warnings.Add($"The file was exported from ART {fileArt}, not {artName.Trim()}.");
        }

        var levelName = ArtPrioritizations.Label(level);
        if (fileLevel is not null && !Same(fileLevel, levelName))
        {
            warnings.Add($"The file ranks {fileLevel} items, but this ART ranks {levelName} items.");
        }

        if (fileScope is not null && !string.IsNullOrWhiteSpace(scopeName) && !Same(fileScope, scopeName))
        {
            warnings.Add($"The file was exported from the {fileScope} order, not the {scopeName.Trim()} order.");
        }

        return warnings;
    }

    private static IEnumerable<T> InCurrentOrder<T>(IEnumerable<T> items, Func<T, int> rank, Func<T, bool> isIncluded) =>
        items
            .Select((item, index) => (Item: item, Index: index))
            .OrderByDescending(x => isIncluded(x.Item))
            .ThenBy(x => rank(x.Item))
            .ThenBy(x => x.Index)
            .Select(x => x.Item);

    private static bool Same(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Stream Seekable(Stream stream)
    {
        if (stream.CanSeek)
        {
            return stream;
        }

        var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        return copy;
    }
}
