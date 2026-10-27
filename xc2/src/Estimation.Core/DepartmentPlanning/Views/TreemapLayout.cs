using System.Globalization;
using System.Text;
using Estimation.Core.DepartmentPlanning.Services;

namespace Estimation.Core.DepartmentPlanning.Views;

public enum TreemapArea
{
    Demand = 0,
    Planned = 1,
}

public enum TreemapTileKind
{
    Row = 0,
    More = 1,
    Unused = 2,
}

public enum TreemapLabelMode
{
    None = 0,
    Key = 1,
    Full = 2,
}

public readonly record struct TreemapRect(double X, double Y, double Width, double Height)
{
    public static readonly TreemapRect Empty = new(0d, 0d, 0d, 0d);

    public double Area => Width * Height;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool IsEmpty => Width <= 0d || Height <= 0d;

    public TreemapRect Shrink(double gap)
    {
        var half = gap / 2d;
        return new TreemapRect(X + half, Y + half, Math.Max(0d, Width - gap), Math.Max(0d, Height - gap));
    }
}

public sealed record TreemapTile(
    string RowId,
    PlanningLevel Level,
    double X,
    double Y,
    double Width,
    double Height,
    double HatchedHeight,
    TreemapLabelMode LabelMode,
    int LabelChars,
    int Depth,
    bool HasChildrenDrawn,
    TreemapTileKind Kind,
    int MoreCount,
    decimal Value)
{
    public string? ParentId { get; init; }

    public double HeaderHeight => HasChildrenDrawn ? TreemapLayout.HeaderHeight(Depth) : 0d;

    public double FontSize => TreemapLayout.FontSize(Depth, HasChildrenDrawn);

    public TreemapRect Rect => new(X, Y, Width, Height);

    public TreemapRect HatchedRect => new(X, Y + Height - HatchedHeight, Width, HatchedHeight);
}

public sealed record TreemapLayoutResult(
    TreemapArea Area,
    PlanningRow? Root,
    IReadOnlyList<PlanningRow> Path,
    IReadOnlyList<TreemapTile> Tiles,
    int HiddenRows,
    decimal UnusedCapacitySp)
{
    public static readonly TreemapLayoutResult Empty = new(TreemapArea.Demand, null, [], [], 0, 0m);

    public bool IsZoomed => Root is not null;

    public bool HasUnusedTile => Tiles.Any(t => t.Kind == TreemapTileKind.Unused);
}

public static class TreemapLayout
{
    public const double Width = 1600d;
    public const double Height = 900d;
    public const int MaxDepth = 3;
    public const double HatchSpacing = 9d;

    private const double MinArea = 150d;
    private const double MinTileSide = 6d;
    private const double MinMoreArea = 1600d;
    private const double MinMoreSide = 44d;
    private const double MaxMoreShare = 0.25d;
    private const double MinInnerSide = 18d;
    private const double CharWidth = 0.56d;
    private const double LineHeight = 1.25d;
    private const double TextPad = 6d;

    private static readonly double[] HeaderHeights = [26d, 22d, 19d];
    private static readonly double[] HeaderFonts = [16d, 14d, 12d];
    private static readonly double[] LeafFonts = [15d, 13d, 12d];
    private static readonly double[] Gaps = [6d, 4d, 3d];

    public static double HeaderHeight(int depth) => HeaderHeights[Math.Clamp(depth, 0, HeaderHeights.Length - 1)];

    public static double FontSize(int depth, bool header) => (header ? HeaderFonts : LeafFonts)[Math.Clamp(depth, 0, LeafFonts.Length - 1)];

    public static decimal ValueOf(PlanningMetrics metrics, TreemapArea area) =>
        area == TreemapArea.Planned ? metrics.PlannedSp : metrics.DemandSp;

