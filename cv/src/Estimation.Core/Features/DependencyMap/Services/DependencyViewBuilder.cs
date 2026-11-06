using System.Globalization;
using System.Text;
using Estimation.Core.Features.Models;

namespace Estimation.Core.Features.DependencyMap.Services;

public enum CardState
{
    Normal = 0,
    Context = 1,
    Focus = 2,
    Chain = 3,
    Dimmed = 4
}

public enum ChainMode
{
    Direct = 0,
    WholeChain = 1
}

public sealed record DependencySelection(string? ConsumerLaneId, string? ProviderLaneId)
{
    public static readonly DependencySelection None = new(null, null);

    public bool IsEmpty => ConsumerLaneId is null && ProviderLaneId is null;
}

public sealed record DependencyFocus(string NodeId, ChainMode Chain, string? DependencyId);

public sealed record DependencyViewState(
    DependencyFilter Filter,
    FeatureCardFilter FeatureFilter,
    string? MyTeamLaneId,
    DependencySelection Selection,
    DependencyFocus? Focus);

public sealed record RiskSummary(int Total, int OutOfOrder, int AtRisk, int InOrder, int NotChecked);

public sealed record MatrixCell(
    int Count,
    int OutOfOrder,
    int AtRisk,
    int InOrder,
    int NotChecked,
    int Related,
    DependencyRisk Worst,
    string Tooltip);

public sealed record TeamMatrix(
    IReadOnlyList<DependencyLane> Lanes,
    IReadOnlyDictionary<(string Row, string Column), MatrixCell> Cells,
    IReadOnlyDictionary<string, int> RowTotals,
    IReadOnlyDictionary<string, int> ColumnTotals,
    int Total,
    int RelatedTotal);

public sealed record DependencyRow(
    FeatureDependency Dependency,
    string Relation,
    string ConsumerTeams,
    string ProviderTeams,
    string ConsumerPlan,
    string ProviderPlan,
    int ConsumerPlanSort,
    int ProviderPlanSort,
    string ProviderScope);

public sealed record BoardCard(
    string NodeId,
    int FeatureId,
    string? JiraId,
    string Title,
    string? Status,
    string? RagStatus,
    DependencyEndKind Kind,
    string? ScopeText,
    string LaneId,
    int ColumnStart,
    int ColumnEnd,
    int Row,
    string SprintText,
    string SprintTooltip,
    bool Approximate,
    bool BelowLine,
    int ExtraTeams,
    string TeamsText,
    int Needs,
    int NeededBy,
    int Related,
    int OtherLinks,
    DependencyRisk WorstRisk,
    bool StatusProblem,
    string? FreeText,
    bool ExternalDependencies,
    CardState State,
    bool AddedByFocus);

public sealed record BoardLaneView(
    DependencyLane Lane,
    int RowCount,
    int NeedsAtRisk,
    int AtRiskForOthers,
    IReadOnlyList<BoardCard> Cards);

public sealed record BoardLine(
    string Key,
    string FromNodeId,
    string ToNodeId,
    bool Directed,
    bool BothWays,
    DependencyRisk Risk,
    int LinkCount,
    bool InFocus,
    string Title,
    IReadOnlyList<string> DependencyIds);

public sealed record BoardLayout(
    IReadOnlyList<BoardColumn> Columns,
    IReadOnlyList<BoardLaneView> Lanes,
    IReadOnlyList<BoardLine> Lines,
    string LinesVersion);

public sealed record DetailsTeamPlan(string TeamName, bool IsPrimary, string PlanText, bool BelowLine, bool Approximate);

public sealed record DetailsRow(
    string DependencyId,
    string OtherNodeId,
    string OtherKey,
    string? OtherTitle,
    string? OtherStatus,
    string? OtherRagStatus,
    string OtherTeams,
    string OtherPlan,
    string Phrase,
    DependencyRisk Risk,
    string Reason,
    bool OtherIsCard,
    bool HiddenByFilters,
    bool Selected);

public sealed record DetailsPanel(
    string NodeId,
    DependencyFeature Feature,
    string? ScopeText,
    IReadOnlyList<DetailsTeamPlan> Plan,
    IReadOnlyList<DetailsRow> Needs,
    IReadOnlyList<DetailsRow> NeededBy,
    IReadOnlyList<DetailsRow> Related,
    IReadOnlyList<DetailsRow> OtherLinks,
    int ChainUpstream,
    int ChainDownstream,
    bool HiddenByFilters);

public sealed record DependencyMapView(
    RiskSummary Summary,
    TeamMatrix Matrix,
    IReadOnlyList<DependencyRow> Rows,
    BoardLayout Board,
    DetailsPanel? Panel,
    int HiddenUnlinked,
    IReadOnlySet<string> FocusNodeIds,
    IReadOnlySet<string> FocusLineKeys);

public static class DependencyViewBuilder
{
    public const int ManyLinesThreshold = 150;

    private const string Dash = "—";
    private const string RangeDash = "–";
    private const string NoTeamText = "(No team)";
    private const string ThisArtAndPi = "This ART and PI";
    private const string NotPlannedText = "Not planned";
    private const string TeamNotPlannedText = "not planned";
    private const string BelowLineSuffix = " · below the line";
    private const string ApproximateNote = "≈ the team's sprint dates do not line up with the ART sprints";

    public static string KindLabel(DependencyStubKind kind) => kind switch
    {
        DependencyStubKind.BusinessOutcome => "Business outcome",
        DependencyStubKind.PortfolioEpic => "Portfolio epic",
        DependencyStubKind.StrategicObjective => "Strategic objective",
        _ => "Not a feature in the tool"
    };

