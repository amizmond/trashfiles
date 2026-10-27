using System.Globalization;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.JiraIntegration.Models;

namespace Estimation.Core.DepartmentPlanning.Views;

public enum RiskQuadrantSizeAxis
{
    Demand = 0,
    ShareOfCapacity = 1,
}

public enum RiskQuadrantRiskAxis
{
    AtRiskEpics = 0,
    AtRiskDemand = 1,
    NotPlanned = 2,
}

public enum RiskQuadrantZone
{
    DecideNow = 0,
    Protect = 1,
    Delegate = 2,
    Watch = 3,
}

public sealed record RiskQuadrantSettings
{
    public PlanningLevel Level { get; init; } = PlanningLevel.PortfolioEpic;

    public RiskQuadrantSizeAxis SizeAxis { get; init; } = RiskQuadrantSizeAxis.Demand;

    public RiskQuadrantRiskAxis RiskAxis { get; init; } = RiskQuadrantRiskAxis.AtRiskEpics;

    public int SizeThresholdSteps { get; init; }

    public int RiskThresholdSteps { get; init; }

    public string? SelectedId { get; init; }

    public DateTime? PiEnd { get; init; }

    public double Width { get; init; } = 1000d;

    public double Height { get; init; } = 600d;
}

public sealed record RiskQuadrantTick(double Value, double Position);

public sealed record RiskQuadrantPoint
{
    public required string RowId { get; init; }

    public int? EntityId { get; init; }

    public string? JiraId { get; init; }

    public required string Title { get; init; }

    public bool IsBucket { get; init; }

    public double SizeValue { get; init; }

    public double RiskValue { get; init; }

    public decimal DemandSp { get; init; }

    public decimal PlannedSp { get; init; }

    public decimal UnplannedSp { get; init; }

    public int Features { get; init; }

    public int Done { get; init; }

    public int AtRiskFeatures { get; init; }

    public decimal AtRiskSp { get; init; }

    public int Blocked { get; init; }

    public int RedFeatures { get; init; }

    public int DependenciesAtRisk { get; init; }

    public int HygieneFailingFeatures { get; init; }

    public double Progress { get; init; }

    public string? Rag { get; init; }

    public bool IsBelowArtLine { get; init; }

    public bool IsLate { get; init; }

    public int SharedWith { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Radius { get; init; }

    public double ArcRadius => Radius + 3d;

    public double ArcCircumference => 2d * Math.PI * ArcRadius;

    public double ArcLength => ArcCircumference * Math.Clamp(Progress, 0d, 1d);

    public RiskQuadrantZone Zone { get; init; }

    public bool IsSelected { get; init; }

    public string? Label { get; init; }

    public double LabelX { get; init; }

    public double LabelY { get; init; }
}

public sealed record RiskQuadrantLayout
{
    public const double MinRadius = 5d;
    public const double MaxRadius = 26d;
    public const double DefaultRiskThreshold = 0.25d;
    public const double RiskThresholdStep = 0.05d;
    private const double MarginLeft = 64d;
    private const double MarginRight = 20d;
    private const double MarginTop = 30d;
    private const double MarginBottom = 52d;
    private const double LabelCharWidth = 6.4d;
    private const double LabelHeight = 14d;
    private const int LabelMaxChars = 24;
    private const double GoldenAngle = 2.399963d;
    private const double MaxNudge = 2d * MaxRadius;
    private const int MaxShareDecimals = 6;
    private const int NudgeTries = 64;

    public static readonly RiskQuadrantLayout Empty = new();

    public IReadOnlyList<RiskQuadrantPoint> Points { get; init; } = [];

    public PlanningLevel Level { get; init; } = PlanningLevel.PortfolioEpic;

    public RiskQuadrantSizeAxis SizeAxis { get; init; }

    public bool SizeAxisSwitched { get; init; }

    public RiskQuadrantRiskAxis RiskAxis { get; init; }

    public bool RiskAxisSwitched { get; init; }

    public double Width { get; init; } = 1000d;

    public double Height { get; init; } = 600d;

    public double PlotLeft { get; init; }

    public double PlotTop { get; init; }

    public double PlotRight { get; init; }

    public double PlotBottom { get; init; }

    public double SizeMax { get; init; }

    public double RiskMax { get; init; }

    public double SizeMedian { get; init; }

    public double SizeThreshold { get; init; }

    public double SizeThresholdX { get; init; }

    public double SizeThresholdStep { get; init; }

    public double RiskThreshold { get; init; }

    public double RiskThresholdY { get; init; }

