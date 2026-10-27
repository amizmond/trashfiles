using Estimation.Core.Capacity.Services;
using Estimation.Core.JiraIntegration.Models;
using Estimation.Core.Train.Models;

namespace Estimation.Core.DepartmentPlanning.Services;

public enum PlanningLens
{
    Objectives = 0,
    Drawdown = 1,
}

public enum PlanningLevel
{
    Art = 0,
    StrategicObjective = 1,
    PortfolioEpic = 2,
    BusinessOutcome = 3,
    BusinessEpic = 4,
}

public enum PlanningRowKind
{
    Node = 0,
    LineMarker = 1,
}

public enum PlanningSort
{
    Priority = 0,
    Title = 1,
    JiraId = 2,
    Features = 3,
    Progress = 4,
    Demand = 5,
    Planned = 6,
    Unplanned = 7,
    Rag = 8,
    Dependencies = 9,
    Hygiene = 10,
    TargetEnd = 11,
    Share = 12,
}

public sealed record PlanningOptions
{
    public const string NoRag = "None";

    public PlanningLens Lens { get; init; } = PlanningLens.Objectives;

    public string? Search { get; init; }

    public bool HideDone { get; init; }

    public bool OnlyUnplanned { get; init; }

    public bool OnlyAtRisk { get; init; }

    public bool OnlyHygieneIssues { get; init; }

    public IReadOnlySet<string>? RagStatuses { get; init; }

    public IReadOnlySet<int>? ArtIds { get; init; }

    public bool IncludeEmptyNodes { get; init; }

    public PlanningSort SortBy { get; init; } = PlanningSort.Priority;

    public bool SortDescending { get; init; }

    public int ActiveFilterCount =>
        (HideDone ? 1 : 0) + (OnlyUnplanned ? 1 : 0) + (OnlyAtRisk ? 1 : 0) + (OnlyHygieneIssues ? 1 : 0)
        + (RagStatuses is null ? 0 : 1) + (string.IsNullOrWhiteSpace(Search) ? 0 : 1);
}

public sealed record PlanningMetrics(
    int Features,
    int Done,
    int InProgress,
    int Blocked,
    int NotStarted,
    decimal DemandSp,
    decimal PlannedSp,
    decimal UnplannedSp,
    decimal ReservedSp,
    int Dependencies,
    int DependenciesAtRisk,
    int FeaturesAtRisk,
    int HygieneFailingFeatures,
    int Teams,
    int Arts,
    string? WorstRag,
    DateTime? TargetEnd)
{
    public static readonly PlanningMetrics Empty = new(0, 0, 0, 0, 0, 0m, 0m, 0m, 0m, 0, 0, 0, 0, 0, 0, null, null);

    public double Progress => Features == 0 ? 0d : (double)Done / Features;

    public bool HasUnplanned => UnplannedSp > 0m;
}

public sealed record PlanningRank(int ArtId, string ArtAbbreviation, string ArtName, int Rank, bool BelowLine, bool IsSaved);

public sealed record PlanningTeamCell(decimal DemandSp, decimal PlannedSp, decimal? CumulativeSp, bool OverCapacity);

public sealed record PlanningRow
{
    private static readonly IReadOnlyDictionary<int, PlanningTeamCell> NoCells = new Dictionary<int, PlanningTeamCell>();

    public required string Id { get; init; }

    public PlanningRowKind Kind { get; init; } = PlanningRowKind.Node;

    public required PlanningLevel Level { get; init; }

    public int? EntityId { get; init; }

    public string? JiraId { get; init; }

    public required string Title { get; init; }

    public string? Status { get; init; }

    public string? RagStatus { get; init; }

    public string? RagExplain { get; init; }

    public string? FundingStatus { get; init; }

    public DateTime? TargetStart { get; init; }

    public DateTime? TargetEnd { get; init; }

    public int Depth { get; init; }

    public IReadOnlyList<PlanningRow> Children { get; init; } = [];

    public PlanningMetrics Metrics { get; init; } = PlanningMetrics.Empty;

    public IReadOnlyList<PlanningRank> Ranks { get; init; } = [];

    public int? SortRank { get; init; }

    public bool HasArtRank { get; init; }

    public bool IsBelowArtLine { get; init; }

    public decimal? CumulativePlannedSp { get; init; }

    public bool OverCapacity { get; init; }

    public bool CapacityRunsOutHere { get; init; }

    public decimal? CapacitySp { get; init; }

    public double? CapacityDays { get; init; }

    public double? ShareOfCapacity { get; init; }

    public IReadOnlyList<string> AlsoUnder { get; init; } = [];

    public IReadOnlyDictionary<int, PlanningTeamCell> TeamCells { get; init; } = NoCells;

    public PlanningFeature? Feature { get; init; }

    public int? ArtId { get; init; }

    public bool IsBucket => Kind == PlanningRowKind.Node && EntityId is null && Level != PlanningLevel.Art;

    public DateTime? ShownTargetEnd => Level == PlanningLevel.BusinessEpic ? TargetEnd : Metrics.TargetEnd ?? TargetEnd;

    public bool HasChildren => Children.Count > 0;
}

