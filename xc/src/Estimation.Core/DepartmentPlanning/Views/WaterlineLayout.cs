using System.Globalization;
using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.Train.Models;

namespace Estimation.Core.DepartmentPlanning.Views;

public enum WaterlineScale
{
    StoryPoints = 0,
    PercentOfCapacity = 1,
}

public sealed record WaterlineBand(double Top, double Bottom)
{
    public double Height => Bottom - Top;
}

public sealed record WaterlineBlock(WaterlineBand StoryPoints, WaterlineBand Percent)
{
    public WaterlineBand For(WaterlineScale scale) => scale == WaterlineScale.PercentOfCapacity ? Percent : StoryPoints;
}

public sealed record WaterlineLevel(double StoryPoints, double Percent)
{
    public double For(WaterlineScale scale) => scale == WaterlineScale.PercentOfCapacity ? Percent : StoryPoints;
}

public sealed record WaterlineTick(decimal Value, double Y, string Label);

public sealed record WaterlineAxis(string Title, IReadOnlyList<WaterlineTick> Ticks);

public sealed record WaterlineObjective(int Index, int? Id, string? JiraId, string Title, int Items);

public sealed record WaterlineSegment(
    PlanningRow Row,
    int Position,
    decimal From,
    decimal To,
    WaterlineBlock Block,
    WaterlineBlock? Over,
    WaterlineBlock? Ghost,
    double Shade,
    int Objective,
    bool IsBelowLine)
{
    public decimal Sp => To - From;

    public PlanningRank? Rank => Row.Ranks.Count > 0 ? Row.Ranks[0] : null;

    public bool IsSaved => Row.Ranks.Count == 0 || Row.Ranks[0].IsSaved;

    public string? Rag => Row.RagStatus ?? Row.Metrics.WorstRag;

    public string? TwinKey => Row.EntityId is { } id ? $"{(int)Row.Level}:{id.ToString(CultureInfo.InvariantCulture)}" : null;
}

public sealed record WaterlineTeamColumn(
    PlanningTeam Team,
    double X,
    double Width,
    decimal CapacitySp,
    decimal PlannedSp,
    WaterlineLevel? CapacityY,
    WaterlineLevel LineY,
    IReadOnlyList<WaterlineSegment> Segments,
    IReadOnlyList<WaterlineSegment> BelowLine)
{
    public bool HasCapacity => CapacitySp > 0m;

    public decimal OverSp => PlannedSp - CapacitySp;
}

public sealed record WaterlineColumn(
    PlanningRow Row,
    PlanningArt? Art,
    PlanningLevel ItemLevel,
    string Subtitle,
    bool IsRanked,
    bool HasLine,
    double X,
    double Width,
    double BarX,
    double BarWidth,
    double GhostX,
    double TextX,
    decimal CapacitySp,
    decimal PlannedSp,
    WaterlineLevel? CapacityY,
    WaterlineLevel LineY,
    WaterlineLevel TopY,
    IReadOnlyList<WaterlineSegment> Segments,
    IReadOnlyList<WaterlineSegment> BelowLine,
    IReadOnlyList<WaterlineTeamColumn> Teams,
    int ZeroPlannedItems)
{
    public string Name => Row.Title;

    public bool HasCapacity => CapacitySp > 0m;

    public decimal OverSp => PlannedSp - CapacitySp;

    public int Items => Segments.Count + BelowLine.Count;

    public bool IsEmpty => Items == 0 && !HasCapacity;
}

public sealed record WaterlineChart(
    IReadOnlyList<WaterlineColumn> Columns,
    IReadOnlyList<WaterlineObjective> Objectives,
    WaterlineAxis StoryPointAxis,
    WaterlineAxis PercentAxis,
    double Width,
    double Height)
{
    public static readonly WaterlineChart Empty = new([], [], new WaterlineAxis(WaterlineLayout.StoryPointAxisTitle, []), new WaterlineAxis(WaterlineLayout.PercentAxisTitle, []), 0d, WaterlineLayout.Height);

    public bool IsEmpty => Columns.Count == 0;

    public WaterlineAxis Axis(WaterlineScale scale) => scale == WaterlineScale.PercentOfCapacity ? PercentAxis : StoryPointAxis;
}