    public IReadOnlyList<RiskQuadrantTick> SizeTicks { get; init; } = [];

    public IReadOnlyList<RiskQuadrantTick> RiskTicks { get; init; } = [];

    public int Candidates { get; init; }

    public int ExcludedEmpty { get; init; }

    public int SharedCollapsed { get; init; }

    public IReadOnlyList<RiskQuadrantPoint> ListFor(RiskQuadrantZone zone) =>
        Points
            .Where(p => p.Zone == zone)
            .OrderByDescending(p => p.DemandSp)
            .ThenByDescending(p => p.RiskValue)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public int CountIn(RiskQuadrantZone zone) => Points.Count(p => p.Zone == zone);

    public static RiskQuadrantZone ZoneOf(bool big, bool risky) => (big, risky) switch
    {
        (true, true) => RiskQuadrantZone.DecideNow,
        (true, false) => RiskQuadrantZone.Protect,
        (false, true) => RiskQuadrantZone.Delegate,
        _ => RiskQuadrantZone.Watch,
    };

    public static RiskQuadrantLayout Build(PlanningTree tree, RiskQuadrantSettings settings)
    {
        var rows = Distinct(tree, settings.Level, out var collapsed);
        var excluded = rows.Count(r => r.Metrics.Features == 0);
        var kept = rows.Where(r => r.Metrics.Features > 0).ToList();

        var sizeAxis = settings.SizeAxis;
        var sizeSwitched = false;
        if (sizeAxis == RiskQuadrantSizeAxis.ShareOfCapacity && kept.All(r => r.ShareOfCapacity is null))
        {
            sizeAxis = RiskQuadrantSizeAxis.Demand;
            sizeSwitched = kept.Count > 0;
        }

        var data = kept.Select(r => Measure(r, settings, sizeAxis)).ToList();

        var riskAxis = settings.RiskAxis;
        var riskSwitched = false;
        if (riskAxis != RiskQuadrantRiskAxis.NotPlanned && data.Count > 0 && data.All(p => RiskOf(p, riskAxis) <= 0d))
        {
            riskAxis = RiskQuadrantRiskAxis.NotPlanned;
            riskSwitched = true;
        }

        data = data
            .Select(p => p with { RiskValue = RiskOf(p, riskAxis) })
            .OrderByDescending(p => p.DemandSp)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.RowId, StringComparer.Ordinal)
            .ToList();

        var width = Math.Max(settings.Width, 320d);
        var height = Math.Max(settings.Height, 240d);
        var plotLeft = MarginLeft;
        var plotRight = width - MarginRight;
        var plotTop = MarginTop;
        var plotBottom = height - MarginBottom;

        var sizeValues = data.Select(p => p.SizeValue).ToList();
        var sizeMaxValue = sizeValues.Count == 0 ? 0d : sizeValues.Max();
        var sizeStep = sizeMaxValue > 0d ? NiceStep(sizeMaxValue, 5) : 1d;
        var sizeMax = sizeMaxValue > 0d ? Math.Ceiling((sizeMaxValue / sizeStep) - 1e-9) * sizeStep : 1d;
        var median = Median(sizeValues);
        var thresholdStep = sizeStep / 2d;
        var sizeThreshold = Math.Clamp(median + (settings.SizeThresholdSteps * thresholdStep), 0d, sizeMax);

        var riskThreshold = Math.Round(Math.Clamp(DefaultRiskThreshold + (settings.RiskThresholdSteps * RiskThresholdStep), RiskThresholdStep, 1d - RiskThresholdStep), 6);
        var riskMaxValue = data.Count == 0 ? 0d : data.Max(p => p.RiskValue);
        var riskMax = Math.Min(1d, Math.Max(0.3d, (Math.Ceiling((riskMaxValue * 10d) - 1e-9) / 10d) + 0.1d));
        riskMax = Math.Max(riskMax, Math.Min(1d, riskThreshold + RiskThresholdStep));
        var riskStep = NiceStep(riskMax, 4);
        riskMax = Math.Min(1d, Math.Ceiling((riskMax / riskStep) - 1e-9) * riskStep);

        var pad = MaxRadius + 4d;
        var innerWidth = plotRight - plotLeft - (2d * pad);
        var innerHeight = plotBottom - plotTop - (2d * pad);
        double ScaleX(double value) => plotLeft + pad + (value / sizeMax * innerWidth);
        double ScaleY(double value) => plotBottom - pad - (value / riskMax * innerHeight);

