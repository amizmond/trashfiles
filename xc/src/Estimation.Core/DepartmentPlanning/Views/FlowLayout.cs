using System.Globalization;
using Estimation.Core.DepartmentPlanning.Services;

namespace Estimation.Core.DepartmentPlanning.Views;

public enum FlowSourceLevel
{
    Objectives = 0,
    PortfolioEpics = 1,
}

public enum FlowNodeKind
{
    Row = 0,
    Other = 1,
    Art = 2,
    Outlet = 3,
}

public sealed record FlowOptions
{
    public FlowSourceLevel Source { get; init; } = FlowSourceLevel.Objectives;

    public bool BundleSmall { get; init; } = true;

    public double BundleShare { get; init; } = 0.02d;

    public double Width { get; init; } = 1000d;

    public double MinHeight { get; init; } = 520d;

    public double RowPitch { get; init; } = 26d;

    public double NodeWidth { get; init; } = 18d;

    public double Gap { get; init; } = 10d;

    public double LabelWidth { get; init; } = 250d;

    public double Padding { get; init; } = 12d;

    public bool ShowCapacity { get; init; } = true;
}

public sealed record FlowNode(
    string Key,
    FlowNodeKind Kind,
    PlanningRow? Row,
    string Title,
    string? JiraId,
    decimal Sp,
    decimal PlannedSp,
    decimal UnplannedSp,
    decimal? CapacitySp,
    int ColorIndex,
    bool IsBucket,
    IReadOnlyList<string> BundledTitles,
    double X,
    double Y,
    double Width,
    double Height,
    double? CapacityHeight)
{
    public string? RowId => Row?.Id;

    public bool IsSelectable => Row is not null;

    public int BundledCount => BundledTitles.Count;

    public decimal NetSp { get; init; } = Sp;

    public double NetHeight { get; init; } = Height;

    public decimal SharedSp => Sp - NetSp;

    public bool HasShared => SharedSp > 0m;

    public bool OverCapacity => CapacitySp is { } capacity && NetSp > capacity;

    public decimal FreeSp => CapacitySp is { } capacity ? capacity - NetSp : 0m;

    public double SlotHeight => Math.Max(Height, CapacityHeight ?? 0d);

    public double CenterY => Y + SlotHeight / 2d;
}

public sealed record FlowLink(
    int Index,
    string SourceKey,
    string TargetKey,
    decimal Sp,
    bool ToOutlet,
    int ColorIndex,
    double ShareOfSource,
    double ShareOfTarget,
    double Thickness,
    string Path);

