using System.Globalization;
using Estimation.Core.DepartmentPlanning.Services;

namespace Estimation.Core.DepartmentPlanning.Views;

public enum TimelineRange
{
    Pi = 0,
    PiPlusQuarter = 1,
    All = 2,
}

public sealed record TimelineSettings(
    DateTime? PiStart,
    DateTime? PiEnd,
    DateTime Today,
    PlanningLevel MaxLevel = PlanningLevel.PortfolioEpic,
    TimelineRange Range = TimelineRange.Pi,
    IReadOnlySet<string>? Collapsed = null);

public sealed record TimelineTick(DateTime Date, double X, string Label, bool IsMajor);

public sealed record TimelineWindow(DateTime Start, DateTime End, double X, double Width);

public sealed record TimelineBar(
    DateTime? Start,
    DateTime? End,
    bool StartDerived,
    bool EndDerived,
    double X,
    double Width,
    double FillWidth,
    bool IsMilestone,
    bool OutsideRange,
    bool ClippedLeft,
    bool ClippedRight,
    bool RunsPastPi,
    int? DaysPastPiEnd,
    int? DurationDays,
    DateTime? ChildrenEnd,
    double? ExtensionX,
    double? ExtensionWidth,
    bool ExtensionClipped)
{
    public bool IsDerived => StartDerived || EndDerived;

    public bool HasConflict => ChildrenEnd is not null;

    public double Right => X + Width;
}

public sealed record TimelineLane(PlanningRow Row, int Index, double Top, bool IsGroup, bool IsCollapsed, int ItemCount, TimelineBar? Bar);

public sealed record TimelineUndated(PlanningRow Row, string? GroupTitle, int Descendants);

