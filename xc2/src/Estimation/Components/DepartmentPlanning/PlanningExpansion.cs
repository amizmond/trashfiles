using Estimation.Core.DepartmentPlanning.Services;

namespace Estimation.Components.DepartmentPlanning;

public static class PlanningExpansion
{
    public static HashSet<string> Initial(PlanningTree tree, string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? new HashSet<string>(StringComparer.Ordinal)
            : tree.FlattenAll()
                .Where(r => r.Kind == PlanningRowKind.Node && r.HasChildren && r.Depth < 3)
                .Select(r => r.Id)
                .ToHashSet(StringComparer.Ordinal);

    public static HashSet<string> Reopen(HashSet<string> expanded, PlanningTree tree, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return expanded;
        }

        var reopened = new HashSet<string>(expanded, StringComparer.Ordinal);
        reopened.UnionWith(Initial(tree, search));
        return reopened.Count == expanded.Count ? expanded : reopened;
    }
}