        var maxDemand = data.Count == 0 ? 0m : data.Max(p => p.DemandSp);
        var selectedRow = settings.SelectedId is null ? null : tree.Find(settings.SelectedId);
        var selectedKey = selectedRow is not null && selectedRow.Level == settings.Level ? KeyOf(selectedRow.EntityId, selectedRow.Id) : null;
        var points = data
            .Select(p => p with
            {
                X = ScaleX(p.SizeValue),
                Y = ScaleY(p.RiskValue),
                Radius = RadiusOf(p.DemandSp, maxDemand),
                Zone = ZoneOf(IsBig(p.SizeValue, sizeThreshold), p.RiskValue >= riskThreshold - Tolerance),
                IsSelected = (settings.SelectedId is not null && p.RowId == settings.SelectedId)
                    || (selectedKey is not null && KeyOf(p.EntityId, p.RowId) == selectedKey),
            })
            .ToList();

        var sizeThresholdX = ScaleX(sizeThreshold);
        var riskThresholdY = ScaleY(riskThreshold);
        points = Nudge(points, plotLeft, plotTop, plotRight, plotBottom, sizeThresholdX, riskThresholdY);
        points = PlaceLabels(points, plotLeft, plotTop, plotRight, plotBottom);

        return new RiskQuadrantLayout
        {
            Points = points,
            Level = settings.Level,
            SizeAxis = sizeAxis,
            SizeAxisSwitched = sizeSwitched,
            RiskAxis = riskAxis,
            RiskAxisSwitched = riskSwitched,
            Width = width,
            Height = height,
            PlotLeft = plotLeft,
            PlotTop = plotTop,
            PlotRight = plotRight,
            PlotBottom = plotBottom,
            SizeMax = sizeMax,
            RiskMax = riskMax,
            SizeMedian = median,
            SizeThreshold = sizeThreshold,
            SizeThresholdX = sizeThresholdX,
            SizeThresholdStep = thresholdStep,
            RiskThreshold = riskThreshold,
            RiskThresholdY = riskThresholdY,
            SizeTicks = Ticks(sizeMaxValue > 0d ? sizeStep : 0d, sizeMax, ScaleX),
            RiskTicks = Ticks(riskStep, riskMax, ScaleY),
            Candidates = rows.Count,
            ExcludedEmpty = excluded,
            SharedCollapsed = collapsed,
        };
    }

    private const double Tolerance = 1e-9;

    public static bool IsBig(double size, double threshold) =>
        threshold > Tolerance ? size >= threshold - Tolerance : size > Tolerance;

    public static string SharePercent(double value, double step) => FormatPercent(value, StepDecimals(step));

    public static string ShareValuePercent(double value, double step)
    {
        var percent = Math.Abs(value * 100d);
        var significant = percent > 0d && double.IsFinite(percent) ? Math.Max(0, 1 - (int)Math.Floor(Math.Log10(percent))) : 0;
        return FormatPercent(value, Math.Min(MaxShareDecimals, Math.Max(StepDecimals(step), significant)));
    }

    private static int StepDecimals(double step)
    {
        var stepPercent = step * 100d;
        var decimals = 0;
        while (stepPercent > 0d && decimals < MaxShareDecimals)
        {
            var scaled = stepPercent * Math.Pow(10d, decimals);
            if (Math.Abs(scaled - Math.Round(scaled)) <= 1e-6 * Math.Max(1d, scaled))
            {
                break;
            }

            decimals++;
        }

        return decimals;
    }

    private static string FormatPercent(double value, int decimals)
    {
        var format = decimals == 0 ? "0" : "0." + new string('#', decimals);
        return (value * 100d).ToString(format, CultureInfo.InvariantCulture) + "%";
    }

    private static string KeyOf(int? entityId, string rowId) => entityId is { } id ? $"e:{id}" : $"r:{rowId}";

    private static List<PlanningRow> Distinct(PlanningTree tree, PlanningLevel level, out int collapsed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<PlanningRow>();
        collapsed = 0;

        foreach (var row in tree.FlattenAll())
        {
            if (row.Kind != PlanningRowKind.Node || row.Level != level)
            {
                continue;
            }

            var key = KeyOf(row.EntityId, row.Id);
            if (seen.Add(key))
            {
                rows.Add(row);
            }
            else
            {
                collapsed++;
            }
        }

        return rows;
    }