    public static TreemapLayoutResult Build(PlanningTree tree, TreemapArea area, int depth, string? zoomRootId, bool showUnused = true)
    {
        depth = Math.Clamp(depth, 1, MaxDepth);
        var canvas = new TreemapRect(0d, 0d, Width, Height);
        var tiles = new List<TreemapTile>();
        var hidden = 0;

        var root = zoomRootId is null ? null : tree.Find(zoomRootId);
        if (root is not null)
        {
            LayoutChildren(root.Children, canvas, area, depth, 0, root.Id, tiles, ref hidden);
            return new TreemapLayoutResult(area, root, tree.PathTo(root.Id), tiles, hidden, 0m);
        }

        var total = tree.Roots.Where(r => r.Kind == PlanningRowKind.Node).Sum(r => Math.Max(0m, ValueOf(r.Metrics, area)));
        var unused = showUnused && tree.TotalCapacitySp > 0m ? Math.Max(0m, tree.TotalCapacitySp - tree.Total.PlannedSp) : 0m;
        var region = canvas;

        if (unused > 0m)
        {
            var drawn = total > 0m ? Math.Max((double)total, MinVisibleArea(canvas) / canvas.Area * (double)(total + unused)) : 0d;
            var split = Squarify([drawn, (double)unused], canvas);
            var rect = split[1].Shrink(Gaps[0]);
            if (Drawable(rect))
            {
                region = split[0];
                var (mode, chars) = Label(rect, 0, false, "Unused capacity");
                tiles.Add(new TreemapTile("unused", PlanningLevel.StrategicObjective, rect.X, rect.Y, rect.Width, rect.Height, 0d, mode, chars, 0, false, TreemapTileKind.Unused, 0, unused));
            }
        }

        if (total <= 0m)
        {
            hidden += tree.Roots.Count(r => r.Kind == PlanningRowKind.Node);
        }
        else
        {
            LayoutChildren(tree.Roots, region, area, depth, 0, null, tiles, ref hidden);
        }

        return new TreemapLayoutResult(area, null, [], tiles, hidden, unused);
    }

    public static IReadOnlyList<TreemapRect> Squarify(IReadOnlyList<double> values, TreemapRect bounds)
    {
        var result = new TreemapRect[values.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new TreemapRect(bounds.X, bounds.Y, 0d, 0d);
        }

        var order = Enumerable.Range(0, values.Count)
            .Where(i => double.IsFinite(values[i]) && values[i] > 0d)
            .OrderByDescending(i => values[i])
            .ToList();
        var sum = order.Sum(i => values[i]);
        if (order.Count == 0 || sum <= 0d || bounds.IsEmpty)
        {
            return result;
        }

        var scale = bounds.Area / sum;
        var areas = order.Select(i => values[i] * scale).ToList();
        var x = bounds.X;
        var y = bounds.Y;
        var w = bounds.Width;
        var h = bounds.Height;
        var start = 0;

        while (start < areas.Count)
        {
            var side = Math.Min(w, h);
            var end = start + 1;
            var rowSum = areas[start];
            var min = rowSum;
            var max = rowSum;
            var worst = Worst(rowSum, min, max, side);

            while (end < areas.Count)
            {
                var next = areas[end];
                var candidate = Worst(rowSum + next, Math.Min(min, next), Math.Max(max, next), side);
                if (candidate > worst)
                {
                    break;
                }

                rowSum += next;
                min = Math.Min(min, next);
                max = Math.Max(max, next);
                worst = candidate;
                end++;
            }

            var last = end == areas.Count;
            if (w >= h)
            {
                var stripWidth = last ? w : Math.Min(w, rowSum / h);
                var cursor = y;
                for (var i = start; i < end; i++)
                {
                    var itemHeight = i == end - 1 ? y + h - cursor : areas[i] / stripWidth;
                    result[order[i]] = new TreemapRect(x, cursor, stripWidth, Math.Max(0d, itemHeight));
                    cursor += itemHeight;
                }

                x += stripWidth;
                w -= stripWidth;
            }
            else
            {
                var stripHeight = last ? h : Math.Min(h, rowSum / w);
                var cursor = x;
                for (var i = start; i < end; i++)
                {
                    var itemWidth = i == end - 1 ? x + w - cursor : areas[i] / stripHeight;
                    result[order[i]] = new TreemapRect(cursor, y, Math.Max(0d, itemWidth), stripHeight);
                    cursor += itemWidth;
                }

                y += stripHeight;
                h -= stripHeight;
            }

            start = end;
        }

        return result;
    }