public static class WaterlineLayout
{
    public const string StoryPointAxisTitle = "SP";
    public const string PercentAxisTitle = "% of capacity";
    public const string NotRanked = "not ranked";
    public const string NoObjectiveTitle = DepartmentPlanningTree.NoObjectiveTitle;
    public const double Height = 620d;
    public const double PlotTop = 96d;
    public const double PlotBottom = 568d;
    public const double AxisWidth = 56d;
    public const double ColumnPad = 14d;
    public const double MinBarWidth = 120d;
    public const double GhostWidth = 10d;
    public const double TextWidth = 74d;
    public const double TeamMinWidth = 26d;
    public const double TeamGap = 4d;
    public const double RightPad = 16d;
    private const double InnerGap = 6d;
    private const double Headroom = 1.08d;
    private const int TargetTicks = 6;

    public static WaterlineChart Build(PlanningTree tree, DepartmentPlanningData? data, bool teamScale = false)
    {
        var roots = tree.Roots.Where(r => r.Kind == PlanningRowKind.Node && r.Level == PlanningLevel.Art).ToList();
        if (roots.Count == 0)
        {
            return WaterlineChart.Empty;
        }

        var arts = (data?.Arts ?? tree.Arts).GroupBy(a => a.Id).ToDictionary(g => g.Key, g => g.First());
        var facts = roots
            .Select(root => new ColumnFacts(root, arts.GetValueOrDefault(root.ArtId ?? root.EntityId ?? ArtBoardItems.None), tree.Teams))
            .ToList();

        var objectives = new ObjectiveIndex(data);
        foreach (var fact in facts)
        {
            objectives.Count(fact.Included);
            objectives.Count(fact.Below);
        }

        objectives.Freeze();

        var spMax = Math.Max(1m, facts.Max(f => f.Top)) * (decimal)Headroom;
        var percentMax = Math.Max(1d, facts.Max(f => teamScale ? f.PercentTop : f.ColumnPercentTop)) * Headroom;
        var mapper = new Mapper(spMax, percentMax);

        var columns = new List<WaterlineColumn>(facts.Count);
        var x = AxisWidth;
        foreach (var fact in facts)
        {
            var column = BuildColumn(fact, x, mapper, objectives);
            columns.Add(column);
            x += column.Width;
        }

        return new WaterlineChart(columns, objectives.Legend, StoryPointAxis(mapper, spMax), PercentAxis(mapper, percentMax), x + RightPad, Height);
    }

    public static string SubtitleOf(PlanningArt? art) =>
        art?.Level is not { } level || art.OrderMode == ArtOrderMode.Empty
            ? NotRanked
            : $"ranked by {ArtPrioritizations.Label(level).ToLowerInvariant()}, {(art.OrderMode == ArtOrderMode.PiOrder ? "own PI order" : "general order")}";

    internal static decimal NiceStep(decimal max)
    {
        if (max <= 0m)
        {
            return 1m;
        }

        var raw = (double)max / TargetTicks;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalised = raw / magnitude;
        var step = normalised <= 1d ? 1d : normalised <= 2d ? 2d : normalised <= 2.5d ? 2.5d : normalised <= 5d ? 5d : 10d;
        return (decimal)(step * magnitude);
    }

