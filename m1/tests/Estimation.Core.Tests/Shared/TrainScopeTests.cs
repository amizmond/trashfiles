using Estimation.Core.Features.Models;
using Estimation.Core.Shared.Services;
using Estimation.Core.Train.Services;
using Xunit;

namespace Estimation.Core.Tests.Shared;

public class TrainScopeTests
{
    [Fact]
    public void The_unrestricted_scope_carries_no_trains()
    {
        Assert.Equal(TrainScopeKind.Unrestricted, TrainScope.Unrestricted.Kind);
        Assert.Empty(TrainScope.Unrestricted.CapitalProjectIds);
    }

    [Fact]
    public void The_admin_only_scope_carries_no_trains()
    {
        Assert.Equal(TrainScopeKind.AdminOrGeneralOnly, TrainScope.AdminOrGeneralOnly.Kind);
        Assert.Empty(TrainScope.AdminOrGeneralOnly.CapitalProjectIds);
    }

    [Fact]
    public void A_scope_over_no_trains_falls_back_to_admin_only()
    {
        Assert.Same(TrainScope.AdminOrGeneralOnly, TrainScope.ForTrains(Array.Empty<int>()));
    }

    [Fact]
    public void A_scope_over_trains_keeps_the_train_ids()
    {
        var scope = TrainScope.ForTrains(new[] { 7, 9 });

        Assert.Equal(TrainScopeKind.Trains, scope.Kind);
        Assert.Equal(new[] { 7, 9 }, scope.CapitalProjectIds);
    }
}

public class SolutionTrainResolverScopeTests
{
    private static SolutionTrainResolver Resolver() => new(null!);

    private static ArtMatcher Arts(params (int ArtId, string Key)[] keys) => new(keys);

    private static Feature Issue(string? projectKey, string? jiraId = null) =>
        new() { ProjectKey = projectKey, JiraId = jiraId, Summary = "Feature" };

    [Fact]
    public void An_unidentifiable_issue_is_left_unrestricted()
    {
        var scope = Resolver().ResolveForIssue(Issue(null), Arts((1, "ATLAS")));

        Assert.Equal(TrainScopeKind.Unrestricted, scope.Kind);
    }

    [Fact]
    public void A_known_project_key_resolves_to_its_train()
    {
        var scope = Resolver().ResolveForIssue(Issue("ATLAS"), Arts((4, "ATLAS")));

        Assert.Equal(TrainScopeKind.Trains, scope.Kind);
        Assert.Equal(new[] { 4 }, scope.CapitalProjectIds);
    }

    [Fact]
    public void A_known_project_key_is_matched_without_regard_to_case()
    {
        var scope = Resolver().ResolveForIssue(Issue("atlas"), Arts((4, "ATLAS")));

        Assert.Equal(new[] { 4 }, scope.CapitalProjectIds);
    }

    [Fact]
    public void Every_key_of_a_multi_key_train_resolves_to_it()
    {
        var arts = Arts((4, "ATLAS"), (4, "ORBIT"));

        Assert.Equal(new[] { 4 }, Resolver().ResolveForIssue(Issue(null, "ORBIT-7"), arts).CapitalProjectIds);
        Assert.Equal(new[] { 4 }, Resolver().ResolveForIssue(Issue("ATLAS"), arts).CapitalProjectIds);
    }

    [Fact]
    public void An_unknown_project_key_is_restricted_to_admins()
    {
        var scope = Resolver().ResolveForIssue(Issue("MYSTERY"), Arts((4, "ATLAS")));

        Assert.Equal(TrainScopeKind.AdminOrGeneralOnly, scope.Kind);
    }

    [Fact]
    public void A_key_shared_by_two_trains_is_restricted_to_admins()
    {
        var scope = Resolver().ResolveForIssue(Issue("ATLAS"), Arts((4, "ATLAS"), (5, "ATLAS")));

        Assert.Equal(TrainScopeKind.AdminOrGeneralOnly, scope.Kind);
    }

    [Fact]
    public void A_parent_with_no_children_is_restricted_to_admins()
    {
        var scope = Resolver().ResolveForChildren(Array.Empty<Feature>(), Arts((4, "ATLAS")));

        Assert.Equal(TrainScopeKind.AdminOrGeneralOnly, scope.Kind);
    }

    [Fact]
    public void A_parent_inherits_the_trains_of_all_its_children()
    {
        var children = new[] { Issue("ATLAS"), Issue(null, "BOREALIS-9") };

        var scope = Resolver().ResolveForChildren(children, Arts((4, "ATLAS"), (5, "BOREALIS")));

        Assert.Equal(TrainScopeKind.Trains, scope.Kind);
        Assert.Equal(new[] { 4, 5 }, scope.CapitalProjectIds.OrderBy(i => i));
    }

    [Fact]
    public void Children_sharing_a_train_contribute_it_once()
    {
        var children = new[] { Issue("ATLAS"), Issue("ORBIT") };

        var scope = Resolver().ResolveForChildren(children, Arts((4, "ATLAS"), (4, "ORBIT")));

        Assert.Equal(new[] { 4 }, scope.CapitalProjectIds);
    }

    [Fact]
    public void A_resolvable_child_outweighs_an_unidentifiable_sibling()
    {
        var children = new[] { Issue(null), Issue("ATLAS") };

        var scope = Resolver().ResolveForChildren(children, Arts((4, "ATLAS")));

        Assert.Equal(TrainScopeKind.Trains, scope.Kind);
        Assert.Equal(new[] { 4 }, scope.CapitalProjectIds);
    }

    [Fact]
    public void Children_that_are_all_unidentifiable_leave_the_parent_unrestricted()
    {
        var children = new[] { Issue(null), Issue(null) };

        var scope = Resolver().ResolveForChildren(children, Arts((4, "ATLAS")));

        Assert.Equal(TrainScopeKind.Unrestricted, scope.Kind);
    }

    [Fact]
    public void Children_from_unknown_projects_restrict_the_parent_to_admins()
    {
        var children = new[] { Issue("MYSTERY"), Issue("UNKNOWN") };

        var scope = Resolver().ResolveForChildren(children, Arts((4, "ATLAS")));

        Assert.Equal(TrainScopeKind.AdminOrGeneralOnly, scope.Kind);
    }

    [Fact]
    public void A_child_on_a_shared_key_adds_no_train_to_the_parent()
    {
        var children = new[] { Issue("SHARED"), Issue("ATLAS") };

        var scope = Resolver().ResolveForChildren(children, Arts((4, "ATLAS"), (5, "SHARED"), (6, "SHARED")));

        Assert.Equal(new[] { 4 }, scope.CapitalProjectIds);
    }
}