    private static RiskQuadrantPoint Measure(PlanningRow row, RiskQuadrantSettings settings, RiskQuadrantSizeAxis sizeAxis)
    {
        var leaves = Leaves(row);
        var atRisk = 0;
        var atRiskSp = 0m;
        var blocked = 0;
        var red = 0;
        var riskyDependencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leaf in leaves)
        {
            var feature = leaf.Feature!;
            if (feature.IsDone)
            {
                continue;
            }

            if (DepartmentPlanningTree.IsAtRisk(feature))
            {
                atRisk++;
                atRiskSp += leaf.Metrics.DemandSp;
            }

            riskyDependencies.UnionWith(feature.RiskyDependencyIds);

            if (feature.StatusClass == PlanningStatusClass.Blocked)
            {
                blocked++;
            }

            if (string.Equals(RagStatuses.Normalize(feature.RagStatus), RagStatuses.Red, StringComparison.OrdinalIgnoreCase))
            {
                red++;
            }
        }

        var metrics = row.Metrics;
        var targetEnd = row.TargetEnd ?? metrics.TargetEnd;

        return new RiskQuadrantPoint
        {
            RowId = row.Id,
            EntityId = row.EntityId,
            JiraId = row.JiraId,
            Title = row.Title,
            IsBucket = row.IsBucket,
            SizeValue = sizeAxis == RiskQuadrantSizeAxis.ShareOfCapacity ? row.ShareOfCapacity ?? 0d : (double)metrics.DemandSp,
            DemandSp = metrics.DemandSp,
            PlannedSp = metrics.PlannedSp,
            UnplannedSp = metrics.UnplannedSp,
            Features = metrics.Features,
            Done = metrics.Done,
            AtRiskFeatures = atRisk,
            AtRiskSp = atRiskSp,
            Blocked = blocked,
            RedFeatures = red,
            DependenciesAtRisk = riskyDependencies.Count,
            HygieneFailingFeatures = metrics.HygieneFailingFeatures,
            Progress = metrics.Progress,
            Rag = RagStatuses.Normalize(row.RagStatus) ?? metrics.WorstRag,
            IsBelowArtLine = row.IsBelowArtLine,
            IsLate = targetEnd is { } end && settings.PiEnd is { } piEnd && end.Date > piEnd.Date,
            SharedWith = row.AlsoUnder.Count,
        };
    }

    private static List<PlanningRow> Leaves(PlanningRow row)
    {
        var leaves = new List<PlanningRow>();
        var seen = new HashSet<int>();
        var stack = new Stack<PlanningRow>();
        stack.Push(row);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.Feature is { } feature)
            {
                if (seen.Add(feature.Id))
                {
                    leaves.Add(current);
                }

                continue;
            }

            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }

        return leaves;
    }

    private static double RiskOf(RiskQuadrantPoint point, RiskQuadrantRiskAxis axis) => axis switch
    {
        RiskQuadrantRiskAxis.AtRiskEpics => point.Features == 0 ? 0d : (double)point.AtRiskFeatures / point.Features,
        RiskQuadrantRiskAxis.AtRiskDemand => point.DemandSp <= 0m ? 0d : (double)(point.AtRiskSp / point.DemandSp),
        _ => point.DemandSp <= 0m ? 0d : (double)(point.UnplannedSp / point.DemandSp),
    };

    private static double RadiusOf(decimal demand, decimal maxDemand)
    {
        if (demand <= 0m || maxDemand <= 0m)
        {
            return MinRadius;
        }

        return MinRadius + ((MaxRadius - MinRadius) * Math.Sqrt((double)(demand / maxDemand)));
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0d;
        }

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    public static double NiceStep(double max, int targetTicks)
    {
        if (max <= 0d || targetTicks <= 0)
        {
            return 1d;
        }

        var raw = max / targetTicks;
        var magnitude = Math.Pow(10d, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var factor = normalized <= 1d ? 1d : normalized <= 2d ? 2d : normalized <= 2.5d ? 2.5d : normalized <= 5d ? 5d : 10d;
        return factor * magnitude;
    }

    private static List<RiskQuadrantTick> Ticks(double step, double max, Func<double, double> scale)
    {
        if (step <= 0d || max <= 0d)
        {
            return [new RiskQuadrantTick(0d, scale(0d))];
        }

        var count = (int)Math.Round(max / step, MidpointRounding.AwayFromZero);
        var ticks = new List<RiskQuadrantTick>(count + 1);
        for (var i = 0; i <= count; i++)
        {
            var value = Math.Round(i * step, 10);
            if (value > max + 1e-9)
            {
                break;
            }

            ticks.Add(new RiskQuadrantTick(value, scale(value)));
        }

        return ticks;
    }

    private static List<RiskQuadrantPoint> Nudge(List<RiskQuadrantPoint> points, double left, double top, double right, double bottom, double sizeLineX, double riskLineY)
    {
        var groups = new Dictionary<(int, int), (int Next, List<(double X, double Y)> Used)>();
        var result = new List<RiskQuadrantPoint>(points.Count);
        foreach (var point in points)
        {
            var key = ((int)Math.Round(point.X), (int)Math.Round(point.Y));
            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = (1, [(point.X, point.Y)]);
                result.Add(point);
                continue;
            }

            var big = point.Zone is RiskQuadrantZone.DecideNow or RiskQuadrantZone.Protect;
            var risky = point.Zone is RiskQuadrantZone.DecideNow or RiskQuadrantZone.Delegate;
            var (minX, maxX) = SideOf(left, right, sizeLineX, big, point.X, point.Radius);
            var (minY, maxY) = SideOf(top, bottom, riskLineY, !risky, point.Y, point.Radius);
            var slot = group.Next;
            var (x, y) = Spot(slot);
            for (var tries = 1; tries < NudgeTries && group.Used.Any(u => Math.Abs(u.X - x) < 1d && Math.Abs(u.Y - y) < 1d); tries++)
            {
                (x, y) = Spot(++slot);
            }

            group.Used.Add((x, y));
            groups[key] = (slot + 1, group.Used);
            result.Add(point with { X = x, Y = y });

            (double X, double Y) Spot(int index)
            {
                var angle = index * GoldenAngle;
                var distance = Math.Min(6d + (4d * index), MaxNudge);
                return (Within(point.X, Math.Cos(angle) * distance, minX, maxX), Within(point.Y, Math.Sin(angle) * distance, minY, maxY));
            }
        }

        return result;
    }

    private static double Within(double position, double offset, double min, double max)
    {
        var value = position + offset;
        if (value < min || value > max)
        {
            var mirrored = position - offset;
            if (mirrored >= min && mirrored <= max)
            {
                value = mirrored;
            }
        }

        return Math.Clamp(value, min, max);
    }

    private static (double Min, double Max) SideOf(double start, double end, double line, bool afterLine, double position, double radius)
    {
        if (afterLine)
        {
            var max = end - radius;
            return (Math.Min(max, Math.Min(line + radius, position)), max);
        }

        var min = start + radius;
        return (min, Math.Max(min, Math.Max(line - radius, position)));
    }

    private static List<RiskQuadrantPoint> PlaceLabels(List<RiskQuadrantPoint> points, double left, double top, double right, double bottom)
    {
        var boxes = new List<(double X, double Y, double W, double H)>();
        var result = new RiskQuadrantPoint[points.Count];
        var order = Enumerable.Range(0, points.Count).OrderBy(i => points[i].IsSelected ? 0 : 1).ToList();

        foreach (var i in order)
        {
            var point = points[i];
            if (!point.IsSelected && point.Zone != RiskQuadrantZone.DecideNow)
            {
                result[i] = point;
                continue;
            }

            var text = LabelText(point);
            var width = (text.Length * LabelCharWidth) + 6d;
            var r = point.Radius;
            (double X, double Y)[] candidates =
            [
                (point.X + r + 4d, point.Y + 4d),
                (point.X - r - 4d - width, point.Y + 4d),
                (point.X - (width / 2d), point.Y - r - 6d),
                (point.X - (width / 2d), point.Y + r + LabelHeight),
            ];

            var placed = false;
            foreach (var (x, y) in candidates)
            {
                (double X, double Y, double W, double H) box = (x, y - LabelHeight + 3d, width, LabelHeight);
                if (box.X < left || box.X + box.W > right || box.Y < top || box.Y + box.H > bottom)
                {
                    continue;
                }

                if (boxes.Any(b => Overlaps(b, box)))
                {
                    continue;
                }

                boxes.Add(box);
                result[i] = point with { Label = text, LabelX = x, LabelY = y };
                placed = true;
                break;
            }

            if (!placed)
            {
                result[i] = point.IsSelected
                    ? point with { Label = text, LabelX = candidates[0].X, LabelY = candidates[0].Y }
                    : point;
            }
        }

        return result.ToList();
    }

    private static string LabelText(RiskQuadrantPoint point)
    {
        if (!string.IsNullOrWhiteSpace(point.JiraId))
        {
            return point.JiraId;
        }

        return point.Title.Length <= LabelMaxChars ? point.Title : point.Title[..(LabelMaxChars - 1)] + "…";
    }

    private static bool Overlaps((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
}