    public static string Fit(string? text, int chars)
    {
        if (string.IsNullOrEmpty(text) || chars <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= chars)
        {
            return text;
        }

        return chars == 1 ? "…" : text[..(chars - 1)].TrimEnd() + "…";
    }

    public static string HatchPath(TreemapRect band, double spacing = HatchSpacing)
    {
        if (band.IsEmpty || spacing <= 0d)
        {
            return string.Empty;
        }

        var path = new StringBuilder();
        for (var c = spacing; c < band.Width + band.Height; c += spacing)
        {
            var from = Math.Max(0d, c - band.Height);
            var to = Math.Min(band.Width, c);
            if (to <= from)
            {
                continue;
            }

            path.Append('M').Append(Num(band.X + from)).Append(' ').Append(Num(band.Y + c - from))
                .Append('L').Append(Num(band.X + to)).Append(' ').Append(Num(band.Y + c - to));
        }

        return path.ToString();
    }

    private static bool LayoutChildren(
        IReadOnlyList<PlanningRow> rows,
        TreemapRect rect,
        TreemapArea area,
        int depthLeft,
        int nesting,
        string? parentId,
        List<TreemapTile> tiles,
        ref int hidden)
    {
        var items = new List<(PlanningRow Row, double Value)>();
        foreach (var row in rows)
        {
            if (row.Kind != PlanningRowKind.Node)
            {
                continue;
            }

            var value = (double)ValueOf(row.Metrics, area);
            if (value <= 0d)
            {
                hidden++;
                continue;
            }

            items.Add((row, value));
        }

        if (items.Count == 0 || rect.IsEmpty)
        {
            return false;
        }

        items.Sort((a, b) => b.Value.CompareTo(a.Value));
        var gap = Gaps[Math.Min(nesting, Gaps.Length - 1)];
        var scale = rect.Area / items.Sum(i => i.Value);
        var minMoreValue = MinVisibleArea(rect) / scale;
        var kept = items.TakeWhile(i => i.Value * scale >= MinArea).ToList();
        var rest = items.Skip(kept.Count).ToList();
        var rects = Squarify(Values(), rect);
        while (kept.Count > 0)
        {
            var thin = Enumerable.Range(0, kept.Count).Where(i => !Drawable(rects[i].Shrink(gap))).ToList();
            if (thin.Count == 0 && rest.Count > 0 && !Drawable(rects[kept.Count].Shrink(gap)))
            {
                thin.Add(kept.Count - 1);
            }

            if (thin.Count == 0)
            {
                break;
            }

            var first = thin.Min();
            rest.InsertRange(0, kept.Skip(first));
            kept.RemoveRange(first, kept.Count - first);
            rects = Squarify(Values(), rect);
        }

        var collapsed = rest.Count;

        for (var i = 0; i < kept.Count; i++)
        {
            var (row, _) = kept[i];
            var tileRect = rects[i].Shrink(gap);
            var drawChildren = depthLeft > 1 && row.Children.Any(c => c.Kind == PlanningRowKind.Node && ValueOf(c.Metrics, area) > 0m);
            var inner = TreemapRect.Empty;
            if (drawChildren)
            {
                var header = HeaderHeight(nesting);
                inner = new TreemapRect(tileRect.X + gap, tileRect.Y + header, tileRect.Width - 2d * gap, tileRect.Height - header - gap);
                drawChildren = inner.Width >= MinInnerSide && inner.Height >= MinInnerSide;
            }

            var childTiles = new List<TreemapTile>();
            var childHidden = 0;
            if (drawChildren && !LayoutChildren(row.Children, inner, area, depthLeft - 1, nesting + 1, row.Id, childTiles, ref childHidden))
            {
                drawChildren = false;
            }

            var (mode, chars) = Label(tileRect, nesting, drawChildren, row.JiraId ?? row.Title);
            var hatch = drawChildren ? 0d : Hatch(row.Metrics, area, tileRect.Height);
            tiles.Add(new TreemapTile(row.Id, row.Level, tileRect.X, tileRect.Y, tileRect.Width, tileRect.Height, hatch, mode, chars, nesting, drawChildren, TreemapTileKind.Row, 0, ValueOf(row.Metrics, area))
            {
                ParentId = parentId,
            });

            if (drawChildren)
            {
                tiles.AddRange(childTiles);
                hidden += childHidden;
            }
        }

        if (collapsed == 0)
        {
            return true;
        }

        var moreRect = rects[kept.Count].Shrink(gap);
        if (moreRect.IsEmpty)
        {
            return kept.Count > 0;
        }

        var demand = rest.Sum(r => r.Row.Metrics.DemandSp);
        var unplanned = rest.Sum(r => r.Row.Metrics.UnplannedSp);
        var moreValue = rest.Sum(r => ValueOf(r.Row.Metrics, area));
        var moreHatch = area == TreemapArea.Demand && demand > 0m ? moreRect.Height * (double)(unplanned / demand) : 0d;
        var (moreMode, moreChars) = Label(moreRect, nesting, false, $"+{collapsed} more");
        tiles.Add(new TreemapTile($"{parentId ?? "root"}/more", rest[0].Row.Level, moreRect.X, moreRect.Y, moreRect.Width, moreRect.Height, Math.Clamp(moreHatch, 0d, moreRect.Height), moreMode, moreChars, nesting, false, TreemapTileKind.More, collapsed, moreValue)
        {
            ParentId = parentId,
        });

        return kept.Count > 0;

        List<double> Values()
        {
            var values = kept.Select(i => i.Value).ToList();
            if (rest.Count > 0)
            {
                values.Add(Math.Max(rest.Sum(i => i.Value), minMoreValue));
            }

            return values;
        }
    }