public sealed record PlanningTree(
    PlanningLens Lens,
    IReadOnlyList<PlanningRow> Roots,
    PlanningMetrics Total,
    decimal TotalCapacitySp,
    double TotalCapacityDays,
    IReadOnlyList<PlanningTeam> Teams,
    IReadOnlyList<PlanningArt> Arts,
    int FeaturesInScope,
    int FeaturesShown)
{
    public static readonly PlanningTree Empty = new(PlanningLens.Objectives, [], PlanningMetrics.Empty, 0m, 0d, [], [], 0, 0);

    private Dictionary<string, PlanningRow>? _byId;

    public IReadOnlyDictionary<int, decimal> PlannedSpByTeam { get; init; } = new Dictionary<int, decimal>();

    public IEnumerable<PlanningRow> Flatten(Func<PlanningRow, bool> isExpanded)
    {
        var stack = new Stack<PlanningRow>();
        for (var i = Roots.Count - 1; i >= 0; i--)
        {
            stack.Push(Roots[i]);
        }

        while (stack.Count > 0)
        {
            var row = stack.Pop();
            yield return row;
            if (row.Children.Count == 0 || !isExpanded(row))
            {
                continue;
            }

            for (var i = row.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(row.Children[i]);
            }
        }
    }

    public IEnumerable<PlanningRow> FlattenAll() => Flatten(_ => true);

    public PlanningRow? Find(string id)
    {
        _byId ??= FlattenAll().GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        return _byId.GetValueOrDefault(id);
    }

    public IReadOnlyList<PlanningRow> PathTo(string id)
    {
        var path = new List<PlanningRow>();
        return Walk(Roots, id, path) ? path : [];

        static bool Walk(IReadOnlyList<PlanningRow> rows, string target, List<PlanningRow> path)
        {
            foreach (var row in rows)
            {
                path.Add(row);
                if (row.Id == target || Walk(row.Children, target, path))
                {
                    return true;
                }

                path.RemoveAt(path.Count - 1);
            }

            return false;
        }
    }
}

public static class DepartmentPlanningTree
{
    public const int None = ArtBoardItems.None;

    public const string NoObjectiveTitle = "No strategic objective";
    public const string NoEpicTitle = "No portfolio epic";
    public const string NoOutcomeTitle = "No business outcome";

    private static readonly Comparer<string?> JiraKeys = Comparer<string?>.Create(ArtOrderResolver.CompareJiraKeys);

    public static PlanningTree Build(DepartmentPlanningData data, PlanningOptions options) => new Builder(data, options).Build();

    public static int RagRank(string? rag) => RagStatuses.Normalize(rag) switch
    {
        RagStatuses.Red => 0,
        RagStatuses.Amber => 1,
        RagStatuses.Green => 2,
        null => 4,
        _ => 3,
    };

    public static IReadOnlyList<string> RagChoices(IEnumerable<PlanningFeature> features)
    {
        var others = RagStatuses.Choices(features.Select(f => f.RagStatus))
            .Where(rag => !RagStatuses.IsKnown(rag) && !string.Equals(rag, PlanningOptions.NoRag, StringComparison.OrdinalIgnoreCase));
        return [RagStatuses.Red, RagStatuses.Amber, RagStatuses.Green, .. others, PlanningOptions.NoRag];
    }

    public static bool IsAtRisk(PlanningFeature feature) =>
        !feature.IsDone
        && (feature.DependenciesAtRisk > 0
        || feature.StatusClass == PlanningStatusClass.Blocked
        || string.Equals(RagStatuses.Normalize(feature.RagStatus), RagStatuses.Red, StringComparison.OrdinalIgnoreCase));

    private sealed class Builder
    {
        private readonly DepartmentPlanningData _data;
        private readonly PlanningOptions _options;
        private readonly Dictionary<int, PlanningTeam> _teamsById;
        private readonly Dictionary<int, PlanningOutcome> _outcomesById;
        private readonly Dictionary<int, PlanningEpic> _epicsById;
        private readonly Dictionary<int, PlanningObjective> _objectivesById;
        private readonly Dictionary<int, List<int>> _artIdsByTeam;
        private readonly HashSet<int> _scopedArtIds;
        private readonly HashSet<int> _scopedTeamIds;
        private readonly List<PlanningFeature> _scopeFeatures;
        private readonly List<PlanningFeature> _features;
        private readonly string? _search;
        private readonly HashSet<int> _matchingObjectives = [];
        private readonly HashSet<int> _matchingEpics = [];
        private readonly HashSet<int> _matchingOutcomes = [];
        private readonly HashSet<int> _matchingArts = [];

