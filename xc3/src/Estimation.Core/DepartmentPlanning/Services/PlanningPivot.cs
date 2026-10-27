using Estimation.Core.Capacity.Services;

namespace Estimation.Core.DepartmentPlanning.Services;

public sealed record PlanningPivotRoot(PlanningRow Row, IReadOnlyDictionary<PlanningLevel, PlanningRow> Ancestors);

public sealed record PlanningPivotLine(PlanningRow Row, int Indent, PlanningPivotRoot Root);

public sealed record PlanningPivot(
    PlanningLens Lens,
    int StartLevel,
    IReadOnlyList<PlanningPivotRoot> Roots,
    IReadOnlyList<PlanningLevel> AncestorLevels)
{
    public const int MaxStartLevel = 3;

    public static readonly PlanningPivot Empty = new(PlanningLens.Objectives, 0, [], []);

    private static readonly PlanningLevel[] HierarchyOrder =
        [PlanningLevel.Art, PlanningLevel.StrategicObjective, PlanningLevel.PortfolioEpic, PlanningLevel.BusinessOutcome, PlanningLevel.BusinessEpic];

    private static readonly PlanningLevel[] RankedLevels = [PlanningLevel.PortfolioEpic, PlanningLevel.BusinessOutcome];

    public bool IsPivoted => StartLevel > 0;

    public static PlanningPivot Build(PlanningTree tree, int startLevel, PlanningSort sortBy = PlanningSort.Priority, bool sortDescending = false)
    {
        startLevel = Math.Clamp(startLevel, 0, MaxStartLevel);
        var roots = new List<PlanningPivotRoot>();
        var path = new List<PlanningRow>();
        Collect(tree.Roots, tree.Lens, startLevel, path, roots);

        var keepsTreeOrder = tree.Lens == PlanningLens.Drawdown && (startLevel == 1 || sortBy == PlanningSort.Priority);
        if (startLevel > 0 && !keepsTreeOrder)
        {
            var nodes = roots.Where(r => r.Row.Kind == PlanningRowKind.Node).ToList();
            if (sortBy == PlanningSort.Priority)
            {
                roots = RankOrder(nodes, sortDescending);
            }
            else
            {
                var sorted = DepartmentPlanningTree.SortRows(nodes.Select(r => r.Row), sortBy, sortDescending);
                var byId = nodes.GroupBy(r => r.Row.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                roots = sorted.Select(row => byId[row.Id]).ToList();
            }
        }

        var levels = roots
            .SelectMany(r => r.Ancestors.Keys)
            .Distinct()
            .OrderBy(level => Array.IndexOf(HierarchyOrder, level))
            .ToList();

        return new PlanningPivot(tree.Lens, startLevel, roots, levels);
    }

    public static bool IsStart(PlanningRow row, PlanningLens lens, int startLevel)
    {
        if (row.Kind == PlanningRowKind.LineMarker)
        {
            return lens == PlanningLens.Drawdown && startLevel == 1 && row.Depth == 1;
        }

        return (lens, startLevel) switch
        {
            (_, 0) => row.Depth == 0,
            (PlanningLens.Drawdown, 1) => row.Depth == 1,
            (PlanningLens.Objectives, 1) => row.Level == PlanningLevel.PortfolioEpic,
            (_, 2) => row.Level == PlanningLevel.BusinessOutcome,
            _ => row.Level == PlanningLevel.BusinessEpic,
        };
    }

    public IEnumerable<PlanningPivotLine> Flatten(Func<PlanningRow, bool> isExpanded)
    {
        foreach (var root in Roots)
        {
            var stack = new Stack<PlanningRow>();
            stack.Push(root.Row);
            while (stack.Count > 0)
            {
                var row = stack.Pop();
                yield return new PlanningPivotLine(row, Math.Max(0, row.Depth - root.Row.Depth), root);
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
    }

    public IEnumerable<PlanningRow> AllRows() => Flatten(_ => true).Select(line => line.Row);

    private sealed record RankKey(PlanningPivotRoot Root, bool Ranked, IReadOnlyList<PlanningRow> Path);

    private static List<PlanningPivotRoot> RankOrder(IEnumerable<PlanningPivotRoot> roots, bool descending)
    {
        var direction = descending ? -1 : 1;
        return roots
            .Select(RankKeyOf)
            .OrderBy(key => key, Comparer<RankKey>.Create((a, b) => CompareRanks(a, b, direction)))
            .Select(key => key.Root)
            .ToList();
    }

    private static RankKey RankKeyOf(PlanningPivotRoot root)
    {
        var chain = RankedLevels
            .Where(root.Ancestors.ContainsKey)
            .Select(level => root.Ancestors[level])
            .Append(root.Row)
            .ToList();
        var anchor = chain.FindLastIndex(row => row.HasArtRank);
        return anchor < 0
            ? new RankKey(root, false, chain)
            : new RankKey(root, true, chain.Skip(anchor).ToList());
    }

    private static int CompareRanks(RankKey a, RankKey b, int direction)
    {
        if (a.Ranked != b.Ranked)
        {
            return a.Ranked ? -1 : 1;
        }

        for (var i = 0; i < Math.Min(a.Path.Count, b.Path.Count); i++)
        {
            var order = CompareStep(a.Path[i], b.Path[i], a.Ranked && i == 0, direction);
            if (order != 0)
            {
                return order;
            }
        }

        return a.Path.Count.CompareTo(b.Path.Count);
    }

    private static int CompareStep(PlanningRow a, PlanningRow b, bool ranked, int direction)
    {
        if (!ranked && a.IsBucket != b.IsBucket)
        {
            return a.IsBucket ? 1 : -1;
        }

        if (a.SortRank != b.SortRank)
        {
            return a.SortRank is null ? 1 : b.SortRank is null ? -1 : direction * a.SortRank.Value.CompareTo(b.SortRank.Value);
        }

        var key = ArtOrderResolver.CompareJiraKeys(a.JiraId, b.JiraId);
        return key != 0 ? key : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
    }

    private static void Collect(
        IReadOnlyList<PlanningRow> rows,
        PlanningLens lens,
        int startLevel,
        List<PlanningRow> path,
        List<PlanningPivotRoot> roots)
    {
        foreach (var row in rows)
        {
            if (IsStart(row, lens, startLevel))
            {
                var ancestors = new Dictionary<PlanningLevel, PlanningRow>();
                foreach (var ancestor in path.Where(a => a.Kind == PlanningRowKind.Node))
                {
                    ancestors[ancestor.Level] = ancestor;
                }

                roots.Add(new PlanningPivotRoot(row, ancestors));
                continue;
            }

            if (row.Kind != PlanningRowKind.Node || row.Children.Count == 0)
            {
                continue;
            }

            path.Add(row);
            Collect(row.Children, lens, startLevel, path, roots);
            path.RemoveAt(path.Count - 1);
        }
    }
}
