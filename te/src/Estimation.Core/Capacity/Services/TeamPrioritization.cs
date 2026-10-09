using Estimation.Core.Capacity.Models;
using Estimation.Core.Features.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Capacity.Services;

public sealed record ArtBoardPlacement(int SortOrder, bool IsIncluded, int? Position);

public sealed record ArtLineRef(int ArtId, string ArtName);

public static class ArtBoardItems
{
    public const int None = 0;

    public static int ItemIdOf(Feature feature, ArtPrioritization level) => level == ArtPrioritization.PortfolioEpic
        ? feature.BusinessOutcome?.PortfolioEpicId ?? feature.BusinessOutcome?.PortfolioEpic?.Id ?? None
        : feature.BusinessOutcomeId ?? feature.BusinessOutcome?.Id ?? None;

    public static int ItemIdOf(ArtPrioritizationOrder order) => order.Level == ArtPrioritization.PortfolioEpic
        ? order.PortfolioEpicId ?? None
        : order.BusinessOutcomeId ?? None;

    public static IReadOnlyDictionary<int, ArtBoardPlacement> Placements(ArtOrderResolution resolution) =>
        resolution.Mode == ArtOrderMode.Empty
            ? new Dictionary<int, ArtBoardPlacement>()
            : resolution.Entries
                .DistinctBy(e => e.ItemId)
                .ToDictionary(e => e.ItemId, e => new ArtBoardPlacement(e.Rank, e.IsIncluded, e.IsIncluded ? e.Rank : null));
}

public sealed record TeamArtPrioritization(string ArtName, ArtPrioritization? Prioritization)
{
    public int ArtId { get; init; }

    public IReadOnlyDictionary<int, ArtBoardPlacement> Placements { get; init; } = new Dictionary<int, ArtBoardPlacement>();

    public ArtOrderMode Kind { get; init; } = ArtOrderMode.Empty;

    public bool HasBoard => Prioritization is not null && Kind != ArtOrderMode.Empty && Placements.Count > 0;

    public ArtBoardPlacement? PlacementOf(Feature feature) =>
        Prioritization is { } level && Placements.TryGetValue(ArtBoardItems.ItemIdOf(feature, level), out var placement)
            ? placement
            : null;
}

public sealed record TeamPrioritization(ArtPrioritization? Mode, IReadOnlyList<TeamArtPrioritization> Arts)
{
    public static TeamPrioritization None { get; } = new(null, []);

    public IReadOnlyList<string> SourceArtNames =>
        Mode is null ? [] : Arts.Where(a => a.Prioritization == Mode).Select(a => a.ArtName).ToList();

    public bool IsConflict =>
        Mode is null && Arts.Select(a => a.Prioritization).OfType<ArtPrioritization>().Distinct().Count() > 1;

    public TeamArtPrioritization? OrderArt => BoardArts is [var only] ? only : null;

    public IReadOnlyList<string> CompetingBoardArtNames =>
        BoardArts.Count > 1 ? BoardArts.Select(a => a.ArtName).ToList() : [];

    private IReadOnlyList<TeamArtPrioritization> BoardArts
    {
        get
        {
            var boards = Mode is null ? [] : Arts.Where(a => a.Prioritization == Mode && a.HasBoard).ToList();
            var piOrders = boards.Where(a => a.Kind == ArtOrderMode.PiOrder).ToList();
            return piOrders.Count > 0 ? piOrders : boards;
        }
    }

    public static TeamPrioritization Resolve(IEnumerable<TeamArtPrioritization> arts)
    {
        var ordered = arts.OrderBy(a => a.ArtName, StringComparer.OrdinalIgnoreCase).ToList();
        var modes = ordered.Select(a => a.Prioritization).OfType<ArtPrioritization>().Distinct().ToList();
        return new TeamPrioritization(modes.Count == 1 ? modes[0] : null, ordered);
    }

    public int? RankOf(Feature feature) => OrderArt?.PlacementOf(feature)?.SortOrder;

    public int? ArtPositionOf(Feature feature) => OrderArt?.PlacementOf(feature)?.Position;

    public IReadOnlyList<ArtLineRef> BelowArtLineOf(Feature feature) =>
        Arts.Where(a => a.PlacementOf(feature) is { IsIncluded: false })
            .Select(a => new ArtLineRef(a.ArtId, a.ArtName))
            .ToList();

    public int? SortKeyOf(Feature feature) => OrderArt is null ? feature.Ranking : RankOf(feature);

    public List<TeamCapacityFeatureRow> DefaultOrder(IEnumerable<TeamCapacityFeatureRow> rows) =>
        rows.OrderBy(r => (Mode is null ? r.FeatureRanking : r.PrioritizationRank) ?? int.MaxValue)
            .ThenBy(r => r.FeatureRanking ?? int.MaxValue)
            .ThenBy(r => r.FeatureId)
            .ToList();

    public static async Task<Dictionary<int, TeamPrioritization>> LoadAsync(
        EstimationDbContext db, IReadOnlyCollection<int> teamIds, int piId)
    {
        var links = await db.CapitalProjectTeams
            .Where(cpt => teamIds.Contains(cpt.TeamId))
            .Select(cpt => new { cpt.TeamId, ArtId = cpt.CapitalProjectId, cpt.Art.Name, cpt.Art.Prioritization })
            .ToListAsync();

        var levelByArt = links
            .Where(l => l.Prioritization != null)
            .DistinctBy(l => l.ArtId)
            .ToDictionary(l => l.ArtId, l => l.Prioritization!.Value);
        var facts = await ArtOrderLoader.LoadAsync(db, levelByArt, [piId]);
        var resolutions = facts.ToDictionary(kv => kv.Key, kv => kv.Value.Resolve(piId));

        return teamIds.Distinct().ToDictionary(
            id => id,
            id => Resolve(links
                .Where(l => l.TeamId == id)
                .Select(l => resolutions.TryGetValue(l.ArtId, out var resolution)
                    ? new TeamArtPrioritization(l.Name, l.Prioritization)
                    {
                        ArtId = l.ArtId,
                        Placements = ArtBoardItems.Placements(resolution),
                        Kind = resolution.Mode,
                    }
                    : new TeamArtPrioritization(l.Name, l.Prioritization) { ArtId = l.ArtId })));
    }
}