        public Builder(DepartmentPlanningData data, PlanningOptions options)
        {
            _data = data;
            _options = options;
            _teamsById = data.Teams.ToDictionary(t => t.Id);
            _outcomesById = data.Outcomes.ToDictionary(o => o.Id);
            _epicsById = data.Epics.ToDictionary(e => e.Id);
            _objectivesById = data.Objectives.ToDictionary(o => o.Id);

            _artIdsByTeam = new Dictionary<int, List<int>>();
            foreach (var art in data.Arts)
            {
                foreach (var teamId in art.TeamIds)
                {
                    if (!_artIdsByTeam.TryGetValue(teamId, out var list))
                    {
                        _artIdsByTeam[teamId] = list = [];
                    }

                    list.Add(art.Id);
                }
            }

            _scopedArtIds = data.Arts.Select(a => a.Id).Where(id => options.ArtIds is null || options.ArtIds.Contains(id)).ToHashSet();
            _scopedTeamIds = data.Teams
                .Where(t => _artIdsByTeam.TryGetValue(t.Id, out var arts) && arts.Any(_scopedArtIds.Contains))
                .Select(t => t.Id)
                .ToHashSet();

            _search = string.IsNullOrWhiteSpace(options.Search) ? null : options.Search.Trim();
            if (_search is not null)
            {
                _matchingObjectives = data.Objectives.Where(o => Matches(o.JiraId, o.Summary)).Select(o => o.Id).ToHashSet();
                _matchingEpics = data.Epics.Where(e => Matches(e.JiraId, e.Summary)).Select(e => e.Id).ToHashSet();
                _matchingOutcomes = data.Outcomes.Where(o => Matches(o.JiraId, o.Summary)).Select(o => o.Id).ToHashSet();
                _matchingArts = data.Arts.Where(a => Matches(a.Name, a.Abbreviation)).Select(a => a.Id).ToHashSet();
            }

            _scopeFeatures = data.Features
                .Select(f => f with { Teams = f.Teams.Where(t => _scopedTeamIds.Contains(t.TeamId)).ToList() })
                .Where(f => f.Teams.Count > 0)
                .ToList();
            _features = _scopeFeatures
                .Where(Passes)
                .Where(KeptBySearch)
                .ToList();
        }

        private bool SearchActive => _search is not null;

        public PlanningTree Build()
        {
            var roots = _options.Lens == PlanningLens.Drawdown ? BuildDrawdown() : BuildObjectives();
            var scopedTeams = _data.Teams.Where(t => _scopedTeamIds.Contains(t.Id)).ToList();
            var scopedArts = _data.Arts.Where(a => _scopedArtIds.Contains(a.Id)).ToList();

            return new PlanningTree(
                _options.Lens,
                roots,
                Aggregate(_features, _ => true),
                scopedTeams.Sum(t => t.CapacitySp),
                scopedTeams.Sum(t => t.CapacityDays),
                scopedTeams,
                scopedArts,
                _scopeFeatures.Count,
                _features.Count)
            {
                PlannedSpByTeam = PlannedByTeam(_features, _ => true),
            };
        }

        private bool Passes(PlanningFeature feature)
        {
            if (_options.HideDone && feature.IsDone)
            {
                return false;
            }

            if (_options.OnlyUnplanned && feature.UnplannedSp <= 0)
            {
                return false;
            }

            if (_options.OnlyAtRisk && !IsAtRisk(feature))
            {
                return false;
            }

            if (_options.OnlyHygieneIssues && feature.HygieneFailures == 0)
            {
                return false;
            }

            if (_options.RagStatuses is { } rags)
            {
                var rag = RagStatuses.Normalize(feature.RagStatus) ?? PlanningOptions.NoRag;
                if (!rags.Contains(rag))
                {
                    return false;
                }
            }

            return true;
        }

        private bool KeptBySearch(PlanningFeature feature)
        {
            if (!SearchActive)
            {
                return true;
            }

            if (Matches(feature.JiraId, feature.Title))
            {
                return true;
            }

            if (feature.BusinessOutcomeId is { } outcomeId && _matchingOutcomes.Contains(outcomeId))
            {
                return true;
            }

            if (feature.PortfolioEpicId is { } epicId)
            {
                if (_matchingEpics.Contains(epicId))
                {
                    return true;
                }

                if (_epicsById.TryGetValue(epicId, out var epic) && epic.StrategicObjectiveIds.Any(_matchingObjectives.Contains))
                {
                    return true;
                }
            }

            return _options.Lens == PlanningLens.Drawdown
                && feature.Teams.Any(t => _artIdsByTeam.TryGetValue(t.TeamId, out var arts) && arts.Any(_matchingArts.Contains));
        }

        private bool Matches(string? first, string? second) =>
            _search is not null
            && ((first is not null && first.Contains(_search, StringComparison.OrdinalIgnoreCase))
                || (second is not null && second.Contains(_search, StringComparison.OrdinalIgnoreCase)));