    public static DependencyMapView Build(DependencyIndex index, DependencyViewState state)
    {
        var context = new ViewContext(
            index,
            new Lookup(index),
            new DependencySelection(Blank(state.Selection.ConsumerLaneId), Blank(state.Selection.ProviderLaneId)),
            Blank(state.MyTeamLaneId));

        var matching = MatchingFeatures(index, state.FeatureFilter);
        var linked = LinkFiltered(index, state.Filter);
        var withMatch = WithMatchingEnd(linked, matching);
        var scoped = Shown(context, withMatch, state.Filter.ShowWithinTeam);
        var summary = Summarize(scoped);
        var riskFiltered = RiskFiltered(withMatch, state.Filter.Risks);
        var matrix = BuildMatrix(context, riskFiltered, state.Filter);
        var shown = RiskFiltered(scoped, state.Filter.Risks);
        var rows = BuildRows(context, shown);
        var unlinked = Unlinked(index, matching, withMatch, context.MyTeam);
        var cards = CardNodeIds(shown, unlinked, state.Filter.ShowUnlinked && context.Selection.IsEmpty);
        var focus = BuildFocus(index, linked, cards, state.Focus);
        var addedByFocus = AddFocusCards(index, cards, focus);
        var visible = VisibleDependencies(linked, shown, focus);
        var board = BuildBoard(context, matching, shown, visible, TypeFiltered(index, state.Filter), cards, addedByFocus, focus);
        var panel = focus is null ? null : BuildPanel(context, linked, focus, state.Focus?.DependencyId);
        IReadOnlySet<string> focusNodeIds = focus?.Chain ?? new HashSet<string>(StringComparer.Ordinal);
        IReadOnlySet<string> focusLineKeys = board.Lines
            .Where(l => l.InFocus)
            .Select(l => l.Key)
            .ToHashSet(StringComparer.Ordinal);

        return new DependencyMapView(
            summary,
            matrix,
            rows,
            board,
            panel,
            context.Selection.IsEmpty ? unlinked.Count : 0,
            focusNodeIds,
            focusLineKeys);
    }

    private static HashSet<string> MatchingFeatures(DependencyIndex index, FeatureCardFilter filter) =>
        index.Nodes.Values
            .Where(n => n.Feature is { Kind: DependencyEndKind.InScope } feature && filter.Matches(feature))
            .Select(n => n.NodeId)
            .ToHashSet(StringComparer.Ordinal);

    private static List<FeatureDependency> LinkFiltered(DependencyIndex index, DependencyFilter filter) =>
        index.Dependencies
            .Where(d => EndAllowed(d.Provider, filter)
                && EndAllowed(d.Consumer, filter)
                && (filter.ShowWithinTeam || !d.WithinTeam))
            .Select(d => WithLinkTypes(d, filter))
            .OfType<FeatureDependency>()
            .ToList();

    private static List<FeatureDependency> TypeFiltered(DependencyIndex index, DependencyFilter filter) =>
        index.Dependencies
            .Select(d => WithLinkTypes(d, filter))
            .OfType<FeatureDependency>()
            .ToList();

    private static FeatureDependency? WithLinkTypes(FeatureDependency dependency, DependencyFilter filter)
    {
        if (!filter.FiltersLinkTypes)
        {
            return dependency;
        }

        var kept = dependency.Links.Where(l => filter.ShowsLinkType(l.TypeName)).ToList();

        if (kept.Count == 0)
        {
            return null;
        }

        if (kept.Count == dependency.Links.Count)
        {
            return dependency;
        }

        if (dependency.Directed && kept.Any(l => l.Order != LinkOrder.Unordered))
        {
            return dependency with { Links = kept };
        }

        var swap = string.CompareOrdinal(dependency.Provider.NodeId, dependency.Consumer.NodeId) > 0;

        return dependency with
        {
            Provider = swap ? dependency.Consumer : dependency.Provider,
            Consumer = swap ? dependency.Provider : dependency.Consumer,
            Directed = false,
            InCycle = false,
            Links = swap
                ? kept.Select(l => l with { ProviderPhrase = l.ConsumerPhrase, ConsumerPhrase = l.ProviderPhrase }).ToList()
                : kept,
            Verdict = DependencyRiskRule.Unordered(kept[0].Outward)
        };
    }

    private static bool EndAllowed(DependencyNode node, DependencyFilter filter) => node.Feature?.Kind switch
    {
        null => filter.ShowNotInTool,
        DependencyEndKind.OtherPi => filter.ShowOtherPi,
        DependencyEndKind.OtherArt => filter.ShowOtherArt,
        _ => true
    };

    private static List<FeatureDependency> WithMatchingEnd(
        IReadOnlyList<FeatureDependency> linked,
        IReadOnlySet<string> matching) =>
        linked
            .Where(d => matching.Contains(d.Provider.NodeId) || matching.Contains(d.Consumer.NodeId))
            .ToList();

    private static RiskSummary Summarize(IReadOnlyList<FeatureDependency> dependencies) =>
        new(
            dependencies.Count,
            dependencies.Count(d => d.Verdict.Risk == DependencyRisk.OutOfOrder),
            dependencies.Count(d => d.Verdict.Risk == DependencyRisk.AtRisk),
            dependencies.Count(d => d.Verdict.Risk == DependencyRisk.InOrder),
            dependencies.Count(d => d.Verdict.Risk == DependencyRisk.NotChecked));

    private static List<FeatureDependency> RiskFiltered(
        IReadOnlyList<FeatureDependency> dependencies,
        IReadOnlyCollection<DependencyRisk> risks)
    {
        var selected = risks.ToHashSet();
        return selected.Count == 0
            ? dependencies.ToList()
            : dependencies.Where(d => selected.Contains(d.Verdict.Risk)).ToList();
    }

    private static TeamMatrix BuildMatrix(
        ViewContext context,
        IReadOnlyList<FeatureDependency> dependencies,
        DependencyFilter filter)
    {
        var index = context.Index;
        var showNotInTool = filter.ShowNotInTool;
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dependency in dependencies)
        {
            used.UnionWith(dependency.Provider.LaneIds);
            used.UnionWith(dependency.Consumer.LaneIds);
        }