    private static WaterlineColumn BuildColumn(ColumnFacts fact, double x, Mapper mapper, ObjectiveIndex objectives)
    {
        var teamCount = fact.Teams.Count;
        var barWidth = Math.Max(MinBarWidth, teamCount * TeamMinWidth + Math.Max(0, teamCount - 1) * TeamGap);
        var width = ColumnPad + barWidth + InnerGap + GhostWidth + InnerGap + TextWidth + ColumnPad;
        var barX = x + ColumnPad;
        var ghostX = barX + barWidth + InnerGap;
        var textX = ghostX + GhostWidth + InnerGap;

        var included = fact.Included;
        var segments = new List<WaterlineSegment>(included.Count);
        for (var i = 0; i < included.Count; i++)
        {
            var row = included[i];
            var to = row.CumulativePlannedSp ?? 0m;
            var from = to - row.Metrics.PlannedSp;
            segments.Add(new WaterlineSegment(
                row,
                i + 1,
                from,
                to,
                mapper.Block(from, to, fact.PercentBase),
                OverBlock(row.OverCapacity, from, to, fact.Capacity, fact.PercentBase, mapper),
                row.Metrics.UnplannedSp > 0m ? mapper.Block(to, to + row.Metrics.UnplannedSp, fact.PercentBase) : null,
                Shade(i, included.Count),
                objectives.IndexOf(row),
                false));
        }

        var below = BelowBlocks(fact.Below, fact.PlannedTop, r => r.Metrics.DemandSp, fact.PercentBase, mapper, objectives, included.Count, true);

        var teams = new List<WaterlineTeamColumn>(teamCount);
        var teamWidth = teamCount == 0 ? 0d : (barWidth - (teamCount - 1) * TeamGap) / teamCount;
        for (var j = 0; j < teamCount; j++)
        {
            teams.Add(BuildTeam(fact.Teams[j], included, fact.Below, barX + j * (teamWidth + TeamGap), teamWidth, mapper, objectives));
        }

        return new WaterlineColumn(
            fact.Root,
            fact.Art,
            fact.ItemLevel,
            SubtitleOf(fact.Art),
            fact.Art?.Level is not null && fact.Art.OrderMode != ArtOrderMode.Empty,
            fact.HasLine,
            x,
            width,
            barX,
            barWidth,
            ghostX,
            textX,
            fact.Capacity,
            fact.PlannedTop,
            fact.Capacity > 0m ? mapper.Level(fact.Capacity, fact.PercentBase) : null,
            mapper.Level(fact.PlannedTop, fact.PercentBase),
            mapper.Level(fact.Top, fact.PercentBase),
            segments,
            below,
            teams,
            included.Count(r => r.Metrics.PlannedSp == 0m));
    }

    private static WaterlineTeamColumn BuildTeam(
        TeamFacts fact,
        List<PlanningRow> included,
        List<PlanningRow> below,
        double x,
        double width,
        Mapper mapper,
        ObjectiveIndex objectives)
    {
        var teamId = fact.Team.Id;
        var segments = new List<WaterlineSegment>();
        for (var i = 0; i < included.Count; i++)
        {
            var row = included[i];
            var cell = row.TeamCells.GetValueOrDefault(teamId);
            if (cell is null || cell.CumulativeSp is not { } to || cell.PlannedSp <= 0m)
            {
                continue;
            }

            var from = to - cell.PlannedSp;
            segments.Add(new WaterlineSegment(
                row,
                i + 1,
                from,
                to,
                mapper.Block(from, to, fact.PercentBase),
                OverBlock(cell.OverCapacity, from, to, fact.Capacity, fact.PercentBase, mapper),
                null,
                Shade(i, included.Count),
                objectives.IndexOf(row),
                false));
        }

        var belowBlocks = BelowBlocks(below, fact.PlannedTop, r => r.TeamCells.GetValueOrDefault(teamId)?.DemandSp ?? 0m, fact.PercentBase, mapper, objectives, included.Count, false);

        return new WaterlineTeamColumn(
            fact.Team,
            x,
            width,
            fact.Capacity,
            fact.PlannedTop,
            fact.Capacity > 0m ? mapper.Level(fact.Capacity, fact.PercentBase) : null,
            mapper.Level(fact.PlannedTop, fact.PercentBase),
            segments,
            belowBlocks);
    }

    private static List<WaterlineSegment> BelowBlocks(
        List<PlanningRow> rows,
        decimal start,
        Func<PlanningRow, decimal> size,
        decimal percentBase,
        Mapper mapper,
        ObjectiveIndex objectives,
        int positionOffset,
        bool keepEmpty)
    {
        var blocks = new List<WaterlineSegment>(rows.Count);
        var from = start;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var sp = Math.Max(0m, size(row));
            if (sp == 0m && !keepEmpty)
            {
                continue;
            }

            var to = from + sp;
            blocks.Add(new WaterlineSegment(row, positionOffset + i + 1, from, to, mapper.Block(from, to, percentBase), null, null, 1d, objectives.IndexOf(row), true));
            from = to;
        }