        private List<PlanningRow> BuildObjectives()
        {
            var featuresByOutcome = _features
                .GroupBy(f => f.BusinessOutcomeId ?? None)
                .ToDictionary(g => g.Key, g => g.ToList());

            var outcomesByEpic = new Dictionary<int, HashSet<int>>();
            foreach (var (outcomeKey, features) in featuresByOutcome)
            {
                Add(outcomesByEpic, EpicKeyOf(outcomeKey, features), outcomeKey);
            }

            var epicsByObjective = new Dictionary<int, HashSet<int>>();
            foreach (var epicKey in outcomesByEpic.Keys)
            {
                foreach (var objectiveKey in ObjectiveKeysOf(epicKey))
                {
                    Add(epicsByObjective, objectiveKey, epicKey);
                }
            }

            if (_options.IncludeEmptyNodes)
            {
                foreach (var objective in _data.Objectives.Where(o => o.ArtIds.Any(_scopedArtIds.Contains)))
                {
                    if (!SearchActive || _matchingObjectives.Contains(objective.Id))
                    {
                        epicsByObjective.TryAdd(objective.Id, []);
                    }
                }

                foreach (var epic in _data.Epics)
                {
                    foreach (var objectiveId in epic.StrategicObjectiveIds.Where(epicsByObjective.ContainsKey))
                    {
                        if (!SearchActive || _matchingEpics.Contains(epic.Id) || _matchingObjectives.Contains(objectiveId))
                        {
                            epicsByObjective[objectiveId].Add(epic.Id);
                            outcomesByEpic.TryAdd(epic.Id, []);
                        }
                    }
                }

                foreach (var outcome in _data.Outcomes.Where(o => o.PortfolioEpicId is { } epicId && outcomesByEpic.ContainsKey(epicId)))
                {
                    var epicId = outcome.PortfolioEpicId!.Value;
                    if (!SearchActive || _matchingOutcomes.Contains(outcome.Id) || _matchingEpics.Contains(epicId)
                        || EpicObjectiveIds(epicId).Any(_matchingObjectives.Contains))
                    {
                        outcomesByEpic[epicId].Add(outcome.Id);
                        featuresByOutcome.TryAdd(outcome.Id, []);
                    }
                }
            }

            var rows = epicsByObjective
                .Select(kv => BuildObjectiveRow(kv.Key, kv.Value, outcomesByEpic, featuresByOutcome))
                .ToList();

            return Sort(rows);
        }

        private PlanningRow BuildObjectiveRow(
            int objectiveKey,
            IEnumerable<int> epicKeys,
            Dictionary<int, HashSet<int>> outcomesByEpic,
            Dictionary<int, List<PlanningFeature>> featuresByOutcome)
        {
            var id = $"so:{objectiveKey}";
            var children = Sort(epicKeys
                .Select(epicKey => BuildEpicRow(id, objectiveKey, epicKey, outcomesByEpic.GetValueOrDefault(epicKey) ?? [], featuresByOutcome))
                .ToList());
            var features = children.SelectMany(c => c.Children).SelectMany(c => c.Children).Select(c => c.Feature!).ToList();
            var metrics = Aggregate(features, _ => true);
            var objective = _objectivesById.GetValueOrDefault(objectiveKey);

            return new PlanningRow
            {
                Id = id,
                Level = PlanningLevel.StrategicObjective,
                EntityId = objectiveKey == None ? null : objectiveKey,
                JiraId = objective?.JiraId,
                Title = objective?.Summary ?? NoObjectiveTitle,
                Status = objective?.Status,
                RagStatus = objective?.RagStatus,
                Depth = 0,
                Children = children,
                Metrics = metrics,
                SortRank = BestRank(children),
                HasArtRank = children.Any(c => c.HasArtRank),
                ShareOfCapacity = Share(metrics.PlannedSp, TotalCapacity()),
                TeamCells = Cells(features, _ => true),
            };
        }

        private PlanningRow BuildEpicRow(
            string parentId,
            int objectiveKey,
            int epicKey,
            IEnumerable<int> outcomeKeys,
            Dictionary<int, List<PlanningFeature>> featuresByOutcome)
        {
            var id = $"{parentId}/pe:{epicKey}";
            var children = Sort(outcomeKeys
                .Select(outcomeKey => BuildOutcomeRow(id, outcomeKey, featuresByOutcome.GetValueOrDefault(outcomeKey) ?? [], _ => true, 2))
                .ToList());
            var features = children.SelectMany(c => c.Children).Select(c => c.Feature!).ToList();
            var epic = _epicsById.GetValueOrDefault(epicKey);
            var ranks = RanksFor(ArtPrioritization.PortfolioEpic, epicKey);
            var metrics = Aggregate(features, _ => true);
            var alsoUnder = epic is null
                ? []
                : epic.StrategicObjectiveIds
                    .Where(soId => soId != objectiveKey)
                    .Select(soId => _objectivesById.TryGetValue(soId, out var so) ? so.JiraId ?? so.Summary : $"#{soId}")
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            return new PlanningRow
            {
                Id = id,
                Level = PlanningLevel.PortfolioEpic,
                EntityId = epicKey == None ? null : epicKey,
                JiraId = epic?.JiraId,
                Title = epic?.Summary ?? NoEpicTitle,
                Status = epic?.Status,
                RagStatus = epic?.RagStatus,
                FundingStatus = epic?.FundingStatus,
                TargetStart = epic?.TargetStart,
                TargetEnd = epic?.TargetEnd,
                Depth = 1,
                Children = children,
                Metrics = metrics,
                Ranks = ranks,
                SortRank = ranks.Count > 0 ? ranks.Min(r => r.Rank) : BestRank(children),
                HasArtRank = ranks.Count > 0 || children.Any(c => c.HasArtRank),
                IsBelowArtLine = ranks.Count > 0 && ranks.All(r => r.BelowLine),
                ShareOfCapacity = Share(metrics.PlannedSp, TotalCapacity()),
                AlsoUnder = alsoUnder,
                TeamCells = Cells(features, _ => true),
            };
        }