        var lanes = index.Lanes
            .Where(l => l.Kind == DependencyLaneKind.ArtTeam
                || (used.Contains(l.LaneId) && (l.Kind != DependencyLaneKind.NotInTool || showNotInTool)))
            .ToList();
        var names = lanes.ToDictionary(l => l.LaneId, l => l.Name, StringComparer.Ordinal);
        var rowTotals = lanes.ToDictionary(l => l.LaneId, _ => 0, StringComparer.Ordinal);
        var columnTotals = lanes.ToDictionary(l => l.LaneId, _ => 0, StringComparer.Ordinal);
        var tallies = new Dictionary<(string Row, string Column), CellTally>();
        var total = 0;
        var relatedTotal = 0;

        foreach (var dependency in dependencies)
        {
            var lanePairs = LanePairs(context.Lookup, dependency, filter.ShowWithinTeam);

            if (!dependency.Directed)
            {
                relatedTotal++;
                var pairs = new HashSet<(string, string)>();

                foreach (var (x, y) in lanePairs)
                {
                    pairs.Add((x, y));
                    pairs.Add((y, x));
                }

                foreach (var pair in pairs.Where(p => names.ContainsKey(p.Item1) && names.ContainsKey(p.Item2)))
                {
                    TallyOf(pair).Related++;
                }

                continue;
            }

            total++;
            var counted = lanePairs.Where(p => names.ContainsKey(p.Row) && names.ContainsKey(p.Column)).ToList();

            foreach (var row in counted.Select(p => p.Row).Distinct(StringComparer.Ordinal))
            {
                rowTotals[row]++;
            }

            foreach (var column in counted.Select(p => p.Column).Distinct(StringComparer.Ordinal))
            {
                columnTotals[column]++;
            }

            foreach (var pair in counted)
            {
                TallyOf(pair).Add(dependency.Verdict.Risk);
            }
        }

        var cells = new Dictionary<(string Row, string Column), MatrixCell>();

        foreach (var row in lanes)
        {
            foreach (var column in lanes)
            {
                if (tallies.TryGetValue((row.LaneId, column.LaneId), out var tally))
                {
                    cells[(row.LaneId, column.LaneId)] = tally.ToCell(row, column);
                }
            }
        }

        return new TeamMatrix(lanes, cells, rowTotals, columnTotals, total, relatedTotal);