public sealed record FlowLayout(
    FlowSourceLevel Source,
    PlanningLevel SourceLevel,
    double Width,
    double Height,
    IReadOnlyList<FlowNode> Sources,
    IReadOnlyList<FlowNode> Targets,
    IReadOnlyList<FlowLink> Links,
    decimal PlannedSp,
    decimal UnplannedSp,
    decimal CapacitySp,
    int HiddenRows,
    int BundledRows)
{
    public const int ColorCount = 8;

    public const string OtherKey = "other";

    public const string OutletKey = "outlet";

    public static readonly FlowLayout Empty =
        new(FlowSourceLevel.Objectives, PlanningLevel.StrategicObjective, 0d, 0d, [], [], [], 0m, 0m, 0m, 0, 0);

    public bool IsEmpty => Links.Count == 0;

    public IEnumerable<FlowNode> Nodes => Sources.Concat(Targets);

    public static string RowKey(string rowId) => "row:" + rowId;

    public static string ArtKey(int artId) => "art:" + artId.ToString(CultureInfo.InvariantCulture);

    public static string Fit(string text, int maxChars)
    {
        if (maxChars < 2 || text.Length <= maxChars)
        {
            return text;
        }

        return text[..(maxChars - 1)].TrimEnd() + "…";
    }

    public static FlowLayout Build(PlanningTree tree, FlowOptions options, IReadOnlyList<int>? objectiveOrder = null)
    {
        var level = options.Source == FlowSourceLevel.PortfolioEpics ? PlanningLevel.PortfolioEpic : PlanningLevel.StrategicObjective;
        var colors = RootColors(tree, objectiveOrder);
        var arts = tree.Arts
            .Select(a => new ArtSlot(
                a,
                a.TeamIds.ToHashSet(),
                options.ShowCapacity ? tree.Teams.Where(t => a.TeamIds.Contains(t.Id)).Sum(t => t.CapacitySp) : 0m))
            .ToList();
        var capacity = arts.Sum(a => a.CapacitySp);
        var netPlanned = new decimal[arts.Count];
        foreach (var leaf in tree.FlattenAll().Where(r => r.Feature is not null).DistinctBy(r => r.Feature!.Id))
        {
            for (var i = 0; i < arts.Count; i++)
            {
                foreach (var (teamId, cell) in leaf.TeamCells)
                {
                    if (arts[i].TeamIds.Contains(teamId))
                    {
                        netPlanned[i] += cell.PlannedSp;
                    }
                }
            }
        }

        var entries = new List<Entry>();
        var hidden = 0;
        foreach (var row in SourceRows(tree, options.Source))
        {
            var flows = new decimal[arts.Count];
            for (var i = 0; i < arts.Count; i++)
            {
                foreach (var (teamId, cell) in row.TeamCells)
                {
                    if (arts[i].TeamIds.Contains(teamId))
                    {
                        flows[i] += cell.PlannedSp;
                    }
                }
            }

            var unplanned = Math.Max(0m, row.Metrics.UnplannedSp);
            var total = flows.Sum() + unplanned;
            if (total <= 0m)
            {
                hidden++;
                continue;
            }

            entries.Add(new Entry(
                row,
                row.Title,
                row.JiraId,
                row.IsBucket,
                colors.GetValueOrDefault(RootIdOf(row.Id), -1),
                flows,
                unplanned,
                row.Metrics.PlannedSp,
                row.Metrics.UnplannedSp,
                total,
                []));
        }

        if (entries.Count == 0)
        {
            return Empty with { Source = options.Source, SourceLevel = level, Width = options.Width, CapacitySp = capacity, HiddenRows = hidden };
        }

        var totalFlow = entries.Sum(e => e.Total);
        var threshold = (decimal)options.BundleShare * totalFlow;
        var small = entries.Where(e => e.Total < threshold).ToHashSet();
        var kept = entries
            .Where(e => !options.BundleSmall || small.Count < 2 || !small.Contains(e))
            .OrderByDescending(e => e.Total)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Row?.Id, StringComparer.Ordinal)
            .ToList();
        var bundled = 0;
        if (options.BundleSmall && small.Count > 1)
        {
            bundled = small.Count;
            var flows = new decimal[arts.Count];
            foreach (var entry in small)
            {
                for (var i = 0; i < arts.Count; i++)
                {
                    flows[i] += entry.Flows[i];
                }
            }

            kept.Add(new Entry(
                null,
                "Other",
                null,
                false,
                -1,
                flows,
                small.Sum(e => e.Unplanned),
                small.Sum(e => e.PlannedSp),
                small.Sum(e => e.UnplannedSp),
                small.Sum(e => e.Total),
                small.OrderByDescending(e => e.Total).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Select(e => e.Label).ToList()));
        }

        var inflow = new decimal[arts.Count];
        foreach (var entry in kept)
        {
            for (var i = 0; i < arts.Count; i++)
            {
                inflow[i] += entry.Flows[i];
            }
        }

        var outletSp = kept.Sum(e => e.Unplanned);
        var artOrder = Enumerable.Range(0, arts.Count)
            .Where(i => inflow[i] > 0m || arts[i].CapacitySp > 0m)
            .OrderByDescending(i => inflow[i])
            .ThenBy(i => arts[i].Art.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => arts[i].Art.Id)
            .ToList();

        var sourceCount = kept.Count;
        var targetCount = artOrder.Count + (outletSp > 0m ? 1 : 0);
        var sumLeft = (double)totalFlow;
        var sumRight = artOrder.Sum(i => (double)Math.Max(inflow[i], arts[i].CapacitySp)) + (double)outletSp;
        var inner = Math.Max(options.MinHeight, options.RowPitch * Math.Max(sourceCount, targetCount));
        var scale = (inner - (sourceCount - 1) * options.Gap) / sumLeft;
        if (sumRight > 0d)
        {
            scale = Math.Min(scale, (inner - (targetCount - 1) * options.Gap) / sumRight);
        }

        var usedLeft = sumLeft * scale + (sourceCount - 1) * options.Gap;
        var usedRight = sumRight * scale + (targetCount - 1) * options.Gap;
        var height = inner + 2d * options.Padding;
        var xLeft = options.LabelWidth;
        var xRight = options.Width - options.LabelWidth - options.NodeWidth;

        var sources = new List<FlowNode>(sourceCount);
        var y = options.Padding + (inner - usedLeft) / 2d;
        foreach (var entry in kept)
        {
            var nodeHeight = (double)entry.Total * scale;
            sources.Add(new FlowNode(
                entry.Row is null ? OtherKey : RowKey(entry.Row.Id),
                entry.Row is null ? FlowNodeKind.Other : FlowNodeKind.Row,
                entry.Row,
                entry.Title,
                entry.JiraId,
                entry.Total,
                entry.PlannedSp,
                entry.UnplannedSp,
                null,
                entry.Color,
                entry.IsBucket,
                entry.Bundled,
                xLeft,
                y,
                options.NodeWidth,
                nodeHeight,
                null));
            y += nodeHeight + options.Gap;
        }

        var targets = new List<FlowNode>(targetCount);
        var artOfTarget = new List<int>(targetCount);
        y = options.Padding + (inner - usedRight) / 2d;
        foreach (var i in artOrder)
        {
            var art = arts[i];
            var nodeHeight = (double)inflow[i] * scale;
            var net = Math.Min(netPlanned[i], inflow[i]);
            var capacityHeight = art.CapacitySp > 0m ? (double)art.CapacitySp * scale : (double?)null;
            artOfTarget.Add(i);
            targets.Add(new FlowNode(
                ArtKey(art.Art.Id),
                FlowNodeKind.Art,
                null,
                art.Art.Name,
                null,
                inflow[i],
                inflow[i],
                0m,
                options.ShowCapacity ? art.CapacitySp : null,
                -1,
                false,
                [],
                xRight,
                y,
                options.NodeWidth,
                nodeHeight,
                capacityHeight)
            {
                NetSp = net,
                NetHeight = (double)net * scale,
            });
            y += Math.Max(nodeHeight, capacityHeight ?? 0d) + options.Gap;
        }

        if (outletSp > 0m)
        {
            var netOutlet = Math.Min(Math.Max(0m, tree.Total.UnplannedSp), outletSp);
            artOfTarget.Add(-1);
            targets.Add(new FlowNode(
                OutletKey,
                FlowNodeKind.Outlet,
                null,
                "Not planned",
                null,
                outletSp,
                0m,
                outletSp,
                null,
                -1,
                false,
                [],
                xRight,
                y,
                options.NodeWidth,
                (double)outletSp * scale,
                null)
            {
                NetSp = netOutlet,
                NetHeight = (double)netOutlet * scale,
            });
        }

        var links = new List<FlowLink>();
        var targetOffsets = new double[targets.Count];
        var x0 = xLeft + options.NodeWidth;
        for (var s = 0; s < kept.Count; s++)
        {
            var entry = kept[s];
            var source = sources[s];
            var sourceOffset = 0d;
            for (var t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                var toOutlet = artOfTarget[t] < 0;
                var value = toOutlet ? entry.Unplanned : entry.Flows[artOfTarget[t]];
                if (value <= 0m)
                {
                    continue;
                }

                var thickness = (double)value * scale;
                var y0 = source.Y + sourceOffset + thickness / 2d;
                var y1 = target.Y + targetOffsets[t] + thickness / 2d;
                links.Add(new FlowLink(
                    links.Count,
                    source.Key,
                    target.Key,
                    value,
                    toOutlet,
                    entry.Color,
                    (double)(value / entry.Total),
                    target.Sp > 0m ? (double)(value / target.Sp) : 0d,
                    thickness,
                    Bezier(x0, y0, xRight, y1)));
                sourceOffset += thickness;
                targetOffsets[t] += thickness;
            }
        }

        return new FlowLayout(
            options.Source,
            level,
            options.Width,
            height,
            sources,
            targets,
            links,
            artOrder.Sum(i => inflow[i]),
            outletSp,
            capacity,
            hidden,
            bundled);
    }

    private static IEnumerable<PlanningRow> SourceRows(PlanningTree tree, FlowSourceLevel source)
    {
        var roots = tree.Roots.Where(r => r.Kind == PlanningRowKind.Node);
        return source == FlowSourceLevel.PortfolioEpics
            ? roots.SelectMany(r => r.Children).Where(c => c.Kind == PlanningRowKind.Node)
            : roots;
    }

    private static Dictionary<string, int> RootColors(PlanningTree tree, IReadOnlyList<int>? objectiveOrder)
    {
        var colors = new Dictionary<string, int>(StringComparer.Ordinal);
        var roots = tree.Roots.Where(r => r.Kind == PlanningRowKind.Node && !r.IsBucket).ToList();
        var free = Enumerable.Range(0, ColorCount).ToList();

        if (objectiveOrder is not null)
        {
            var fixedSlots = objectiveOrder
                .Take(ColorCount)
                .Select((id, index) => (Id: id, Index: index))
                .DistinctBy(x => x.Id)
                .ToDictionary(x => x.Id, x => x.Index);
            foreach (var root in roots)
            {
                if (root.EntityId is { } id && fixedSlots.TryGetValue(id, out var slot) && free.Remove(slot))
                {
                    colors[root.Id] = slot;
                }
            }
        }

        foreach (var root in roots)
        {
            if (colors.ContainsKey(root.Id))
            {
                continue;
            }

            if (free.Count == 0)
            {
                colors[root.Id] = -1;
                continue;
            }

            colors[root.Id] = free[0];
            free.RemoveAt(0);
        }

        return colors;
    }

    private static string RootIdOf(string rowId)
    {
        var slash = rowId.IndexOf('/');
        return slash < 0 ? rowId : rowId[..slash];
    }

    private static string Bezier(double x0, double y0, double x1, double y1)
    {
        var xm = (x0 + x1) / 2d;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"M{x0:0.##} {y0:0.##} C{xm:0.##} {y0:0.##}, {xm:0.##} {y1:0.##}, {x1:0.##} {y1:0.##}");
    }

    private sealed record ArtSlot(PlanningArt Art, HashSet<int> TeamIds, decimal CapacitySp);

    private sealed record Entry(
        PlanningRow? Row,
        string Title,
        string? JiraId,
        bool IsBucket,
        int Color,
        decimal[] Flows,
        decimal Unplanned,
        decimal PlannedSp,
        decimal UnplannedSp,
        decimal Total,
        IReadOnlyList<string> Bundled)
    {
        public string Label => string.IsNullOrEmpty(JiraId) ? Title : $"{JiraId} {Title}";
    }
}