        private PlanningRow BuildOutcomeRow(
            string parentId,
            int outcomeKey,
            List<PlanningFeature> features,
            Func<PlanningFeatureTeam, bool> teamFilter,
            int depth,
            decimal? capacityForShare = null)
        {
            var id = $"{parentId}/bo:{outcomeKey}";
            var children = Sort(features.Select(f => BuildFeatureRow(id, f, teamFilter, depth + 1, capacityForShare)).ToList());
            var outcome = _outcomesById.GetValueOrDefault(outcomeKey);
            var ranks = RanksFor(ArtPrioritization.BusinessOutcome, outcomeKey);
            var metrics = Aggregate(features, teamFilter);

            return new PlanningRow
            {
                Id = id,
                Level = PlanningLevel.BusinessOutcome,
                EntityId = outcomeKey == None ? null : outcomeKey,
                JiraId = outcome?.JiraId,
                Title = outcome?.Summary ?? NoOutcomeTitle,
                Status = outcome?.Status,
                RagStatus = outcome?.RagStatus,
                RagExplain = outcome?.RagExplain,
                TargetStart = outcome?.TargetStart,
                TargetEnd = outcome?.TargetEnd,
                Depth = depth,
                Children = children,
                Metrics = metrics,
                Ranks = ranks,
                SortRank = ranks.Count > 0 ? ranks.Min(r => r.Rank) : BestRank(children),
                HasArtRank = ranks.Count > 0 || children.Any(c => c.HasArtRank),
                IsBelowArtLine = ranks.Count > 0 && ranks.All(r => r.BelowLine),
                ShareOfCapacity = Share(metrics.PlannedSp, capacityForShare ?? TotalCapacity()),
                TeamCells = Cells(features, teamFilter),
            };
        }

        private PlanningRow BuildFeatureRow(
            string parentId,
            PlanningFeature feature,
            Func<PlanningFeatureTeam, bool> teamFilter,
            int depth,
            decimal? capacityForShare)
        {
            var teams = feature.Teams.Where(teamFilter).ToList();
            var metrics = Aggregate([feature], teamFilter);

            return new PlanningRow
            {
                Id = $"{parentId}/f:{feature.Id}",
                Level = PlanningLevel.BusinessEpic,
                EntityId = feature.Id,
                JiraId = feature.JiraId,
                Title = feature.Title,
                Status = feature.Status,
                RagStatus = feature.RagStatus,
                RagExplain = feature.RagExplain,
                FundingStatus = feature.FundingStatus,
                TargetStart = feature.TargetStart,
                TargetEnd = feature.TargetEnd,
                Depth = depth,
                Metrics = metrics,
                SortRank = teams.Count == 0 ? null : teams.Min(t => t.BoardPosition),
                IsBelowArtLine = teams.Count > 0 && teams.All(t => t.BelowArtLine.Count > 0),
                ShareOfCapacity = Share(metrics.PlannedSp, capacityForShare ?? TotalCapacity()),
                TeamCells = Cells([feature], teamFilter),
                Feature = feature,
            };
        }

