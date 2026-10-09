using Estimation.Core.Capacity.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;

namespace Estimation.Core.Capacity.Services;

public sealed record ArtOrderFacts
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> NoFeatures = new Dictionary<int, IReadOnlyList<int>>();

    public required int ArtId { get; init; }

    public required ArtPrioritization Level { get; init; }

    public IReadOnlyList<ArtOrderRow> General { get; init; } = [];

    public IReadOnlyDictionary<int, IReadOnlyList<ArtOrderRow>> PiRows { get; init; } = new Dictionary<int, IReadOnlyList<ArtOrderRow>>();

    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlyList<int>>> PiFeatures { get; init; } =
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<int>>>();

    public IReadOnlyDictionary<int, IReadOnlyList<int>> GeneralFeatures { get; init; } = NoFeatures;

    public IReadOnlySet<int> FeaturelessItems { get; init; } = new HashSet<int>();

    public IReadOnlyDictionary<int, string?> JiraIds { get; init; } = new Dictionary<int, string?>();

    public bool HasGeneralRows => General.Count > 0;

    public IReadOnlyList<ArtOrderRow> OwnRows(int? piId) =>
        piId is { } pi ? PiRows.GetValueOrDefault(pi) ?? [] : General;

    public bool HasOwnRows(int piId) => OwnRows(piId).Count > 0;

    public bool IsKeptByGeneral(int itemId) => FeaturelessItems.Contains(itemId) && General.Any(r => r.ItemId == itemId);

    public IReadOnlyDictionary<int, IReadOnlyList<int>> FeaturesByItem(int? piId) =>
        piId is { } pi ? PiFeatures.GetValueOrDefault(pi) ?? NoFeatures : GeneralFeatures;

    public IReadOnlyList<ArtOrderItem> Relevant(int? piId)
    {
        var ids = FeaturesByItem(piId).Keys.ToHashSet();
        if (piId is null)
        {
            ids.UnionWith(General.Select(r => r.ItemId));
        }
        else
        {
            ids.UnionWith(General.Concat(OwnRows(piId)).Select(r => r.ItemId).Where(FeaturelessItems.Contains));
        }

        return ids.Select(id => new ArtOrderItem(id, JiraIds.GetValueOrDefault(id))).ToList();
    }

    public ArtOrderResolution Resolve(int? piId) => piId is null
        ? ArtOrderResolver.ResolveGeneral(General, Relevant(null))
        : ArtOrderResolver.ResolvePi(OwnRows(piId), General, Relevant(piId));

    public IReadOnlyList<(int PiId, ArtOrderResolution Resolution)> LockedPiFreeze(IEnumerable<int> lockedPiIds) =>
        lockedPiIds
            .Distinct()
            .Where(pi => !HasOwnRows(pi) && FeaturesByItem(pi).Count > 0)
            .Select(pi => (PiId: pi, Resolution: Resolve(pi)))
            .Where(x => x.Resolution.Entries.Count > 0)
            .ToList();
}

public static class ArtOrderLoader
{
    private sealed record FeatureFact(int TeamId, int FeatureId, int? PiId, DateTime? PiEnd, int? OutcomeId, int? EpicId);

    public static int ItemIdOf(ArtPrioritization level, int? outcomeId, int? epicId) =>
        (level == ArtPrioritization.PortfolioEpic ? epicId : outcomeId) ?? ArtBoardItems.None;

    public static async Task<ArtOrderFacts?> LoadAsync(EstimationDbContext db, int artId, ArtPrioritization level, IReadOnlyCollection<int?> scopes) =>
        (await LoadAsync(db, new Dictionary<int, ArtPrioritization> { [artId] = level }, scopes)).GetValueOrDefault(artId);