        return blocks;
    }

    private static WaterlineBlock? OverBlock(bool over, decimal from, decimal to, decimal capacity, decimal percentBase, Mapper mapper)
    {
        if (!over)
        {
            return null;
        }

        var redFrom = Math.Max(from, capacity);
        return redFrom < to ? mapper.Block(redFrom, to, percentBase) : null;
    }

    private static double Shade(int index, int count) => count <= 1 ? 0d : (double)index / (count - 1);

    private static WaterlineAxis StoryPointAxis(Mapper mapper, decimal max)
    {
        var step = NiceStep(max);
        var ticks = new List<WaterlineTick>();
        for (var value = 0m; value <= max; value += step)
        {
            ticks.Add(new WaterlineTick(value, mapper.StoryPointY(value), value.ToString("0.#", CultureInfo.InvariantCulture)));
        }

        return new WaterlineAxis(StoryPointAxisTitle, ticks);
    }

    private static WaterlineAxis PercentAxis(Mapper mapper, double max)
    {
        var percentMax = (decimal)(max * 100d);
        var step = NiceStep(percentMax);
        var ticks = new List<WaterlineTick>();
        for (var value = 0m; value <= percentMax; value += step)
        {
            ticks.Add(new WaterlineTick(value, mapper.PercentY((double)value / 100d), value.ToString("0.#", CultureInfo.InvariantCulture) + "%"));
        }

        return new WaterlineAxis(PercentAxisTitle, ticks);
    }

    private sealed class Mapper(decimal spMax, double percentMax)
    {
        private const double Plot = PlotBottom - PlotTop;

        public double StoryPointY(decimal value) => PlotBottom - (double)(value / spMax) * Plot;

        public double PercentY(double ratio) => PlotBottom - ratio / percentMax * Plot;

        public WaterlineLevel Level(decimal value, decimal percentBase) => new(StoryPointY(value), PercentY((double)(value / percentBase)));

        public WaterlineBlock Block(decimal from, decimal to, decimal percentBase) => new(
            new WaterlineBand(StoryPointY(to), StoryPointY(from)),
            new WaterlineBand(PercentY((double)(to / percentBase)), PercentY((double)(from / percentBase))));
    }

    private sealed class ColumnFacts
    {
        public ColumnFacts(PlanningRow root, PlanningArt? art, IReadOnlyList<PlanningTeam> teams)
        {
            Root = root;
            Art = art;
            Included = root.Children.Where(c => c.Kind == PlanningRowKind.Node && !c.IsBelowArtLine).ToList();
            Below = root.Children.Where(c => c.Kind == PlanningRowKind.Node && c.IsBelowArtLine).ToList();
            HasLine = root.Children.Any(c => c.Kind == PlanningRowKind.LineMarker);
            Capacity = root.CapacitySp ?? 0m;
            PlannedTop = Included.Select(r => r.CumulativePlannedSp ?? 0m).DefaultIfEmpty(0m).Max();
            var belowSum = Below.Sum(r => r.Metrics.DemandSp);
            var ghostTop = Included.Select(r => (r.CumulativePlannedSp ?? 0m) + r.Metrics.UnplannedSp).DefaultIfEmpty(0m).Max();
            Top = Math.Max(Capacity, Math.Max(PlannedTop + belowSum, ghostTop));
            PercentBase = Capacity > 0m ? Capacity : Math.Max(Top, 1m);

            var teamIds = art?.TeamIds.ToHashSet() ?? Included.Concat(Below).SelectMany(r => r.TeamCells.Keys).ToHashSet();
            Teams = teams.Where(t => teamIds.Contains(t.Id)).Select(t => new TeamFacts(t, Included, Below)).ToList();
            ColumnPercentTop = (double)(Top / PercentBase);
            PercentTop = Math.Max(ColumnPercentTop, Teams.Select(t => t.PercentTop).DefaultIfEmpty(0d).Max());
            ItemLevel = Included.Concat(Below).FirstOrDefault()?.Level
                ?? (art?.Level == ArtPrioritization.BusinessOutcome ? PlanningLevel.BusinessOutcome : PlanningLevel.PortfolioEpic);
        }

        public PlanningRow Root { get; }

        public PlanningArt? Art { get; }

        public List<PlanningRow> Included { get; }

        public List<PlanningRow> Below { get; }

        public bool HasLine { get; }

        public decimal Capacity { get; }

        public decimal PlannedTop { get; }

        public decimal Top { get; }

        public decimal PercentBase { get; }

        public double ColumnPercentTop { get; }

        public double PercentTop { get; }

        public List<TeamFacts> Teams { get; }

        public PlanningLevel ItemLevel { get; }
    }

    private sealed class TeamFacts
    {
        public TeamFacts(PlanningTeam team, List<PlanningRow> included, List<PlanningRow> below)
        {
            Team = team;
            Capacity = team.CapacitySp;
            PlannedTop = included.Select(r => r.TeamCells.GetValueOrDefault(team.Id)?.CumulativeSp ?? 0m).DefaultIfEmpty(0m).Max();
            var belowSum = below.Sum(r => r.TeamCells.GetValueOrDefault(team.Id)?.DemandSp ?? 0m);
            var top = Math.Max(Capacity, PlannedTop + belowSum);
            PercentBase = Capacity > 0m ? Capacity : Math.Max(top, 1m);
            PercentTop = (double)(top / PercentBase);
        }

        public PlanningTeam Team { get; }

        public decimal Capacity { get; }

        public decimal PlannedTop { get; }

        public decimal PercentBase { get; }

        public double PercentTop { get; }
    }

    private sealed class ObjectiveIndex
    {
        private readonly Dictionary<int, PlanningEpic> _epics;
        private readonly Dictionary<int, PlanningOutcome> _outcomes;
        private readonly Dictionary<int, PlanningObjective> _objectives;
        private readonly Dictionary<int, int> _items = new();
        private readonly Dictionary<int, int> _indexById = new();

        public ObjectiveIndex(DepartmentPlanningData? data)
        {
            _epics = (data?.Epics ?? []).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
            _outcomes = (data?.Outcomes ?? []).GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
            _objectives = (data?.Objectives ?? []).GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
        }

        public IReadOnlyList<WaterlineObjective> Legend { get; private set; } = [];

        public void Count(IEnumerable<PlanningRow> rows)
        {
            foreach (var row in rows)
            {
                var id = ObjectiveIdOf(row);
                _items[id] = _items.GetValueOrDefault(id) + 1;
            }
        }

        public void Freeze()
        {
            var ordered = _items.Keys
                .Where(id => id != ArtBoardItems.None)
                .OrderBy(id => TitleOf(id), StringComparer.OrdinalIgnoreCase)
                .ThenBy(id => id)
                .ToList();
            if (_items.ContainsKey(ArtBoardItems.None))
            {
                ordered.Add(ArtBoardItems.None);
            }

            var legend = new List<WaterlineObjective>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++)
            {
                var id = ordered[i];
                _indexById[id] = i;
                var objective = _objectives.GetValueOrDefault(id);
                legend.Add(new WaterlineObjective(i, id == ArtBoardItems.None ? null : id, objective?.JiraId, TitleOf(id), _items[id]));
            }

            Legend = legend;
        }

        public int IndexOf(PlanningRow row) => _indexById.GetValueOrDefault(ObjectiveIdOf(row));

        private string TitleOf(int id) => id == ArtBoardItems.None
            ? NoObjectiveTitle
            : _objectives.TryGetValue(id, out var objective) ? objective.Summary : $"#{id}";

        private int ObjectiveIdOf(PlanningRow row)
        {
            var epicId = row.Level switch
            {
                PlanningLevel.PortfolioEpic => row.EntityId,
                PlanningLevel.BusinessOutcome => row.EntityId is { } outcomeId && _outcomes.TryGetValue(outcomeId, out var outcome) ? outcome.PortfolioEpicId : null,
                _ => null,
            };

            return epicId is { } id && _epics.TryGetValue(id, out var epic) && epic.StrategicObjectiveIds.Count > 0
                ? epic.StrategicObjectiveIds[0]
                : ArtBoardItems.None;
        }
    }
}