        private List<PlanningRow> BuildDrawdown()
        {
            var rows = new List<PlanningRow>();

            foreach (var art in _data.Arts.Where(a => _scopedArtIds.Contains(a.Id)).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                var artTeams = art.TeamIds.Where(_scopedTeamIds.Contains).ToHashSet();
                bool TeamFilter(PlanningFeatureTeam t) => artTeams.Contains(t.TeamId);
                var artFeatures = _features.Where(f => f.Teams.Any(TeamFilter)).ToList();
                var level = art.Level ?? ArtPrioritization.PortfolioEpic;
                var capacitySp = artTeams.Sum(id => _teamsById[id].CapacitySp);
                var capacityDays = artTeams.Sum(id => _teamsById[id].CapacityDays);
                var artRowId = $"art:{art.Id}";
                int ItemKey(PlanningFeature f) => level == ArtPrioritization.PortfolioEpic ? f.PortfolioEpicId ?? None : f.BusinessOutcomeId ?? None;

                var itemFeatures = artFeatures
                    .GroupBy(ItemKey)
                    .ToDictionary(g => g.Key, g => g.ToList());
                var plannedByItem = _scopeFeatures
                    .Where(f => f.Teams.Any(TeamFilter))
                    .GroupBy(ItemKey)
                    .ToDictionary(g => g.Key, g => PlannedByTeam(g, TeamFilter));

                if (_options.IncludeEmptyNodes)
                {
                    foreach (var entry in art.Order.Where(e => e.ItemId != None && !itemFeatures.ContainsKey(e.ItemId)))
                    {
                        if (!SearchActive || ItemMatches(level, entry.ItemId) || _matchingArts.Contains(art.Id))
                        {
                            itemFeatures[entry.ItemId] = [];
                        }
                    }
                }

                var ordered = itemFeatures.Keys
                    .Union(plannedByItem.Keys)
                    .Select(key => (Key: key, Entry: art.OrderByItem.GetValueOrDefault(key)))
                    .OrderBy(x => x.Entry is null ? 1 : x.Entry.IsIncluded ? 0 : 2)
                    .ThenBy(x => x.Entry?.Rank ?? int.MaxValue)
                    .ThenBy(x => ItemJiraId(level, x.Key), JiraKeys)
                    .ThenBy(x => x.Key)
                    .ToList();

                var children = new List<PlanningRow>();
                var cumulative = 0m;
                var teamCumulative = artTeams.ToDictionary(id => id, _ => 0m);
                var runsOutMarked = false;
                var lineInserted = false;

                foreach (var (key, entry) in ordered)
                {
                    var below = entry is { IsIncluded: false };
                    var over = false;
                    var runsOutHere = false;
                    if (!below)
                    {
                        if (plannedByItem.TryGetValue(key, out var planned))
                        {
                            foreach (var (teamId, sp) in planned)
                            {
                                cumulative += sp;
                                teamCumulative[teamId] += sp;
                            }
                        }

                        over = cumulative > capacitySp;
                        runsOutHere = over && !runsOutMarked;
                        runsOutMarked |= over;
                    }

                    if (!itemFeatures.TryGetValue(key, out var features))
                    {
                        continue;
                    }

                    if (below && !lineInserted)
                    {
                        children.Add(LineMarker(artRowId, art, capacitySp, capacityDays));
                        lineInserted = true;
                    }

                    var row = BuildItemRow(artRowId, art, level, key, entry, features, TeamFilter, capacitySp);
                    if (below)
                    {
                        children.Add(row with { IsBelowArtLine = true });
                        continue;
                    }

                    children.Add(row with
                    {
                        CumulativePlannedSp = cumulative,
                        OverCapacity = over,
                        CapacityRunsOutHere = runsOutHere,
                        TeamCells = CumulativeCells(row.TeamCells, artTeams, teamCumulative),
                    });
                }

                if (!lineInserted && art.OrderMode != ArtOrderMode.Empty)
                {
                    children.Add(LineMarker(artRowId, art, capacitySp, capacityDays));
                }

                var metrics = Aggregate(artFeatures, TeamFilter);
                rows.Add(new PlanningRow
                {
                    Id = artRowId,
                    Level = PlanningLevel.Art,
                    EntityId = art.Id,
                    ArtId = art.Id,
                    Title = art.Name,
                    Depth = 0,
                    Children = children,
                    Metrics = metrics,
                    CapacitySp = capacitySp,
                    CapacityDays = capacityDays,
                    ShareOfCapacity = Share(metrics.PlannedSp, capacitySp),
                    CumulativePlannedSp = cumulative,
                    OverCapacity = cumulative > capacitySp,
                    TeamCells = CumulativeCells(Cells(artFeatures, TeamFilter), artTeams, teamCumulative),
                });
            }

            return rows;
        }

        private Dictionary<int, PlanningTeamCell> CumulativeCells(
            IReadOnlyDictionary<int, PlanningTeamCell> cells,
            IEnumerable<int> teamIds,
            Dictionary<int, decimal> teamCumulative)
        {
            var result = new Dictionary<int, PlanningTeamCell>();
            foreach (var teamId in teamIds)
            {
                var cell = cells.GetValueOrDefault(teamId) ?? new PlanningTeamCell(0m, 0m, null, false);
                result[teamId] = cell with
                {
                    CumulativeSp = teamCumulative[teamId],
                    OverCapacity = teamCumulative[teamId] > _teamsById[teamId].CapacitySp,
                };
            }

            return result;
        }

        private static Dictionary<int, decimal> PlannedByTeam(IEnumerable<PlanningFeature> features, Func<PlanningFeatureTeam, bool> teamFilter)
        {
            var planned = new Dictionary<int, decimal>();
            foreach (var feature in features.DistinctBy(f => f.Id))
            {
                foreach (var team in feature.Teams.Where(teamFilter).Where(t => t.IsPlanned))
                {
                    planned[team.TeamId] = planned.GetValueOrDefault(team.TeamId) + team.DemandSp;
                }
            }

            return planned;
        }

        private PlanningRow BuildItemRow(
            string parentId,
            PlanningArt art,
            ArtPrioritization level,
            int key,
            ArtOrderEntry? entry,
            List<PlanningFeature> features,
            Func<PlanningFeatureTeam, bool> teamFilter,
            decimal capacitySp)
        {
            IReadOnlyList<PlanningRank> ranks = entry is null
                ? []
                : [new PlanningRank(art.Id, art.Abbreviation, art.Name, entry.Rank, !entry.IsIncluded, entry.Origin != ArtOrderOrigin.New)];

            if (level == ArtPrioritization.BusinessOutcome)
            {
                var outcomeRow = BuildOutcomeRow(parentId, key, features, teamFilter, 1, capacitySp);
                return outcomeRow with { Ranks = ranks, SortRank = entry?.Rank ?? outcomeRow.SortRank, HasArtRank = entry is not null, IsBelowArtLine = false };
            }

            var id = $"{parentId}/pe:{key}";
            var children = Sort(features
                .GroupBy(f => f.BusinessOutcomeId ?? None)
                .Select(g => BuildOutcomeRow(id, g.Key, g.ToList(), teamFilter, 2, capacitySp))
                .Select(row => row with { Ranks = [], IsBelowArtLine = false, SortRank = MinRank(row.Children), HasArtRank = false })
                .ToList());
            var epic = _epicsById.GetValueOrDefault(key);
            var metrics = Aggregate(features, teamFilter);

            return new PlanningRow
            {
                Id = id,
                Level = PlanningLevel.PortfolioEpic,
                EntityId = key == None ? null : key,
                JiraId = epic?.JiraId,
                Title = epic?.Summary ?? NoEpicTitle,
                Status = epic?.Status,
                RagStatus = epic?.RagStatus,
                FundingStatus = epic?.FundingStatus,
                TargetStart = epic?.TargetStart,
                TargetEnd = epic?.TargetEnd,
                Depth = 1,
                Children = children,
                Metrics = metrics,
                Ranks = ranks,
                SortRank = entry?.Rank,
                HasArtRank = entry is not null,
                ShareOfCapacity = Share(metrics.PlannedSp, capacitySp),
                TeamCells = Cells(features, teamFilter),
            };
        }

