using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Capacity.Services;

public sealed record ArtRank(int ArtId, string ArtName, int Rank, bool IsBelowLine, bool IsPiOrder);

public interface IArtRankingLookupService
{
    Task<IReadOnlyDictionary<int, IReadOnlyList<ArtRank>>> GetAsync(ArtPrioritization level, int? piId);

    Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> RankingArtIdsByItemAsync(ArtPrioritization level);
}

public class ArtRankingLookupService : IArtRankingLookupService
{
    private readonly IDbContextFactory<EstimationDbContext> _ctx;

    public ArtRankingLookupService(IDbContextFactory<EstimationDbContext> ctx) => _ctx = ctx;

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<ArtRank>>> GetAsync(ArtPrioritization level, int? piId)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var arts = await db.CapitalProjects
            .AsNoTracking()
            .Where(a => a.Prioritization == level)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync();
        if (arts.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<ArtRank>>();
        }

        var facts = await ArtOrderLoader.LoadAsync(db, arts.ToDictionary(a => a.Id, _ => level), [piId]);

        var ranks = new List<(int ItemId, ArtRank Rank)>();
        foreach (var art in arts)
        {
            if (!facts.TryGetValue(art.Id, out var artFacts))
            {
                continue;
            }

            var resolution = artFacts.Resolve(piId);
            ranks.AddRange(resolution.Entries
                .Where(e => e.ItemId != ArtBoardItems.None && e.Origin != ArtOrderOrigin.New)
                .Select(e => (e.ItemId, new ArtRank(art.Id, art.Name, e.Rank, !e.IsIncluded, resolution.Mode == ArtOrderMode.PiOrder))));
        }

        return ranks
            .GroupBy(r => r.ItemId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ArtRank>)g
                    .Select(r => r.Rank)
                    .OrderBy(r => r.ArtName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(r => r.ArtId)
                    .ToList());
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> RankingArtIdsByItemAsync(ArtPrioritization level)
    {
        await using var db = await _ctx.CreateDbContextAsync();

        var rows = await db.ArtPrioritizationOrders
            .AsNoTracking()
            .Where(o => o.Level == level && o.Art.Prioritization == level)
            .Select(o => new { ArtId = o.CapitalProjectId, ItemId = level == ArtPrioritization.PortfolioEpic ? o.PortfolioEpicId : o.BusinessOutcomeId })
            .Where(o => o.ItemId != null)
            .Distinct()
            .ToListAsync();

        return rows
            .GroupBy(r => r.ItemId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(r => r.ArtId).Distinct().OrderBy(id => id).ToList());
    }
}