    private static double MinVisibleArea(TreemapRect rect) =>
        Math.Min(Math.Max(MinMoreArea, MinMoreSide * Math.Min(rect.Width, rect.Height)), rect.Area * MaxMoreShare);

    private static bool Drawable(TreemapRect rect) => Math.Min(rect.Width, rect.Height) >= MinTileSide;

    private static (TreemapLabelMode Mode, int Chars) Label(TreemapRect rect, int nesting, bool header, string key)
    {
        var font = FontSize(nesting, header);
        var chars = (int)Math.Floor((rect.Width - 2d * TextPad) / (font * CharWidth));
        if (chars < 3)
        {
            return (TreemapLabelMode.None, 0);
        }

        var line = font * LineHeight;
        var keyLength = Math.Max(1, key.Length);
        if (header)
        {
            if (rect.Height < HeaderHeight(nesting))
            {
                return (TreemapLabelMode.None, 0);
            }

            return chars >= keyLength + 8 ? (TreemapLabelMode.Full, chars) : (TreemapLabelMode.Key, chars);
        }

        if (rect.Height >= 2d * line + TextPad && chars >= keyLength + 4)
        {
            return (TreemapLabelMode.Full, chars);
        }

        if (rect.Height >= line + TextPad && chars >= Math.Min(keyLength, 6))
        {
            return (TreemapLabelMode.Key, chars);
        }

        return (TreemapLabelMode.None, 0);
    }

    private static double Hatch(PlanningMetrics metrics, TreemapArea area, double height)
    {
        if (area != TreemapArea.Demand || metrics.DemandSp <= 0m || metrics.UnplannedSp <= 0m)
        {
            return 0d;
        }

        return Math.Clamp(height * (double)(metrics.UnplannedSp / metrics.DemandSp), 0d, height);
    }

    private static double Worst(double sum, double min, double max, double side)
    {
        var s2 = sum * sum;
        var w2 = side * side;
        return Math.Max(w2 * max / s2, s2 / (w2 * min));
    }

    private static string Num(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);
}
