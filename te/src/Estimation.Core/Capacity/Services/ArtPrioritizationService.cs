using Estimation.Core.Capacity.Models;
using Estimation.Core.Features.DependencyMap.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Capacity.Services;

public sealed record ArtBoardItem(
    int Id,
    string? JiraId,
    string? Summary,
    int Rank,
    bool IsIncluded,
    ArtOrderOrigin Origin,
    IReadOnlyList<int> FeatureIds)
{
    public bool IsNone => Id == ArtBoardItems.None;

    public bool IsSaved => Origin == ArtOrderOrigin.Saved;

    public bool HasFeaturesInScope => FeatureIds.Count > 0;

    public bool IsKeptByGeneralOrder { get; init; }

    public string? Status { get; init; }
    public string? RagStatus { get; init; }
    public string? RagExplain { get; init; }
    public DateTime? TargetStart { get; init; }
    public DateTime? TargetEnd { get; init; }

    public int? ParentId { get; init; }
    public string? ParentJiraId { get; init; }
    public string? ParentSummary { get; init; }

    public IReadOnlyList<FeatureFilterRef> StrategicObjectives { get; init; } = Array.Empty<FeatureFilterRef>();
    public string? FundingStatus { get; init; }
    public string? L6Owner { get; init; }
    public string? Comments { get; init; }
}

public sealed record ArtBoardFeature(
    int FeatureId,
    string? JiraId,
    string? Name,
    string? Status,
    string? PiName,
    string? BusinessOutcomeJiraId,
    string? BusinessOutcomeSummary);

public sealed record ArtPrioritizationBoard(
    int ArtId,
    string ArtName,
    ArtPrioritization? Level,
    int? PiId,
    ArtOrderMode Mode,
    bool HasGeneralOrder,
    List<ArtBoardItem> Items,
    IReadOnlyDictionary<int, ArtBoardFeature> Features);

public sealed record ArtPrioritizationOrderRow(int ItemId, int SortOrder, bool IsIncluded);

public sealed record ArtItemSearchResult(int Id, string? JiraId, string? Summary, string? Status);

public interface IArtPrioritizationService
{
    Task<ArtPrioritizationBoard?> GetAsync(int artId, int? piId);

    Task SaveAsync(int artId, int? piId, ArtPrioritization level, IReadOnlyList<ArtPrioritizationOrderRow> rows);

    Task UseGeneralOrderAsync(int artId, int piId, ArtPrioritization level);

    Task<List<ArtItemSearchResult>> SearchItemsAsync(
        ArtPrioritization level, string? term, bool includeDone, IReadOnlyCollection<int> excludeIds, int take = 50);

    Task<ArtBoardItem?> GetItemAsync(ArtPrioritization level, int itemId);
}