        CellTally TallyOf((string Row, string Column) key)
        {
            if (!tallies.TryGetValue(key, out var tally))
            {
                tally = new CellTally();
                tallies[key] = tally;
            }

            return tally;
        }
    }

    private static HashSet<(string Row, string Column)> LanePairs(
        Lookup lookup,
        FeatureDependency dependency,
        bool showWithinTeam)
    {
        var pairs = new HashSet<(string Row, string Column)>();

        foreach (var (consumerTeam, row) in TeamLanes(lookup, dependency.Consumer))
        {
            foreach (var (providerTeam, column) in TeamLanes(lookup, dependency.Provider))
            {
                if (showWithinTeam || consumerTeam is null || consumerTeam != providerTeam)
                {
                    pairs.Add((row, column));
                }
            }
        }

        return pairs;
    }

    private static IEnumerable<(int? TeamId, string LaneId)> TeamLanes(Lookup lookup, DependencyNode node) =>
        node.Feature is { Teams.Count: > 0 } feature
            ? feature.Teams.Select(t => ((int?)t.TeamId, lookup.LaneOfTeam(t.TeamId)))
            : node.LaneIds.Select(l => ((int?)null, l));

    private static List<FeatureDependency> Shown(
        ViewContext context,
        IReadOnlyList<FeatureDependency> dependencies,
        bool showWithinTeam)
    {
        var selection = context.Selection;
        var myTeam = context.MyTeam;

        if (!selection.IsEmpty && !showWithinTeam)
        {
            return dependencies
                .Where(d => LanePairs(context.Lookup, d, false)
                    .Any(p => InSelectedCell(p.Row, p.Column) || (!d.Directed && InSelectedCell(p.Column, p.Row))))
                .ToList();
        }

        if (!selection.IsEmpty)
        {
            return dependencies
                .Where(d => InSelection(d.Consumer, d.Provider) || (!d.Directed && InSelection(d.Provider, d.Consumer)))
                .ToList();
        }

        if (myTeam is not null)
        {
            return dependencies
                .Where(d => d.Consumer.LaneIds.Contains(myTeam) || d.Provider.LaneIds.Contains(myTeam))
                .ToList();
        }

        return dependencies.ToList();

        bool InSelection(DependencyNode consumer, DependencyNode provider) =>
            (selection.ConsumerLaneId is null || consumer.LaneIds.Contains(selection.ConsumerLaneId))
            && (selection.ProviderLaneId is null || provider.LaneIds.Contains(selection.ProviderLaneId));

        bool InSelectedCell(string row, string column) =>
            (selection.ConsumerLaneId is null || row == selection.ConsumerLaneId)
            && (selection.ProviderLaneId is null || column == selection.ProviderLaneId);
    }

    private static List<DependencyRow> BuildRows(ViewContext context, IReadOnlyList<FeatureDependency> shown)
    {
        var artSprints = context.Index.Data.ArtSprints;

        return shown
            .Select(d => new DependencyRow(
                d,
                Phrases(d.Links.Select(l => l.ConsumerPhrase)),
                TeamNames(d.Consumer, context.Lookup),
                TeamNames(d.Provider, context.Lookup),
                d.Consumer.IsCard ? SprintText(d.Consumer, artSprints) : Dash,
                d.Provider.IsCard ? SprintText(d.Provider, artSprints) : Dash,
                d.Consumer.Plan?.First ?? int.MaxValue,
                d.Provider.Plan?.Last ?? int.MaxValue,
                ProviderScope(d.Provider)))
            .OrderByDescending(r => r.Dependency.Verdict.Risk)
            .ThenBy(r => context.Lookup.LaneName(r.Dependency.Consumer.LaneIds[0]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Dependency.Consumer.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Dependency.Provider.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Dependency.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> Unlinked(
        DependencyIndex index,
        IReadOnlySet<string> matching,
        IReadOnlyList<FeatureDependency> withMatch,
        string? myTeam)
    {
        var ends = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dependency in withMatch)
        {
            ends.Add(dependency.Provider.NodeId);
            ends.Add(dependency.Consumer.NodeId);
        }

        return matching
            .Where(id => !ends.Contains(id) && (myTeam is null || index.Nodes[id].LaneIds.Contains(myTeam)))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static HashSet<string> CardNodeIds(
        IReadOnlyList<FeatureDependency> shown,
        IReadOnlyList<string> unlinked,
        bool showUnlinked)
    {
        var cards = new HashSet<string>(StringComparer.Ordinal);

        foreach (var dependency in shown)
        {
            if (dependency.Provider.IsCard)
            {
                cards.Add(dependency.Provider.NodeId);
            }

            if (dependency.Consumer.IsCard)
            {
                cards.Add(dependency.Consumer.NodeId);
            }
        }

        if (showUnlinked)
        {
            cards.UnionWith(unlinked);
        }

        return cards;
    }

    private static FocusResult? BuildFocus(
        DependencyIndex index,
        IReadOnlyList<FeatureDependency> linked,
        IReadOnlySet<string> cards,
        DependencyFocus? focus)
    {
        if (focus is null || !index.Nodes.TryGetValue(focus.NodeId, out var node) || !node.IsCard)
        {
            return null;
        }

        var nodeId = node.NodeId;
        var touching = linked.Where(d => d.Provider.NodeId == nodeId || d.Consumer.NodeId == nodeId).ToList();
        var edges = linked.Where(d => d.Directed && d.Provider.IsCard && d.Consumer.IsCard).Select(d => (d.Provider.NodeId, d.Consumer.NodeId)).ToList();
        var upstream = DependencyGraph.Walk(edges, nodeId, upstream: true);
        var downstream = DependencyGraph.Walk(edges, nodeId, upstream: false);
        var chain = new HashSet<string>(StringComparer.Ordinal) { nodeId };

        foreach (var dependency in touching)
        {
            chain.Add(dependency.Provider.NodeId);
            chain.Add(dependency.Consumer.NodeId);
        }

        var chainDependencyIds = touching.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        if (focus.Chain == ChainMode.WholeChain)
        {
            chain.UnionWith(upstream.Keys);
            chain.UnionWith(downstream.Keys);

            foreach (var dependency in linked.Where(d =>
                         d.Directed && chain.Contains(d.Provider.NodeId) && chain.Contains(d.Consumer.NodeId)))
            {
                chainDependencyIds.Add(dependency.Id);
            }
        }

        return new FocusResult(
            nodeId,
            chain,
            chainDependencyIds,
            upstream.Count,
            downstream.Count,
            !cards.Contains(nodeId));
    }

    private static HashSet<string> AddFocusCards(DependencyIndex index, HashSet<string> cards, FocusResult? focus)
    {
        var added = new HashSet<string>(StringComparer.Ordinal);

        if (focus is null)
        {
            return added;
        }

        foreach (var nodeId in focus.Chain.Where(id => index.Nodes[id].IsCard && !cards.Contains(id)))
        {
            added.Add(nodeId);
        }

        cards.UnionWith(added);
        return added;
    }

    private static List<FeatureDependency> VisibleDependencies(
        IReadOnlyList<FeatureDependency> linked,
        IReadOnlyList<FeatureDependency> shown,
        FocusResult? focus)
    {
        var ids = shown.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        if (focus is not null)
        {
            ids.UnionWith(focus.ChainDependencyIds);
        }

        return linked.Where(d => ids.Contains(d.Id)).ToList();
    }

    private static BoardLayout BuildBoard(
        ViewContext context,
        IReadOnlySet<string> matching,
        IReadOnlyList<FeatureDependency> shown,
        IReadOnlyList<FeatureDependency> visible,
        IReadOnlyList<FeatureDependency> typed,
        IReadOnlySet<string> cards,
        IReadOnlySet<string> addedByFocus,
        FocusResult? focus)
    {
        var index = context.Index;
        var artSprints = index.Data.ArtSprints;
        var nodes = cards.Select(id => index.Nodes[id]).ToList();
        var columns = SprintColumnMapper.Columns(
            artSprints,
            nodes.Any(n => n.Plan?.Position == PlanPosition.EarlierPi),
            nodes.Any(n => n.Plan?.Position == PlanPosition.LaterPi));
        var placements = Place(context, nodes, columns);
        var lanes = BuildLanes(context, matching, shown, visible, typed, placements, addedByFocus, focus);
        var lines = BuildLines(context, visible, placements, focus);

        return new BoardLayout(columns, lanes, lines, LinesVersion(lines));
    }

    private static Dictionary<string, Placement> Place(
        ViewContext context,
        IReadOnlyList<DependencyNode> nodes,
        IReadOnlyList<BoardColumn> columns)
    {
        var artSprints = context.Index.Data.ArtSprints;
        var later = SprintColumnMapper.LaterOrdinal(artSprints);
        var gridIndex = new Dictionary<int, int>();

        for (var i = 0; i < columns.Count; i++)
        {
            gridIndex[columns[i].Ordinal] = i + 1;
        }

        var notPlanned = columns.Count;
        var placements = new Dictionary<string, Placement>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            var plan = node.Plan;
            var (start, end) = plan?.Position switch
            {
                PlanPosition.InPi when plan.First is int first
                    && plan.Last is int last
                    && gridIndex.TryGetValue(first, out var from)
                    && gridIndex.TryGetValue(last, out var to) => (from, to + 1),
                PlanPosition.EarlierPi when gridIndex.TryGetValue(0, out var earlier) => (earlier, earlier + 1),
                PlanPosition.LaterPi when gridIndex.TryGetValue(later, out var laterColumn) => (laterColumn, laterColumn + 1),
                _ => (notPlanned, notPlanned + 1)
            };
            var laneId = HomeLane(context, node);
            var homeTeam = node.Feature!.Teams.FirstOrDefault(t => context.Lookup.LaneOfTeam(t.TeamId) == laneId);
            var sortKey = homeTeam?.SortOrder ?? node.Feature.Ranking ?? int.MaxValue;
            placements[node.NodeId] = new Placement(node, laneId, start, end, sortKey);
        }

        return placements;
    }

    private static string HomeLane(ViewContext context, DependencyNode node)
    {
        foreach (var preferred in new[] { context.Selection.ConsumerLaneId, context.Selection.ProviderLaneId, context.MyTeam })
        {
            if (preferred is not null && node.LaneIds.Contains(preferred))
            {
                return preferred;
            }
        }

        var teams = node.Feature!.Teams;

        if (teams.Count == 0)
        {
            return DependencyClassifier.NoTeamLaneId;
        }

        var home = teams.FirstOrDefault(t => t.IsPrimary)
            ?? teams.FirstOrDefault(t => context.Lookup.Team(t.TeamId).InArt)
            ?? teams[0];

        return context.Lookup.LaneOfTeam(home.TeamId);
    }

    private static List<BoardLaneView> BuildLanes(
        ViewContext context,
        IReadOnlySet<string> matching,
        IReadOnlyList<FeatureDependency> shown,
        IReadOnlyList<FeatureDependency> visible,
        IReadOnlyList<FeatureDependency> typed,
        IReadOnlyDictionary<string, Placement> placements,
        IReadOnlySet<string> addedByFocus,
        FocusResult? focus)
    {
        var index = context.Index;
        var counters = new Dictionary<string, CardCounter>(StringComparer.Ordinal);

        foreach (var dependency in visible)
        {
            if (!dependency.Provider.IsCard || !dependency.Consumer.IsCard)
            {
                continue;
            }

            var provider = CounterOf(dependency.Provider.NodeId);
            var consumer = CounterOf(dependency.Consumer.NodeId);

            if (dependency.Directed)
            {
                consumer.Needs++;
                provider.NeededBy++;
            }
            else
            {
                consumer.Related++;
                provider.Related++;
            }

            consumer.Add(dependency.Verdict.Risk);
            provider.Add(dependency.Verdict.Risk);
        }

        var otherLinks = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var dependency in typed)
        {
            if (!dependency.Provider.IsCard && dependency.Consumer.IsCard)
            {
                otherLinks[dependency.Consumer.NodeId] = otherLinks.GetValueOrDefault(dependency.Consumer.NodeId) + 1;
            }

            if (!dependency.Consumer.IsCard && dependency.Provider.IsCard)
            {
                otherLinks[dependency.Provider.NodeId] = otherLinks.GetValueOrDefault(dependency.Provider.NodeId) + 1;
            }
        }

        var needsAtRisk = new Dictionary<string, int>(StringComparer.Ordinal);
        var atRiskForOthers = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var dependency in shown.Where(d => d.Verdict.Risk >= DependencyRisk.AtRisk))
        {
            var consumerLane = HomeOf(dependency.Consumer);
            var providerLane = HomeOf(dependency.Provider);
            needsAtRisk[consumerLane] = needsAtRisk.GetValueOrDefault(consumerLane) + 1;

            if (providerLane != consumerLane)
            {
                atRiskForOthers[providerLane] = atRiskForOthers.GetValueOrDefault(providerLane) + 1;
            }
        }

        var byLane = placements.Values
            .GroupBy(p => p.LaneId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(p => p.ColumnStart)
                    .ThenBy(p => p.ColumnEnd)
                    .ThenBy(p => p.SortKey)
                    .ThenBy(p => p.Node.Key, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Node.Feature!.FeatureId)
                    .ToList(),
                StringComparer.Ordinal);

        var lanes = index.Lanes
            .Where(l => byLane.ContainsKey(l.LaneId))
            .OrderBy(l => l.LaneId == context.MyTeam ? 0 : 1)
            .ToList();

        var result = new List<BoardLaneView>(lanes.Count);

        foreach (var lane in lanes)
        {
            var placed = byLane[lane.LaneId];
            var rows = BoardRowPacker.Pack(placed.Select(p => (p.ColumnStart, p.ColumnEnd)).ToList());
            var laneCards = placed
                .Select((p, i) => ToCard(context, p, rows[i] + 1, matching, addedByFocus, focus, counters, otherLinks))
                .ToList();

            result.Add(new BoardLaneView(
                lane,
                rows.Count == 0 ? 0 : rows.Max() + 1,
                needsAtRisk.GetValueOrDefault(lane.LaneId),
                atRiskForOthers.GetValueOrDefault(lane.LaneId),
                laneCards));
        }

        return result;

        CardCounter CounterOf(string nodeId)
        {
            if (!counters.TryGetValue(nodeId, out var counter))
            {
                counter = new CardCounter();
                counters[nodeId] = counter;
            }

            return counter;
        }

        string HomeOf(DependencyNode node) =>
            placements.TryGetValue(node.NodeId, out var placement)
                ? placement.LaneId
                : node.IsCard ? HomeLane(context, node) : DependencyClassifier.NotInToolLaneId;
    }

    private static BoardCard ToCard(
        ViewContext context,
        Placement placement,
        int row,
        IReadOnlySet<string> matching,
        IReadOnlySet<string> addedByFocus,
        FocusResult? focus,
        IReadOnlyDictionary<string, CardCounter> counters,
        IReadOnlyDictionary<string, int> otherLinks)
    {
        var node = placement.Node;
        var feature = node.Feature!;
        var artSprints = context.Index.Data.ArtSprints;
        var counter = counters.GetValueOrDefault(node.NodeId) ?? new CardCounter();
        var state = focus is not null
            ? node.NodeId == focus.NodeId ? CardState.Focus
            : focus.Chain.Contains(node.NodeId) ? CardState.Chain
            : CardState.Dimmed
            : feature.Kind == DependencyEndKind.InScope && !matching.Contains(node.NodeId) ? CardState.Context
            : CardState.Normal;

        return new BoardCard(
            node.NodeId,
            feature.FeatureId,
            string.IsNullOrEmpty(node.Key) ? null : node.Key,
            feature.Title,
            feature.Status,
            feature.RagStatus,
            feature.Kind,
            ScopeText(feature),
            placement.LaneId,
            placement.ColumnStart,
            placement.ColumnEnd,
            row,
            SprintText(node, artSprints),
            SprintTooltip(node, context.Lookup),
            node.Plan?.Approximate ?? false,
            feature.Teams.Any(t => t.BelowLine),
            Math.Max(0, feature.Teams.Count - 1),
            TeamsText(node, context.Lookup, artSprints),
            counter.Needs,
            counter.NeededBy,
            counter.Related,
            otherLinks.GetValueOrDefault(node.NodeId),
            counter.Worst,
            DependencyRiskRule.IsRejected(feature.Status),
            feature.FreeTextDependencies,
            feature.ExternalDependencies,
            state,
            addedByFocus.Contains(node.NodeId));
    }

    private static List<BoardLine> BuildLines(
        ViewContext context,
        IReadOnlyList<FeatureDependency> visible,
        IReadOnlyDictionary<string, Placement> placements,
        FocusResult? focus)
    {
        var groups = new SortedDictionary<string, List<FeatureDependency>>(StringComparer.Ordinal);

        foreach (var dependency in visible)
        {
            if (!placements.ContainsKey(dependency.Provider.NodeId) || !placements.ContainsKey(dependency.Consumer.NodeId))
            {
                continue;
            }

            var key = LineKey(dependency.Provider.NodeId, dependency.Consumer.NodeId);

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups[key] = group;
            }

            group.Add(dependency);
        }

        var lines = new List<BoardLine>(groups.Count);

        foreach (var (key, group) in groups)
        {
            var ordered = group.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            var directed = ordered.Where(d => d.Directed).ToList();
            var bothWays = directed.Select(d => d.Provider.NodeId).Distinct(StringComparer.Ordinal).Count() > 1;
            var (min, max) = MinMax(ordered[0].Provider.NodeId, ordered[0].Consumer.NodeId);
            var (from, to) = bothWays
                ? (min, max)
                : directed.Count > 0
                    ? (directed[0].Provider.NodeId, directed[0].Consumer.NodeId)
                    : placements[max].ColumnStart < placements[min].ColumnStart ? (max, min) : (min, max);

            lines.Add(new BoardLine(
                key,
                from,
                to,
                directed.Count > 0,
                bothWays,
                ordered.Max(d => d.Verdict.Risk),
                ordered.Sum(d => d.Links.Count),
                focus is not null && ordered.Any(d => focus.ChainDependencyIds.Contains(d.Id)),
                string.Join("\n", ordered.Select(d => LineTitle(context, d))),
                ordered.Select(d => d.Id).ToList()));
        }

        return lines;
    }

    private static string LineTitle(ViewContext context, FeatureDependency dependency)
    {
        var provider = dependency.Provider;
        var consumer = dependency.Consumer;
        var phrases = Phrases(dependency.Links.Select(l => l.ProviderPhrase));

        if (!dependency.Directed)
        {
            return $"{provider.Key} {phrases} {consumer.Key} · no order, not checked";
        }

        return $"{provider.Key} {phrases} {consumer.Key}"
            + $" · provider ends {EndText(context, provider, providerSide: true)}"
            + $" · consumer starts {EndText(context, consumer, providerSide: false)}"
            + $" · {DependencyRiskRule.Label(dependency.Verdict.Risk)}: {dependency.Verdict.Text}";
    }

    private static string EndText(ViewContext context, DependencyNode node, bool providerSide)
    {
        var plan = node.Plan;
        var artSprints = context.Index.Data.ArtSprints;

        if (plan is null || node.Feature is null)
        {
            return TeamNotPlannedText;
        }

        if (plan.Position is PlanPosition.EarlierPi or PlanPosition.LaterPi or PlanPosition.OtherPiNoDates)
        {
            return PiText(node, artSprints);
        }

        var ordinal = providerSide ? plan.Last : plan.First;

        if (plan.Position != PlanPosition.InPi || ordinal is not int column)
        {
            return TeamNotPlannedText;
        }

        var text = SprintColumnMapper.ShortName(column, artSprints);
        var team = plan.TeamPlans.FirstOrDefault(t => (providerSide ? t.Last : t.First) == column);
        return team is null ? text : $"{text} ({context.Lookup.Team(team.TeamId).Name})";
    }

    private static string LinesVersion(IReadOnlyList<BoardLine> lines)
    {
        var text = string.Join(
            ";",
            lines.Select(l => $"{l.Key},{l.FromNodeId},{l.ToNodeId},{(int)l.Risk},{l.LinkCount},{l.Directed},{l.BothWays}"));

        unchecked
        {
            var hash = 14695981039346656037UL;

            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    private static DetailsPanel BuildPanel(
        ViewContext context,
        IReadOnlyList<FeatureDependency> linked,
        FocusResult focus,
        string? selectedDependencyId)
    {
        var index = context.Index;
        var node = index.Nodes[focus.NodeId];
        var feature = node.Feature!;
        var linkedById = linked.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var needs = new List<DetailsRow>();
        var neededBy = new List<DetailsRow>();
        var related = new List<DetailsRow>();
        var otherLinks = new List<DetailsRow>();

        foreach (var original in index.Dependencies)
        {
            var dependency = linkedById.GetValueOrDefault(original.Id) ?? original;
            var focusIsConsumer = dependency.Consumer.NodeId == focus.NodeId;

            if (!focusIsConsumer && dependency.Provider.NodeId != focus.NodeId)
            {
                continue;
            }

            var other = focusIsConsumer ? dependency.Provider : dependency.Consumer;
            var row = new DetailsRow(
                dependency.Id,
                other.NodeId,
                other.Key,
                other.Feature?.Title ?? other.Stub?.Summary,
                other.Feature?.Status ?? other.Stub?.Status,
                other.Feature?.RagStatus,
                TeamNames(other, context.Lookup),
                other.Stub is { } stub ? KindLabel(stub.Kind) : SprintText(other, index.Data.ArtSprints),
                Phrases(dependency.Links.Select(l => focusIsConsumer ? l.ConsumerPhrase : l.ProviderPhrase)),
                dependency.Verdict.Risk,
                dependency.Verdict.Text,
                other.IsCard,
                !linkedById.ContainsKey(dependency.Id),
                selectedDependencyId is not null && dependency.Id == selectedDependencyId);

            var target = !other.IsCard ? otherLinks
                : !dependency.Directed ? related
                : focusIsConsumer ? needs
                : neededBy;
            target.Add(row);
        }

        return new DetailsPanel(
            node.NodeId,
            feature,
            ScopeText(feature),
            feature.Teams
                .Select(t => new DetailsTeamPlan(
                    context.Lookup.Team(t.TeamId).Name,
                    t.IsPrimary,
                    TeamSprintText(t),
                    t.BelowLine,
                    node.Plan?.TeamPlans.FirstOrDefault(p => p.TeamId == t.TeamId)?.Approximate ?? false))
                .ToList(),
            SortDetails(needs),
            SortDetails(neededBy),
            SortDetails(related),
            SortDetails(otherLinks),
            focus.Upstream,
            focus.Downstream,
            focus.HiddenByFilters);
    }

    private static List<DetailsRow> SortDetails(IEnumerable<DetailsRow> rows) =>
        rows.OrderByDescending(r => r.Risk)
            .ThenBy(r => r.OtherKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.DependencyId, StringComparer.Ordinal)
            .ToList();

    private static string SprintText(DependencyNode node, IReadOnlyList<DependencyArtSprint> artSprints)
    {
        var plan = node.Plan;

        return plan?.Position switch
        {
            PlanPosition.InPi when plan.First is int first && plan.Last is int last => Range(first, last, artSprints),
            PlanPosition.EarlierPi or PlanPosition.LaterPi or PlanPosition.OtherPiNoDates => PiText(node, artSprints),
            _ => NotPlannedText
        };
    }

    private static string Range(int first, int last, IReadOnlyList<DependencyArtSprint> artSprints) =>
        first == last
            ? SprintColumnMapper.ShortName(first, artSprints)
            : $"{SprintColumnMapper.ShortName(first, artSprints)}{RangeDash}{SprintColumnMapper.ShortName(last, artSprints)}";

    private static string PiText(DependencyNode node, IReadOnlyList<DependencyArtSprint> artSprints)
    {
        if (node.Feature?.PiName is { } piName && !string.IsNullOrWhiteSpace(piName))
        {
            return PiLabel(piName);
        }

        var ordinal = node.Plan?.Position == PlanPosition.EarlierPi ? 0 : SprintColumnMapper.LaterOrdinal(artSprints);
        return SprintColumnMapper.ShortName(ordinal, artSprints);
    }

    private static string SprintTooltip(DependencyNode node, Lookup lookup)
    {
        var feature = node.Feature!;

        if (node.Plan?.Position is PlanPosition.EarlierPi or PlanPosition.LaterPi
            || (feature.Kind != DependencyEndKind.InScope && !feature.InSelectedPi))
        {
            return string.IsNullOrWhiteSpace(feature.PiName) ? "No PI" : $"In {PiLabel(feature.PiName)}";
        }

        var lines = feature.Teams
            .Select(t => $"{TeamLabel(t, lookup)}: {TeamSprintText(t)}{(t.BelowLine ? BelowLineSuffix : string.Empty)}")
            .ToList();

        if (lines.Count == 0)
        {
            lines.Add(NoTeamText);
        }

        if (node.Plan?.Approximate == true)
        {
            lines.Add(ApproximateNote);
        }

        return string.Join("\n", lines);
    }

    private static string TeamsText(DependencyNode node, Lookup lookup, IReadOnlyList<DependencyArtSprint> artSprints)
    {
        var feature = node.Feature!;
        var plans = node.Plan?.TeamPlans ?? [];

        return string.Join(
            "\n",
            feature.Teams.Select(t =>
            {
                var plan = plans.FirstOrDefault(p => p.TeamId == t.TeamId);
                var text = plan is { First: int first, Last: int last } ? Range(first, last, artSprints) : TeamNotPlannedText;
                return $"{TeamLabel(t, lookup)}: {text}{(t.BelowLine ? BelowLineSuffix : string.Empty)}";
            }));
    }

    private static string TeamLabel(DependencyFeatureTeam team, Lookup lookup) =>
        team.IsPrimary ? $"{lookup.Team(team.TeamId).Name} ★" : lookup.Team(team.TeamId).Name;

    private static string TeamSprintText(DependencyFeatureTeam team)
    {
        if (team.Sprints.Count == 0)
        {
            return TeamNotPlannedText;
        }

        var sprints = team.Sprints
            .OrderBy(s => s.Start)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = string.Join(", ", sprints.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase));
        var start = sprints.Min(s => s.Start);
        var end = sprints.Max(s => s.End);
        return $"{names} ({Day(start)}{RangeDash}{Day(end)})";
    }

    private static string Day(DateTime date) => date.ToString("dd MMM", CultureInfo.InvariantCulture);

    private static string TeamNames(DependencyNode node, Lookup lookup)
    {
        if (node.Feature is not { } feature)
        {
            return Dash;
        }

        return feature.Teams.Count == 0
            ? NoTeamText
            : string.Join(", ", feature.Teams.Select(t => lookup.Team(t.TeamId).Name));
    }

    private static string ProviderScope(DependencyNode node) =>
        node.Stub is { } stub
            ? KindLabel(stub.Kind)
            : ScopeText(node.Feature!) ?? ThisArtAndPi;

    private static string? ScopeText(DependencyFeature feature) => feature.Kind switch
    {
        DependencyEndKind.OtherPi => string.IsNullOrWhiteSpace(feature.PiName) ? "No PI" : PiLabel(feature.PiName),
        DependencyEndKind.OtherArt => (feature.ArtName is null ? "No ART" : $"ART {feature.ArtName}")
            + (!feature.InSelectedPi && !string.IsNullOrWhiteSpace(feature.PiName) ? $" · {PiLabel(feature.PiName)}" : string.Empty),
        _ => null
    };

    public static string PiLabel(string piName)
    {
        var name = piName.Trim();
        return name.StartsWith("PI", StringComparison.OrdinalIgnoreCase)
            && (name.Length == 2 || !char.IsLetter(name[2]))
            ? name
            : $"PI {name}";
    }

    private static string Phrases(IEnumerable<string> phrases) =>
        string.Join(" / ", phrases.Distinct(StringComparer.OrdinalIgnoreCase));

    private static string LineKey(string a, string b)
    {
        var (min, max) = MinMax(a, b);
        return $"{min}|{max}";
    }

    private static (string Min, string Max) MinMax(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    private static string? Blank(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed record ViewContext(DependencyIndex Index, Lookup Lookup, DependencySelection Selection, string? MyTeam);

    private sealed record Placement(DependencyNode Node, string LaneId, int ColumnStart, int ColumnEnd, int SortKey);

    private sealed record FocusResult(
        string NodeId,
        HashSet<string> Chain,
        HashSet<string> ChainDependencyIds,
        int Upstream,
        int Downstream,
        bool HiddenByFilters);

    private sealed class Lookup
    {
        private readonly Dictionary<string, DependencyLane> _lanes;
        private readonly Dictionary<int, DependencyTeam> _teams = [];

        public Lookup(DependencyIndex index)
        {
            _lanes = index.Lanes
                .GroupBy(l => l.LaneId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var team in index.Data.Teams)
            {
                _teams.TryAdd(team.TeamId, team);
            }
        }

        public DependencyTeam Team(int teamId) =>
            _teams.TryGetValue(teamId, out var team)
                ? team
                : new DependencyTeam(teamId, $"Team {teamId}", false, false, []);

        public string LaneName(string laneId) => _lanes.TryGetValue(laneId, out var lane) ? lane.Name : laneId;

        public string LaneOfTeam(int teamId)
        {
            var laneId = DependencyClassifier.TeamLaneId(teamId);
            return _lanes.ContainsKey(laneId) ? laneId : DependencyClassifier.OtherTeamsLaneId;
        }
    }

    private sealed class CardCounter
    {
        public int Needs { get; set; }

        public int NeededBy { get; set; }

        public int Related { get; set; }

        public DependencyRisk Worst { get; private set; } = DependencyRisk.InOrder;

        public void Add(DependencyRisk risk)
        {
            if (risk > Worst)
            {
                Worst = risk;
            }
        }
    }

    private sealed class CellTally
    {
        public int Count { get; private set; }

        public int OutOfOrder { get; private set; }

        public int AtRisk { get; private set; }

        public int InOrder { get; private set; }

        public int NotChecked { get; private set; }

        public int Related { get; set; }

        public void Add(DependencyRisk risk)
        {
            Count++;

            switch (risk)
            {
                case DependencyRisk.OutOfOrder:
                    OutOfOrder++;
                    break;
                case DependencyRisk.AtRisk:
                    AtRisk++;
                    break;
                case DependencyRisk.InOrder:
                    InOrder++;
                    break;
                default:
                    NotChecked++;
                    break;
            }
        }

        public MatrixCell ToCell(DependencyLane row, DependencyLane column)
        {
            var worst = Count == 0 ? DependencyRisk.NotChecked
                : OutOfOrder > 0 ? DependencyRisk.OutOfOrder
                : AtRisk > 0 ? DependencyRisk.AtRisk
                : NotChecked > 0 ? DependencyRisk.NotChecked
                : DependencyRisk.InOrder;
            var related = $"{Plural(Related, "related link", "related links")} (no order)";
            var diagonal = row.LaneId == column.LaneId;
            var inside = row.Kind switch
            {
                DependencyLaneKind.ArtTeam or DependencyLaneKind.OtherTeam => "inside the team",
                DependencyLaneKind.OtherTeams => "between teams in this group",
                _ => "between features in this group"
            };
            string tooltip;

            if (Count == 0)
            {
                tooltip = diagonal ? $"{row.Name}: {related} {inside}" : $"{row.Name} and {column.Name}: {related}";
            }
            else
            {
                tooltip = (diagonal
                        ? $"{row.Name}: {Plural(Count, "dependency", "dependencies")} {inside}: "
                        : $"{row.Name} needs {column.Name}: {Plural(Count, "dependency", "dependencies")}: ")
                    + $"{OutOfOrder} out of order, {AtRisk} at risk, {InOrder} in order, {NotChecked} not checked";

                if (Related > 0)
                {
                    tooltip += $" · {related}";
                }
            }

            return new MatrixCell(Count, OutOfOrder, AtRisk, InOrder, NotChecked, Related, worst, tooltip);
        }

        private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";
    }
}
