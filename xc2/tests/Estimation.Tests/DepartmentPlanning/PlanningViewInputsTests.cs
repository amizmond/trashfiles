using Estimation.Components.DepartmentPlanning.Views;
using Estimation.Core.DepartmentPlanning.Services;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningViewInputsTests
{
    private static readonly PlanningTree Tree = PlanningTree.Empty;
    private static readonly PlanningOptions Options = new();
    private static readonly DepartmentPlanningData Data =
        new(new DepartmentPlanningScope(1, [10], 3), 3, "PI 26.09", false, null, null, [], [], [], [], [], []);

    private static PlanningViewInputs Inputs(
        PlanningTree? tree = null,
        DepartmentPlanningData? data = null,
        PlanningOptions? options = null,
        string? selectedId = "so:1",
        string? jiraTaskUrl = "https://jira/browse/",
        string? piName = "PI 26.09") =>
        new(tree ?? Tree, data ?? Data, options ?? Options, selectedId, jiraTaskUrl, piName);

    [Fact]
    public void The_same_tree_data_and_options_with_the_same_text_are_the_same_inputs()
    {
        var copy = Inputs(selectedId: new string("so:1".ToCharArray()), piName: string.Concat("PI ", "26.09"));

        Assert.Equal(Inputs(), copy);
        Assert.True(Inputs() == copy);
        Assert.Equal(Inputs().GetHashCode(), copy.GetHashCode());
    }

    [Fact]
    public void A_rebuilt_tree_is_new_inputs_even_when_it_holds_the_same_values()
    {
        var rebuilt = Tree with { };

        Assert.Equal(Tree, rebuilt);
        Assert.NotEqual(Inputs(), Inputs(tree: rebuilt));
    }

    [Fact]
    public void New_data_or_new_options_are_new_inputs()
    {
        Assert.NotEqual(Inputs(), Inputs(data: Data with { }));
        Assert.NotEqual(Inputs(), Inputs(options: Options with { }));
    }

    [Fact]
    public void Another_selection_pi_name_or_jira_address_is_new_inputs()
    {
        Assert.NotEqual(Inputs(), Inputs(selectedId: "so:2"));
        Assert.NotEqual(Inputs(), Inputs(selectedId: null));
        Assert.NotEqual(Inputs(), Inputs(selectedId: "SO:1"));
        Assert.NotEqual(Inputs(), Inputs(piName: "PI 26.10"));
        Assert.NotEqual(Inputs(), Inputs(jiraTaskUrl: null));
    }

    [Fact]
    public void Inputs_without_data_compare_like_any_other()
    {
        var empty = new PlanningViewInputs(PlanningTree.Empty, null, Options, null, null, null);

        Assert.Equal(empty, new PlanningViewInputs(PlanningTree.Empty, null, Options, null, null, null));
        Assert.Equal(empty.GetHashCode(), new PlanningViewInputs(PlanningTree.Empty, null, Options, null, null, null).GetHashCode());
        Assert.NotEqual(empty, empty with { Data = Data });
    }
}