public sealed record TimelineLayout(
    DateTime? RangeStart,
    DateTime? RangeEnd,
    IReadOnlyList<TimelineTick> Ticks,
    TimelineWindow? Pi,
    double? TodayX,
    int? DaysToPiEnd,
    IReadOnlyList<TimelineLane> Lanes,
    IReadOnlyList<TimelineUndated> Undated,
    int ItemCount,
    int DatedCount,
    int OwnDatedCount,
    int PastPiCount,
    int ConflictCount)
{
    public const double LaneHeight = 26d;
    public const double BarHeight = 12d;
    public const double MinBarWidth = 0.6d;

    public static readonly TimelineLayout Empty = new(null, null, [], null, null, null, [], [], 0, 0, 0, 0, 0);

    public bool HasRange => RangeStart is not null;

    public double Height => Lanes.Count * LaneHeight;

    public static TimelineLayout Build(PlanningTree tree, TimelineSettings settings) => new Builder(tree, settings).Build();

    private sealed class Builder
    {
        private readonly PlanningTree _tree;
        private readonly TimelineSettings _settings;
        private readonly DateTime? _piStart;
        private readonly DateTime? _piEnd;
        private readonly DateTime _today;
        private readonly IReadOnlySet<string> _collapsed;
        private readonly List<TimelineLane> _lanes = [];
        private readonly List<TimelineUndated> _undated = [];
        private DateTime _rangeStart;
        private DateTime _rangeEnd;
        private double _totalDays;
        private int _items;
        private int _dated;
        private int _ownDated;
        private int _pastPi;
        private int _conflicts;
        private readonly HashSet<string> _counted = new(StringComparer.Ordinal);

        public Builder(PlanningTree tree, TimelineSettings settings)
        {
            _tree = tree;
            _settings = settings;
            _piStart = settings.PiStart?.Date;
            _piEnd = settings.PiEnd?.Date;
            if (_piStart is { } start && _piEnd is { } end && end < start)
            {
                (_piStart, _piEnd) = (end, start);
            }

            _today = settings.Today.Date;
            _collapsed = settings.Collapsed ?? new HashSet<string>();
        }

        public TimelineLayout Build()
        {
            var roots = _tree.Roots.Where(r => r.Kind == PlanningRowKind.Node).ToList();
            var (dataStart, dataEnd) = Extent();
            var range = Range(dataStart, dataEnd);
            if (range is null)
            {
                foreach (var root in roots)
                {
                    Visit(root.Children, root.Title, false);
                }

                return new TimelineLayout(null, null, [], null, null, DaysToPiEnd(), [], _undated, _items, _dated, _ownDated, _pastPi, _conflicts);
            }

            (_rangeStart, _rangeEnd) = range.Value;
            _totalDays = (_rangeEnd - _rangeStart).TotalDays;

            foreach (var root in roots)
            {
                var collapsed = _collapsed.Contains(root.Id);
                var (start, end, startDerived, endDerived) = Resolve(root);
                var bar = start is null && end is null ? null : Bar(start, end, startDerived, endDerived, null, null, root.Metrics.Progress);
                var itemCount = root.Children.Count(c => c.Kind == PlanningRowKind.Node);
                _lanes.Add(new TimelineLane(root, _lanes.Count, _lanes.Count * LaneHeight, true, collapsed, itemCount, bar));
                Visit(root.Children, root.Title, !collapsed);
            }

            return new TimelineLayout(_rangeStart, _rangeEnd, Ticks(), PiWindow(), TodayX(), DaysToPiEnd(), _lanes, _undated, _items, _dated, _ownDated, _pastPi, _conflicts);
        }

        private void Visit(IReadOnlyList<PlanningRow> rows, string groupTitle, bool emit)
        {
            foreach (var row in rows)
            {
                if (row.Kind != PlanningRowKind.Node || (row.Depth > 1 && row.Level > _settings.MaxLevel))
                {
                    continue;
                }

                var first = _counted.Add(row.EntityId is { } entityId ? $"{(int)row.Level}:{entityId}" : row.Id);
                if (first)
                {
                    _items++;
                }

                var (start, end, startDerived, endDerived) = Resolve(row);
                if (start is null && end is null)
                {
                    if (first)
                    {
                        _undated.Add(new TimelineUndated(row, groupTitle, CountShown(row.Children)));
                    }

                    continue;
                }

                if (first)
                {
                    _dated++;
                    if (row.TargetStart is not null || row.TargetEnd is not null)
                    {
                        _ownDated++;
                    }
                }

                var bar = Bar(start, end, startDerived, endDerived, row.TargetEnd?.Date, LatestBusinessEpicEnd(row.Children), row.Metrics.Progress);
                if (first && bar.RunsPastPi)
                {
                    _pastPi++;
                }

                if (first && bar.HasConflict)
                {
                    _conflicts++;
                }

                if (emit)
                {
                    _lanes.Add(new TimelineLane(row, _lanes.Count, _lanes.Count * LaneHeight, false, false, 0, bar));
                }

                Visit(row.Children, groupTitle, emit);
            }
        }

        private int CountShown(IReadOnlyList<PlanningRow> rows)
        {
            var count = 0;
            foreach (var row in rows)
            {
                if (row.Kind != PlanningRowKind.Node || row.Level > _settings.MaxLevel)
                {
                    continue;
                }

                count += 1 + CountShown(row.Children);
            }

            return count;
        }

        private static (DateTime? Start, DateTime? End, bool StartDerived, bool EndDerived) Resolve(PlanningRow row)
        {
            var ownStart = row.TargetStart?.Date;
            var ownEnd = row.TargetEnd?.Date;
            var (descendantStart, descendantEnd) = Span(row.Children);
            return (
                ownStart ?? descendantStart,
                ownEnd ?? descendantEnd,
                ownStart is null && descendantStart is not null,
                ownEnd is null && descendantEnd is not null);
        }

        private static (DateTime? Start, DateTime? End) Span(IReadOnlyList<PlanningRow> rows)
        {
            DateTime? start = null;
            DateTime? end = null;
            foreach (var row in rows)
            {
                if (row.Kind != PlanningRowKind.Node)
                {
                    continue;
                }

                Min(ref start, row.TargetStart?.Date);
                Max(ref end, row.TargetEnd?.Date);
                var (childStart, childEnd) = Span(row.Children);
                Min(ref start, childStart);
                Max(ref end, childEnd);
            }

            return (start, end);
        }

        private static DateTime? LatestBusinessEpicEnd(IReadOnlyList<PlanningRow> rows)
        {
            DateTime? end = null;
            foreach (var row in rows)
            {
                if (row.Kind != PlanningRowKind.Node)
                {
                    continue;
                }

                if (row.Level == PlanningLevel.BusinessEpic)
                {
                    Max(ref end, row.TargetEnd?.Date);
                }

                Max(ref end, LatestBusinessEpicEnd(row.Children));
            }

            return end;
        }

        private (DateTime? Start, DateTime? End) Extent()
        {
            DateTime? start = null;
            DateTime? end = null;
            foreach (var row in _tree.FlattenAll())
            {
                if (row.Kind != PlanningRowKind.Node)
                {
                    continue;
                }

                Min(ref start, row.TargetStart?.Date);
                Min(ref start, row.TargetEnd?.Date);
                Max(ref end, row.TargetStart?.Date);
                Max(ref end, row.TargetEnd?.Date);
            }

            return (start, end);
        }

        private (DateTime Start, DateTime End)? Range(DateTime? dataStart, DateTime? dataEnd)
        {
            DateTime? start;
            DateTime? end;
            var piKnown = _piStart is not null && _piEnd is not null;
            switch (_settings.Range)
            {
                case TimelineRange.Pi when piKnown:
                    start = _piStart;
                    end = _piEnd!.Value.AddDays(1);
                    break;
                case TimelineRange.PiPlusQuarter when piKnown:
                    start = _piStart!.Value.AddMonths(-3);
                    end = _piEnd!.Value.AddDays(1).AddMonths(3);
                    break;
                case TimelineRange.All when piKnown:
                    start = _piStart;
                    Min(ref start, dataStart);
                    end = _piEnd!.Value.AddDays(1);
                    Max(ref end, dataEnd?.AddDays(1));
                    break;
                default:
                    start = dataStart;
                    end = dataEnd?.AddDays(1);
                    break;
            }

            if (start is null || end is null)
            {
                return null;
            }

            var from = new DateTime(start.Value.Year, start.Value.Month, 1);
            var to = new DateTime(end.Value.Year, end.Value.Month, 1);
            if (to < end.Value)
            {
                to = to.AddMonths(1);
            }

            if (to <= from)
            {
                to = from.AddMonths(1);
            }

            return (from, to);
        }

        private double X(DateTime date) => Math.Clamp((date - _rangeStart).TotalDays / _totalDays * 100d, 0d, 100d);

        private List<TimelineTick> Ticks()
        {
            var months = new List<DateTime>();
            for (var month = _rangeStart; month < _rangeEnd; month = month.AddMonths(1))
            {
                months.Add(month);
            }

            var every = months.Count <= 15 ? 1 : months.Count <= 40 ? 3 : 12;
            var yearShown = false;
            var ticks = new List<TimelineTick>(months.Count);
            foreach (var month in months)
            {
                var major = every switch
                {
                    1 => true,
                    3 => (month.Month - 1) % 3 == 0,
                    _ => month.Month == 1,
                };
                var label = string.Empty;
                if (major)
                {
                    var withYear = !yearShown || month.Month == 1;
                    label = month.ToString(withYear ? "MMM yyyy" : "MMM", CultureInfo.InvariantCulture);
                    yearShown = true;
                }

                ticks.Add(new TimelineTick(month, X(month), label, major));
            }

            return ticks;
        }

        private TimelineWindow? PiWindow()
        {
            if (_piStart is not { } start || _piEnd is not { } end)
            {
                return null;
            }

            var x = X(start);
            return new TimelineWindow(start, end, x, Math.Max(0d, X(end.AddDays(1)) - x));
        }

        private double? TodayX() => _today >= _rangeStart && _today < _rangeEnd ? X(_today.AddHours(12)) : null;

        private int? DaysToPiEnd() => _piEnd is { } end ? (end - _today).Days : null;

        private TimelineBar Bar(DateTime? start, DateTime? end, bool startDerived, bool endDerived, DateTime? ownEnd, DateTime? descendantsEnd, double progress)
        {
            var from = start ?? end!.Value;
            var to = end ?? start!.Value;
            if (to < from)
            {
                (from, to) = (to, from);
            }

            var isMilestone = start is null || end is null;
            var right = to.AddDays(1);
            var outside = right <= _rangeStart || from >= _rangeEnd;
            var x = X(from);
            var width = isMilestone || outside ? 0d : X(right) - x;
            if (!isMilestone && !outside && width < MinBarWidth)
            {
                width = MinBarWidth;
                x = Math.Min(x, 100d - width);
            }

            var fill = width * Math.Clamp(progress, 0d, 1d);
            var runsPastPi = _piEnd is { } piEnd && to > piEnd;
            var daysPastPiEnd = _piEnd is { } pe ? (to - pe).Days : (int?)null;

            DateTime? childrenEnd = null;
            double? extensionX = null;
            double? extensionWidth = null;
            var extensionClipped = false;
            if (ownEnd is { } own && descendantsEnd is { } descendants && descendants > own)
            {
                childrenEnd = descendants;
                var extensionFrom = own.AddDays(1);
                var extensionTo = descendants.AddDays(1);
                extensionClipped = extensionTo > _rangeEnd;
                if (extensionFrom < _rangeEnd && extensionTo > _rangeStart)
                {
                    extensionX = X(extensionFrom);
                    extensionWidth = X(extensionTo) - extensionX;
                }
                else if (extensionFrom >= _rangeEnd)
                {
                    extensionX = 100d;
                    extensionWidth = 0d;
                }
            }

            return new TimelineBar(
                start,
                end,
                startDerived,
                endDerived,
                x,
                width,
                fill,
                isMilestone,
                outside,
                from < _rangeStart,
                right > _rangeEnd,
                runsPastPi,
                daysPastPiEnd,
                isMilestone ? null : (to - from).Days + 1,
                childrenEnd,
                extensionX,
                extensionWidth,
                extensionClipped);
        }

        private static void Min(ref DateTime? current, DateTime? candidate)
        {
            if (candidate is { } value && (current is null || value < current))
            {
                current = value;
            }
        }

        private static void Max(ref DateTime? current, DateTime? candidate)
        {
            if (candidate is { } value && (current is null || value > current))
            {
                current = value;
            }
        }
    }
}