        private static PlanningRow LineMarker(string parentId, PlanningArt art, decimal capacitySp, double capacityDays) => new()
        {
            Id = $"{parentId}/line",
            Kind = PlanningRowKind.LineMarker,
            Level = PlanningLevel.Art,
            ArtId = art.Id,
            Title = $"{art.Name} line",
            Depth = 1,
            CapacitySp = capacitySp,
            CapacityDays = capacityDays,
        };

        private bool ItemMatches(ArtPrioritization level, int itemId) => level == ArtPrioritization.PortfolioEpic
            ? _matchingEpics.Contains(itemId)
            : _matchingOutcomes.Contains(itemId);

        private string? ItemJiraId(ArtPrioritization level, int key) => level == ArtPrioritization.PortfolioEpic
            ? _epicsById.GetValueOrDefault(key)?.JiraId
            : _outcomesById.GetValueOrDefault(key)?.JiraId;

        private int EpicKeyOf(int outcomeKey, List<PlanningFeature> features)
        {
            if (outcomeKey == None)
            {
                return None;
            }

            if (_outcomesById.TryGetValue(outcomeKey, out var outcome))
            {
                return outcome.PortfolioEpicId ?? None;
            }

            return features.Select(f => f.PortfolioEpicId).FirstOrDefault(id => id is not null) ?? None;
        }

        private IEnumerable<int> ObjectiveKeysOf(int epicKey)
        {
            var ids = EpicObjectiveIds(epicKey);
            return ids.Count == 0 ? [None] : ids;
        }

        private IReadOnlyList<int> EpicObjectiveIds(int epicKey) =>
            epicKey != None && _epicsById.TryGetValue(epicKey, out var epic) ? epic.StrategicObjectiveIds : [];

        private List<PlanningRank> RanksFor(ArtPrioritization level, int itemKey) =>
            _data.Arts
                .Where(a => _scopedArtIds.Contains(a.Id) && a.Level == level && a.OrderByItem.ContainsKey(itemKey))
                .Select(a =>
                {
                    var entry = a.OrderByItem[itemKey];
                    return new PlanningRank(a.Id, a.Abbreviation, a.Name, entry.Rank, !entry.IsIncluded, entry.Origin != ArtOrderOrigin.New);
                })
                .OrderBy(r => r.ArtName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static int? MinRank(IEnumerable<PlanningRow> rows)
        {
            int? min = null;
            foreach (var row in rows)
            {
                if (row.SortRank is { } rank && (min is null || rank < min))
                {
                    min = rank;
                }
            }

            return min;
        }

        private static int? BestRank(IReadOnlyList<PlanningRow> rows) =>
            MinRank(rows.Any(r => r.HasArtRank) ? rows.Where(r => r.HasArtRank) : rows);

        private decimal TotalCapacity() => _scopedTeamIds.Sum(id => _teamsById[id].CapacitySp);

        private static double? Share(decimal planned, decimal capacity) =>
            capacity > 0m ? (double)(planned / capacity) : null;

        private static Dictionary<int, PlanningTeamCell> Cells(IEnumerable<PlanningFeature> features, Func<PlanningFeatureTeam, bool> teamFilter)
        {
            var demand = new Dictionary<int, decimal>();
            var planned = new Dictionary<int, decimal>();
            foreach (var feature in features.DistinctBy(f => f.Id))
            {
                foreach (var team in feature.Teams.Where(teamFilter))
                {
                    demand[team.TeamId] = demand.GetValueOrDefault(team.TeamId) + team.DemandSp;
                    if (team.IsPlanned)
                    {
                        planned[team.TeamId] = planned.GetValueOrDefault(team.TeamId) + team.DemandSp;
                    }
                }
            }

            return demand.ToDictionary(kv => kv.Key, kv => new PlanningTeamCell(kv.Value, planned.GetValueOrDefault(kv.Key), null, false));
        }

        private PlanningMetrics Aggregate(IEnumerable<PlanningFeature> features, Func<PlanningFeatureTeam, bool> teamFilter)
        {
            var count = 0;
            var done = 0;
            var inProgress = 0;
            var blocked = 0;
            var notStarted = 0;
            var demand = 0m;
            var planned = 0m;
            var reserved = 0m;
            HashSet<string>? dependencies = null;
            HashSet<string>? dependenciesAtRisk = null;
            var featuresAtRisk = 0;
            var hygiene = 0;
            var teams = new HashSet<int>();
            var arts = new HashSet<int>();
            string? worstRag = null;
            var worstRank = int.MaxValue;
            DateTime? targetEnd = null;

            foreach (var feature in features.DistinctBy(f => f.Id))
            {
                count++;
                switch (feature.StatusClass)
                {
                    case PlanningStatusClass.Done:
                        done++;
                        break;
                    case PlanningStatusClass.Blocked:
                        blocked++;
                        break;
                    case PlanningStatusClass.InProgress:
                        inProgress++;
                        break;
                    default:
                        notStarted++;
                        break;
                }

                foreach (var team in feature.Teams.Where(teamFilter))
                {
                    demand += team.DemandSp;
                    if (team.IsPlanned)
                    {
                        planned += team.DemandSp;
                    }

                    reserved += team.ReservedSp;
                    teams.Add(team.TeamId);
                    if (_artIdsByTeam.TryGetValue(team.TeamId, out var teamArts))
                    {
                        arts.UnionWith(teamArts.Where(_scopedArtIds.Contains));
                    }
                }

                if (feature.DependencyIds.Count > 0)
                {
                    (dependencies ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(feature.DependencyIds);
                }

                if (feature.RiskyDependencyIds.Count > 0)
                {
                    (dependenciesAtRisk ??= new HashSet<string>(StringComparer.Ordinal)).UnionWith(feature.RiskyDependencyIds);
                    featuresAtRisk++;
                }

                if (feature.HygieneFailures > 0)
                {
                    hygiene++;
                }

                var ragRank = RagRank(feature.RagStatus);
                if (ragRank < 3 && ragRank < worstRank)
                {
                    worstRank = ragRank;
                    worstRag = RagStatuses.Normalize(feature.RagStatus);
                }

                if (feature.TargetEnd is { } end && (targetEnd is null || end > targetEnd))
                {
                    targetEnd = end;
                }
            }

            return new PlanningMetrics(
                count, done, inProgress, blocked, notStarted,
                demand, planned, demand - planned, reserved,
                dependencies?.Count ?? 0, dependenciesAtRisk?.Count ?? 0, featuresAtRisk, hygiene,
                teams.Count, arts.Count, worstRag, targetEnd);
        }

        private List<PlanningRow> Sort(List<PlanningRow> rows) => SortRows(rows, _options.SortBy, _options.SortDescending);

        private static void Add(Dictionary<int, HashSet<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var set))
            {
                map[key] = set = [];
            }

            set.Add(value);
        }
    }

