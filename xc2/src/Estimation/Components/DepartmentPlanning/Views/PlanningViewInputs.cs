using System.Runtime.CompilerServices;
using Estimation.Core.DepartmentPlanning.Services;

namespace Estimation.Components.DepartmentPlanning.Views;

public readonly record struct PlanningViewInputs(
    PlanningTree Tree,
    DepartmentPlanningData? Data,
    PlanningOptions Options,
    string? SelectedId,
    string? JiraTaskUrl,
    string? PiName)
{
    public bool Equals(PlanningViewInputs other) =>
        ReferenceEquals(Tree, other.Tree)
        && ReferenceEquals(Data, other.Data)
        && ReferenceEquals(Options, other.Options)
        && string.Equals(SelectedId, other.SelectedId, StringComparison.Ordinal)
        && string.Equals(JiraTaskUrl, other.JiraTaskUrl, StringComparison.Ordinal)
        && string.Equals(PiName, other.PiName, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(
        RuntimeHelpers.GetHashCode(Tree),
        RuntimeHelpers.GetHashCode(Data),
        RuntimeHelpers.GetHashCode(Options),
        SelectedId,
        JiraTaskUrl,
        PiName);
}