    public static async Task<Dictionary<int, ArtOrderFacts>> LoadAsync(
        EstimationDbContext db, IReadOnlyDictionary<int, ArtPrioritization> levelByArt, IReadOnlyCollection<int?> scopes)
    {
        if (levelByArt.Count == 0)
        {
            return new Dictionary<int, ArtOrderFacts>();
        }

        var artIds = levelByArt.Keys.ToList();
        var piIds = scopes.OfType<int>().Distinct().ToList();
        var piScopes = piIds.Select(id => (int?)id).ToList();
        var includeGeneral = scopes.Contains(null);
        var today = DateTime.UtcNow.Date;

        var teamLinks = await db.CapitalProjectTeams
            .AsNoTracking()
            .Where(cpt => artIds.Contains(cpt.CapitalProjectId))
            .Select(cpt => new { ArtId = cpt.CapitalProjectId, cpt.TeamId })
            .ToListAsync();
        var teamIds = teamLinks.Select(l => l.TeamId).Distinct().ToList();

        var orders = (await db.ArtPrioritizationOrders
                .AsNoTracking()
                .Where(o => artIds.Contains(o.CapitalProjectId) && (o.PiId == null || piScopes.Contains(o.PiId)))
                .ToListAsync())
            .Where(o => levelByArt[o.CapitalProjectId] == o.Level)
            .ToList();

        var featureFacts = teamIds.Count == 0
            ? []
            : await db.FeatureTeams
                .AsNoTracking()
                .Where(ft => teamIds.Contains(ft.TeamId)
                    && (piScopes.Contains(ft.Feature.PiId)
                        || (includeGeneral
                            && (ft.Feature.PiId == null || ft.Feature.Pi!.EndDate == null || ft.Feature.Pi.EndDate >= today))))
                .Select(ft => new FeatureFact(
                    ft.TeamId,
                    ft.FeatureId,
                    ft.Feature.PiId,
                    ft.Feature.Pi == null ? null : ft.Feature.Pi.EndDate,
                    ft.Feature.BusinessOutcomeId,
                    ft.Feature.BusinessOutcome == null ? null : ft.Feature.BusinessOutcome.PortfolioEpicId))
                .ToListAsync();

        var boardFacts = teamIds.Count == 0 || piIds.Count == 0
            ? []
            : await db.TeamCapacityFeatureOrders
                .AsNoTracking()
                .Where(o => teamIds.Contains(o.TeamId) && piIds.Contains(o.PiId))
                .Select(o => new FeatureFact(
                    o.TeamId,
                    o.FeatureId,
                    o.PiId,
                    null,
                    o.Feature.BusinessOutcomeId,
                    o.Feature.BusinessOutcome == null ? null : o.Feature.BusinessOutcome.PortfolioEpicId))
                .ToListAsync();

        var labelRules = teamIds.Count == 0 || piIds.Count == 0
            ? []
            : (await db.Pis
                    .AsNoTracking()
                    .Where(p => piIds.Contains(p.Id) && p.FeatureLabels != null && p.FeatureLabels != "")
                    .Select(p => new { p.Id, p.FeatureLabels, p.LabelMatchMode })
                    .ToListAsync())
                .Select(p => (PiId: p.Id, Labels: PiLabelMatching.ParseLabels(p.FeatureLabels), Mode: p.LabelMatchMode))
                .Where(r => r.Labels.Count > 0)
                .ToList();

        var labelFacts = new List<FeatureFact>();
        if (labelRules.Count > 0)
        {
            var labelled = await db.FeatureTeams
                .AsNoTracking()
                .Where(ft => teamIds.Contains(ft.TeamId) && ft.Feature.Labels != null && ft.Feature.Labels != "")
                .Select(ft => new
                {
                    ft.TeamId,
                    ft.FeatureId,
                    ft.Feature.PiId,
                    ft.Feature.Labels,
                    OutcomeId = ft.Feature.BusinessOutcomeId,
                    EpicId = ft.Feature.BusinessOutcome == null ? null : ft.Feature.BusinessOutcome.PortfolioEpicId,
                })
                .ToListAsync();
            foreach (var rule in labelRules)
            {
                labelFacts.AddRange(labelled
                    .Where(f => f.PiId != rule.PiId && PiLabelMatching.Matches(rule.Labels, rule.Mode, f.Labels))
                    .Select(f => new FeatureFact(f.TeamId, f.FeatureId, rule.PiId, null, f.OutcomeId, f.EpicId)));
            }
        }

        var savedEpicIds = orders
            .Where(o => o.Level == ArtPrioritization.PortfolioEpic && o.PortfolioEpicId != null)
            .Select(o => o.PortfolioEpicId)
            .Distinct()
            .ToList();
        var savedOutcomeIds = orders
            .Where(o => o.Level == ArtPrioritization.BusinessOutcome && o.BusinessOutcomeId != null)
            .Select(o => o.BusinessOutcomeId)
            .Distinct()
            .ToList();

        var coverFacts = teamIds.Count == 0 || savedEpicIds.Count + savedOutcomeIds.Count == 0
            ? []
            : (await db.FeatureTeams
                    .AsNoTracking()
                    .Where(ft => teamIds.Contains(ft.TeamId)
                        && ft.Feature.BusinessOutcome != null
                        && (savedOutcomeIds.Contains(ft.Feature.BusinessOutcomeId)
                            || savedEpicIds.Contains(ft.Feature.BusinessOutcome.PortfolioEpicId)))
                    .Select(ft => new { ft.TeamId, OutcomeId = ft.Feature.BusinessOutcomeId, EpicId = ft.Feature.BusinessOutcome!.PortfolioEpicId })
                    .Union(db.TeamCapacityFeatureOrders
                        .Where(o => teamIds.Contains(o.TeamId)
                            && o.Feature.BusinessOutcome != null
                            && (savedOutcomeIds.Contains(o.Feature.BusinessOutcomeId)
                                || savedEpicIds.Contains(o.Feature.BusinessOutcome.PortfolioEpicId)))
                        .Select(o => new { o.TeamId, OutcomeId = o.Feature.BusinessOutcomeId, EpicId = o.Feature.BusinessOutcome!.PortfolioEpicId }))
                    .ToListAsync())
                .Select(c => (c.TeamId, c.OutcomeId, c.EpicId))
                .ToList();

        var facts = new Dictionary<int, ArtOrderFacts>();
        foreach (var (artId, level) in levelByArt)
        {
            var artOrders = orders.Where(o => o.CapitalProjectId == artId).ToList();
            var artTeamIds = teamLinks.Where(l => l.ArtId == artId).Select(l => l.TeamId).ToHashSet();

            var piFeatures = new Dictionary<int, Dictionary<int, List<int>>>();
            var generalFeatures = new Dictionary<int, List<int>>();
            foreach (var fact in featureFacts.Concat(boardFacts).Concat(labelFacts).Where(f => artTeamIds.Contains(f.TeamId)))
            {
                if (fact.PiId is { } pi && piIds.Contains(pi))
                {
                    if (!piFeatures.TryGetValue(pi, out var map))
                    {
                        piFeatures[pi] = map = [];
                    }

                    AddFeature(map, ItemIdOf(level, fact.OutcomeId, fact.EpicId), fact.FeatureId);
                }
            }

            if (includeGeneral)
            {
                foreach (var fact in featureFacts.Where(f => artTeamIds.Contains(f.TeamId)
                    && (f.PiId is null || f.PiEnd is null || f.PiEnd >= today)))
                {
                    AddFeature(generalFeatures, ItemIdOf(level, fact.OutcomeId, fact.EpicId), fact.FeatureId);
                }
            }

            var covered = coverFacts
                .Where(f => artTeamIds.Contains(f.TeamId))
                .Select(f => ItemIdOf(level, f.OutcomeId, f.EpicId))
                .ToHashSet();
            var featureless = artOrders
                .Select(ArtBoardItems.ItemIdOf)
                .Where(id => id != ArtBoardItems.None && !covered.Contains(id))
                .ToHashSet();

            facts[artId] = new ArtOrderFacts
            {
                ArtId = artId,
                Level = level,
                General = artOrders.Where(o => o.PiId is null).Select(ToRow).ToList(),
                PiRows = artOrders
                    .Where(o => o.PiId is not null)
                    .GroupBy(o => o.PiId!.Value)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<ArtOrderRow>)g.Select(ToRow).ToList()),
                PiFeatures = piFeatures.ToDictionary(kv => kv.Key, kv => Freeze(kv.Value)),
                GeneralFeatures = Freeze(generalFeatures),
                FeaturelessItems = featureless,
            };
        }

        await AttachJiraIdsAsync(db, facts);
        return facts;
    }

    private static ArtOrderRow ToRow(ArtPrioritizationOrder order) =>
        new(ArtBoardItems.ItemIdOf(order), order.SortOrder, order.IsIncluded, order.Id);

    private static void AddFeature(Dictionary<int, List<int>> map, int itemId, int featureId)
    {
        if (!map.TryGetValue(itemId, out var list))
        {
            map[itemId] = list = [];
        }

        if (!list.Contains(featureId))
        {
            list.Add(featureId);
        }
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> Freeze(Dictionary<int, List<int>> map) =>
        map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value);

    private static async Task AttachJiraIdsAsync(EstimationDbContext db, Dictionary<int, ArtOrderFacts> facts)
    {
        IEnumerable<int> ItemIds(ArtOrderFacts f) =>
            f.General.Select(r => r.ItemId)
                .Concat(f.PiRows.Values.SelectMany(rows => rows.Select(r => r.ItemId)))
                .Concat(f.GeneralFeatures.Keys)
                .Concat(f.PiFeatures.Values.SelectMany(map => map.Keys))
                .Where(id => id != ArtBoardItems.None);

        var epicIds = facts.Values.Where(f => f.Level == ArtPrioritization.PortfolioEpic).SelectMany(ItemIds).Distinct().ToList();
        var outcomeIds = facts.Values.Where(f => f.Level == ArtPrioritization.BusinessOutcome).SelectMany(ItemIds).Distinct().ToList();

        var epicKeys = epicIds.Count == 0
            ? new Dictionary<int, string?>()
            : await db.PortfolioEpics.AsNoTracking()
                .Where(pe => epicIds.Contains(pe.Id))
                .ToDictionaryAsync(pe => pe.Id, pe => pe.JiraId);
        var outcomeKeys = outcomeIds.Count == 0
            ? new Dictionary<int, string?>()
            : await db.BusinessOutcomes.AsNoTracking()
                .Where(bo => outcomeIds.Contains(bo.Id))
                .ToDictionaryAsync(bo => bo.Id, bo => bo.JiraId);

        foreach (var artId in facts.Keys.ToList())
        {
            var current = facts[artId];
            facts[artId] = current with { JiraIds = current.Level == ArtPrioritization.PortfolioEpic ? epicKeys : outcomeKeys };
        }
    }
}