    public static List<PlanningRow> SortRows(IEnumerable<PlanningRow> rows, PlanningSort sortBy, bool descending)
    {
        Comparison<PlanningRow> primary = sortBy switch
        {
            PlanningSort.Title => (a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase),
            PlanningSort.JiraId => (a, b) => JiraKeys.Compare(a.JiraId, b.JiraId),
            PlanningSort.Features => (a, b) => a.Metrics.Features.CompareTo(b.Metrics.Features),
            PlanningSort.Progress => (a, b) => a.Metrics.Progress.CompareTo(b.Metrics.Progress),
            PlanningSort.Demand => (a, b) => a.Metrics.DemandSp.CompareTo(b.Metrics.DemandSp),
            PlanningSort.Planned => (a, b) => a.Metrics.PlannedSp.CompareTo(b.Metrics.PlannedSp),
            PlanningSort.Unplanned => (a, b) => a.Metrics.UnplannedSp.CompareTo(b.Metrics.UnplannedSp),
            PlanningSort.Rag => (a, b) => RagRank(a.RagStatus ?? a.Metrics.WorstRag).CompareTo(RagRank(b.RagStatus ?? b.Metrics.WorstRag)),
            PlanningSort.Dependencies => (a, b) =>
            {
                var risk = a.Metrics.DependenciesAtRisk.CompareTo(b.Metrics.DependenciesAtRisk);
                return risk != 0 ? risk : a.Metrics.Dependencies.CompareTo(b.Metrics.Dependencies);
            },
            PlanningSort.Hygiene => (a, b) => a.Metrics.HygieneFailingFeatures.CompareTo(b.Metrics.HygieneFailingFeatures),
            PlanningSort.TargetEnd => (a, b) => Nullable.Compare(a.ShownTargetEnd, b.ShownTargetEnd),
            PlanningSort.Share => (a, b) => Nullable.Compare(a.ShareOfCapacity, b.ShareOfCapacity),
            _ => (a, b) => (a.SortRank ?? int.MaxValue).CompareTo(b.SortRank ?? int.MaxValue),
        };

        var direction = descending ? -1 : 1;
        return rows
            .OrderBy(r => BlankOrder(r, sortBy))
            .ThenBy(r => r, Comparer<PlanningRow>.Create((a, b) => direction * primary(a, b)))
            .ThenBy(r => r.JiraId, JiraKeys)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int BlankOrder(PlanningRow row, PlanningSort sortBy) => sortBy switch
    {
        PlanningSort.Priority => row.IsBucket ? 3 : row.SortRank is null ? 2 : row.HasArtRank ? 0 : 1,
        PlanningSort.TargetEnd => row.ShownTargetEnd is null ? 1 : 0,
        PlanningSort.Share => row.ShareOfCapacity is null ? 1 : 0,
        _ => 0,
    };
}
