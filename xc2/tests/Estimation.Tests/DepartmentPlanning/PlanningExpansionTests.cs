using Estimation.Components.DepartmentPlanning;
using Estimation.Core.DepartmentPlanning.Services;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningExpansionTests
{
    private static PlanningRow Row(string id, PlanningLevel level, int depth, params PlanningRow[] children) => new()
    {
        Id = id,
        Level = level,
        Title = id,
        Depth = depth,
        Children = children,
    };

    private static PlanningTree Tree() => PlanningTree.Empty with
    {
        Roots =
        [
            Row("so:1", PlanningLevel.StrategicObjective, 0,
                Row("so:1/pe:1", PlanningLevel.PortfolioEpic, 1,
                    Row("so:1/pe:1/bo:1", PlanningLevel.BusinessOutcome, 2,
                        Row("so:1/pe:1/bo:1/f:1", PlanningLevel.BusinessEpic, 3)))),
            Row("so:2", PlanningLevel.StrategicObjective, 0),
        ],
    };

    [Fact]
    public void Without_a_search_every_row_starts_collapsed()
    {
        Assert.Empty(PlanningExpansion.Initial(Tree(), null));
        Assert.Empty(PlanningExpansion.Initial(Tree(), "  "));
    }

    [Fact]
    public void With_a_search_every_row_with_children_opens_so_the_matches_show()
    {
        var expanded = PlanningExpansion.Initial(Tree(), "rails");

        Assert.Equal(["so:1", "so:1/pe:1", "so:1/pe:1/bo:1"], expanded.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_filter_change_during_a_search_opens_the_new_matches_and_keeps_what_the_user_opened()
    {
        var expanded = new HashSet<string>(["so:9"], StringComparer.Ordinal);

        var reopened = PlanningExpansion.Reopen(expanded, Tree(), "rails");

        Assert.Equal(["so:1", "so:1/pe:1", "so:1/pe:1/bo:1", "so:9"], reopened.Order(StringComparer.Ordinal));
        Assert.Equal("so:9", Assert.Single(expanded));
    }

    [Fact]
    public void A_filter_change_without_new_matches_or_a_search_keeps_the_same_set()
    {
        var expanded = new HashSet<string>(["so:9"], StringComparer.Ordinal);
        var open = PlanningExpansion.Initial(Tree(), "rails");

        Assert.Same(expanded, PlanningExpansion.Reopen(expanded, Tree(), null));
        Assert.Same(open, PlanningExpansion.Reopen(open, Tree(), "rails"));
    }
}
