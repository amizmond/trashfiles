using Xunit;
using Estimation.Core.Capacity.Services;

namespace Estimation.Core.Tests;

public class TeamBoardAllocatorTests
{
    private const int StackWeb = 10;
    private const int StackDb = 20;

    private static TeamMemberMeta Member(int id, string name, double days, params int[] stackIds) =>
        new(id, name, days, days, new Dictionary<int, int>(), new Dictionary<int, string?>(), stackIds.ToHashSet());

    private static TeamFeatureDemand Demand(int featureId, params (int StackId, int Points)[] stacks) =>
        new(
            featureId,
            stacks.Sum(s => s.Points),
            stacks.Select(s => new TeamFeatureStackDemand(s.StackId, $"Stack {s.StackId}", s.Points, new()))
                .ToList(),
            new());

    [Fact]
    public void Members_with_the_most_free_days_are_drawn_on_first()
    {
        var members = new List<TeamMemberMeta> { Member(1, "Ann", 3d, StackWeb), Member(2, "Bob", 8d, StackWeb) };
        var demands = new Dictionary<int, TeamFeatureDemand> { [1] = Demand(1, (StackWeb, 10)) };

        var result = TeamBoardAllocator.Allocate(new[] { 1 }, demands, members);

        Assert.Equal(10m, result[1].Reserved);
        Assert.Equal(2, result[1].Stacks[0].Members[0].HumanResourceId);
        Assert.Equal(8m, result[1].Stacks[0].Members[0].StoryPoints);
        Assert.Equal(2m, result[1].Stacks[0].Members[1].StoryPoints);
    }

    [Fact]
    public void Members_are_drawn_on_by_their_story_point_capacity_not_their_days()
    {
        var members = new List<TeamMemberMeta>
        {
            new(1, "Ann", 5d, 10d, new Dictionary<int, int>(), new Dictionary<int, string?>(), [StackWeb]),
        };
        var demands = new Dictionary<int, TeamFeatureDemand> { [1] = Demand(1, (StackWeb, 10)) };

        var result = TeamBoardAllocator.Allocate(new[] { 1 }, demands, members);

        Assert.True(result[1].IsFullyReserved);
        Assert.Equal(10m, result[1].Stacks[0].Members[0].StoryPoints);
    }

    [Fact]
    public void Members_outside_the_stack_are_never_used()
    {
        var members = new List<TeamMemberMeta> { Member(1, "Ann", 10d, StackDb) };
        var demands = new Dictionary<int, TeamFeatureDemand> { [1] = Demand(1, (StackWeb, 4)) };

        var result = TeamBoardAllocator.Allocate(new[] { 1 }, demands, members);

        Assert.Equal(0m, result[1].Reserved);
        Assert.Empty(result[1].Stacks[0].Members);
    }

    [Fact]
    public void One_member_in_two_stacks_spends_a_single_shared_pool()
    {
        var members = new List<TeamMemberMeta> { Member(1, "Ann", 10d, StackWeb, StackDb) };
        var demands = new Dictionary<int, TeamFeatureDemand> { [1] = Demand(1, (StackWeb, 8), (StackDb, 8)) };

        var result = TeamBoardAllocator.Allocate(new[] { 1 }, demands, members);

        Assert.Equal(10m, result[1].Reserved);
        Assert.Equal(16m, result[1].Required);
        Assert.False(result[1].IsFullyReserved);
    }

    [Fact]
    public void Earlier_features_in_board_order_take_the_days_first()
    {
        var members = new List<TeamMemberMeta> { Member(1, "Ann", 6d, StackWeb) };
        var demands = new Dictionary<int, TeamFeatureDemand>
        {
            [1] = Demand(1, (StackWeb, 5)),
            [2] = Demand(2, (StackWeb, 5)),
        };

        var result = TeamBoardAllocator.Allocate(new[] { 1, 2 }, demands, members);

        Assert.Equal(5m, result[1].Reserved);
        Assert.Equal(1m, result[2].Reserved);
    }

    [Fact]
    public void Below_the_line_features_are_simply_not_passed_in_and_cost_nothing()
    {
        var members = new List<TeamMemberMeta> { Member(1, "Ann", 6d, StackWeb) };
        var demands = new Dictionary<int, TeamFeatureDemand>
        {
            [1] = Demand(1, (StackWeb, 4)),
            [2] = Demand(2, (StackWeb, 4)),
        };

        var result = TeamBoardAllocator.Allocate(new[] { 2 }, demands, members);

        Assert.Equal(4m, result[2].Reserved);
        Assert.False(result.ContainsKey(1));
    }

    [Fact]
    public void A_feature_with_no_stack_story_points_is_invalid_and_reserves_nothing()
    {
        var members = new List<TeamMemberMeta> { Member(1, "Ann", 6d, StackWeb) };
        var demands = new Dictionary<int, TeamFeatureDemand> { [1] = Demand(1) };

        var result = TeamBoardAllocator.Allocate(new[] { 1 }, demands, members);

        Assert.False(result[1].IsValid);
        Assert.False(result[1].IsFullyReserved);
        Assert.Equal(0m, result[1].Reserved);
    }
}