public class ArtPrioritizationService : IArtPrioritizationService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public ArtPrioritizationService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public static bool IsDoneStatus(string? status) =>
        DependencyRiskRule.IsFinished(status) || DependencyRiskRule.IsRejected(status);

    public async Task<ArtPrioritizationBoard?> GetAsync(int artId, int? piId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var art = await db.CapitalProjects
            .AsNoTracking()
            .Where(a => a.Id == artId)
            .Select(a => new { a.Id, a.Name, a.Prioritization })
            .FirstOrDefaultAsync();
        if (art is null)
        {
            return null;
        }

        if (art.Prioritization is not { } level)
        {
            return new ArtPrioritizationBoard(art.Id, art.Name, null, piId, ArtOrderMode.Empty, false, [], new Dictionary<int, ArtBoardFeature>());
        }

        var facts = (await ArtOrderLoader.LoadAsync(db, artId, level, [piId]))!;
        var resolution = facts.Resolve(piId);
        var featuresByItem = facts.FeaturesByItem(piId);

        var featureIds = resolution.Entries
            .SelectMany(e => featuresByItem.GetValueOrDefault(e.ItemId) ?? [])
            .Distinct()
            .ToList();
        var features = await LoadFeaturesAsync(db, featureIds);

        var details = await LoadDetailsAsync(db, level, resolution.Entries.Select(e => e.ItemId).ToList());

        var items = resolution.Entries
            .Select(e =>
            {
                var ids = (featuresByItem.GetValueOrDefault(e.ItemId) ?? [])
                    .Where(features.ContainsKey)
                    .Select(id => features[id])
                    .OrderBy(f => f.Feature.BusinessOutcomeJiraId is null)
                    .ThenBy(f => f.Feature.BusinessOutcomeJiraId, Comparer<string?>.Create(ArtOrderResolver.CompareJiraKeys))
                    .ThenBy(f => f.Ranking ?? int.MaxValue)
                    .ThenBy(f => f.Feature.JiraId, Comparer<string?>.Create(ArtOrderResolver.CompareJiraKeys))
                    .ThenBy(f => f.Feature.FeatureId)
                    .Select(f => f.Feature.FeatureId)
                    .ToList();
                var item = details.GetValueOrDefault(e.ItemId) ?? Blank(e.ItemId);
                return item with
                {
                    Rank = e.Rank,
                    IsIncluded = e.IsIncluded,
                    Origin = e.Origin,
                    FeatureIds = ids,
                    IsKeptByGeneralOrder = piId is not null && facts.IsKeptByGeneral(e.ItemId),
                };
            })
            .ToList();

        return new ArtPrioritizationBoard(
            art.Id,
            art.Name,
            level,
            piId,
            resolution.Mode,
            facts.HasGeneralRows,
            items,
            features.ToDictionary(kv => kv.Key, kv => kv.Value.Feature));
    }

    public async Task SaveAsync(int artId, int? piId, ArtPrioritization level, IReadOnlyList<ArtPrioritizationOrderRow> rows)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        if (piId is null)
        {
            await FreezeLockedPisAsync(db, artId, level);
        }

        var existing = await db.ArtPrioritizationOrders
            .Where(o => o.CapitalProjectId == artId && o.PiId == piId && o.Level == level)
            .ToListAsync();

        var wanted = rows
            .Select((row, index) => (Row: row, Index: index))
            .OrderByDescending(r => r.Row.IsIncluded)
            .ThenBy(r => r.Row.SortOrder)
            .ThenBy(r => r.Index)
            .Select(r => r.Row)
            .DistinctBy(r => r.ItemId)
            .ToList();
        var known = await KnownItemIdsAsync(db, level, wanted.Select(r => r.ItemId).Where(id => id != ArtBoardItems.None).ToList());
        wanted = wanted.Where(r => r.ItemId == ArtBoardItems.None || known.Contains(r.ItemId)).ToList();

        var byItem = new Dictionary<int, ArtPrioritizationOrder>();
        foreach (var order in existing.OrderBy(o => o.Id))
        {
            if (!byItem.TryAdd(ArtBoardItems.ItemIdOf(order), order))
            {
                db.ArtPrioritizationOrders.Remove(order);
            }
        }

        var keep = wanted.Select(r => r.ItemId).ToHashSet();
        db.ArtPrioritizationOrders.RemoveRange(byItem.Where(kv => !keep.Contains(kv.Key)).Select(kv => kv.Value));

        var sortOrder = 0;
        foreach (var row in wanted)
        {
            sortOrder++;
            if (byItem.TryGetValue(row.ItemId, out var entity))
            {
                entity.SortOrder = sortOrder;
                entity.IsIncluded = row.IsIncluded;
                continue;
            }

            db.ArtPrioritizationOrders.Add(NewOrder(artId, piId, level, row.ItemId, sortOrder, row.IsIncluded));
        }

        await db.SaveChangesAsync();
    }

    public async Task UseGeneralOrderAsync(int artId, int piId, ArtPrioritization level)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var rows = await db.ArtPrioritizationOrders
            .Where(o => o.CapitalProjectId == artId && o.PiId == piId && o.Level == level)
            .ToListAsync();
        if (rows.Count == 0)
        {
            return;
        }

        db.ArtPrioritizationOrders.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    public async Task<List<ArtItemSearchResult>> SearchItemsAsync(
        ArtPrioritization level, string? term, bool includeDone, IReadOnlyCollection<int> excludeIds, int take = 50)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var needle = term?.Trim().ToLower() ?? string.Empty;
        var excluded = excludeIds.ToList();

        var matches = level == ArtPrioritization.PortfolioEpic
            ? await db.PortfolioEpics
                .AsNoTracking()
                .Where(pe => !excluded.Contains(pe.Id))
                .Where(pe => needle == ""
                    || (pe.JiraId != null && pe.JiraId.ToLower().Contains(needle))
                    || pe.Summary.ToLower().Contains(needle))
                .Select(pe => new ArtItemSearchResult(pe.Id, pe.JiraId, pe.Summary, pe.Status))
                .ToListAsync()
            : await db.BusinessOutcomes
                .AsNoTracking()
                .Where(bo => !excluded.Contains(bo.Id))
                .Where(bo => needle == ""
                    || (bo.JiraId != null && bo.JiraId.ToLower().Contains(needle))
                    || bo.Summary.ToLower().Contains(needle))
                .Select(bo => new ArtItemSearchResult(bo.Id, bo.JiraId, bo.Summary, bo.Status))
                .ToListAsync();

        return matches
            .Where(i => includeDone || !IsDoneStatus(i.Status))
            .OrderBy(i => i.JiraId, Comparer<string?>.Create(ArtOrderResolver.CompareJiraKeys))
            .ThenBy(i => i.Id)
            .Take(take)
            .ToList();
    }

    public async Task<ArtBoardItem?> GetItemAsync(ArtPrioritization level, int itemId)
    {
        if (itemId == ArtBoardItems.None)
        {
            return Blank(itemId);
        }

        await using var db = await _ctx.CreateDbContextAsync();
        return (await LoadDetailsAsync(db, level, [itemId])).GetValueOrDefault(itemId);
    }

    private static async Task FreezeLockedPisAsync(EstimationDbContext db, int artId, ArtPrioritization level)
    {
        var lockedPiIds = await db.Pis
            .AsNoTracking()
            .Where(p => p.IsLocked == true)
            .Select(p => p.Id)
            .ToListAsync();
        if (lockedPiIds.Count == 0)
        {
            return;
        }

        var facts = await ArtOrderLoader.LoadAsync(db, artId, level, lockedPiIds.Select(id => (int?)id).ToList());
        if (facts is null)
        {
            return;
        }

        foreach (var (piId, resolution) in facts.LockedPiFreeze(lockedPiIds))
        {
            foreach (var entry in resolution.Entries)
            {
                db.ArtPrioritizationOrders.Add(NewOrder(artId, piId, level, entry.ItemId, entry.Rank, entry.IsIncluded));
            }
        }
    }

    private static ArtPrioritizationOrder NewOrder(int artId, int? piId, ArtPrioritization level, int itemId, int sortOrder, bool isIncluded)
    {
        var id = itemId == ArtBoardItems.None ? (int?)null : itemId;
        return new ArtPrioritizationOrder
        {
            CapitalProjectId = artId,
            PiId = piId,
            Level = level,
            PortfolioEpicId = level == ArtPrioritization.PortfolioEpic ? id : null,
            BusinessOutcomeId = level == ArtPrioritization.BusinessOutcome ? id : null,
            SortOrder = sortOrder,
            IsIncluded = isIncluded,
        };
    }

    private static ArtBoardItem Blank(int itemId) => new(itemId, null, null, 0, true, ArtOrderOrigin.New, []);

    private static async Task<Dictionary<int, (ArtBoardFeature Feature, int? Ranking)>> LoadFeaturesAsync(
        EstimationDbContext db, List<int> featureIds)
    {
        if (featureIds.Count == 0)
        {
            return new Dictionary<int, (ArtBoardFeature, int?)>();
        }

        var rows = await db.Features
            .AsNoTracking()
            .Where(f => featureIds.Contains(f.Id))
            .Select(f => new
            {
                f.Id,
                f.JiraId,
                Name = f.Name ?? f.Summary,
                f.Status,
                PiName = f.Pi == null ? null : f.Pi.Name,
                OutcomeJiraId = f.BusinessOutcome == null ? null : f.BusinessOutcome.JiraId,
                OutcomeSummary = f.BusinessOutcome == null ? null : f.BusinessOutcome.Summary,
                f.Ranking,
            })
            .ToListAsync();

        return rows.ToDictionary(
            f => f.Id,
            f => (new ArtBoardFeature(f.Id, f.JiraId, f.Name, f.Status, f.PiName, f.OutcomeJiraId, f.OutcomeSummary), f.Ranking));
    }

    private static Task<Dictionary<int, ArtBoardItem>> LoadDetailsAsync(EstimationDbContext db, ArtPrioritization level, List<int> itemIds)
    {
        var ids = itemIds.Where(id => id != ArtBoardItems.None).Distinct().ToList();
        return level == ArtPrioritization.PortfolioEpic
            ? LoadEpicsAsync(db, ids)
            : LoadOutcomesAsync(db, ids);
    }

    private static async Task<Dictionary<int, ArtBoardItem>> LoadEpicsAsync(EstimationDbContext db, List<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, ArtBoardItem>();
        }

        var epics = await db.PortfolioEpics
            .AsNoTracking()
            .Where(pe => ids.Contains(pe.Id))
            .Include(pe => pe.UnfundedOption)
            .Include(pe => pe.StrategicObjectivePortfolioEpics).ThenInclude(spe => spe.StrategicObjective)
            .AsSplitQuery()
            .ToListAsync();

        return epics.ToDictionary(
            pe => pe.Id,
            pe => new ArtBoardItem(pe.Id, pe.JiraId, pe.Summary, 0, true, ArtOrderOrigin.New, [])
            {
                Status = pe.Status,
                RagStatus = pe.RagStatus,
                TargetStart = pe.TargetStart,
                TargetEnd = pe.TargetEnd,
                StrategicObjectives = pe.StrategicObjectivePortfolioEpics
                    .Select(spe => spe.StrategicObjective)
                    .Where(so => !string.IsNullOrEmpty(so.JiraId))
                    .Select(so => new FeatureFilterRef(so.JiraId!, so.Summary))
                    .DistinctBy(so => so.JiraId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(so => so.JiraId, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                FundingStatus = pe.UnfundedOption?.Name,
                L6Owner = pe.L6Owner,
                Comments = pe.Comments,
            });
    }

    private static async Task<Dictionary<int, ArtBoardItem>> LoadOutcomesAsync(EstimationDbContext db, List<int> ids)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<int, ArtBoardItem>();
        }

        var outcomes = await db.BusinessOutcomes
            .AsNoTracking()
            .Where(bo => ids.Contains(bo.Id))
            .Include(bo => bo.PortfolioEpic)
            .ToListAsync();

        return outcomes.ToDictionary(
            bo => bo.Id,
            bo => new ArtBoardItem(bo.Id, bo.JiraId, bo.Summary, 0, true, ArtOrderOrigin.New, [])
            {
                Status = bo.Status,
                RagStatus = bo.RagStatus,
                RagExplain = bo.RagExplain,
                TargetStart = bo.TargetStart,
                TargetEnd = bo.TargetEnd,
                ParentId = bo.PortfolioEpicId,
                ParentJiraId = bo.PortfolioEpic?.JiraId,
                ParentSummary = bo.PortfolioEpic?.Summary,
            });
    }

    private static async Task<HashSet<int>> KnownItemIdsAsync(EstimationDbContext db, ArtPrioritization level, List<int> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var known = level == ArtPrioritization.PortfolioEpic
            ? await db.PortfolioEpics.Where(pe => ids.Contains(pe.Id)).Select(pe => pe.Id).ToListAsync()
            : await db.BusinessOutcomes.Where(bo => ids.Contains(bo.Id)).Select(bo => bo.Id).ToListAsync();
        return known.ToHashSet();
    }
}
