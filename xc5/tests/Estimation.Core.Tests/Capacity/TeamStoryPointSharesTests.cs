using Estimation.Core.Capacity.Services;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class TeamStoryPointSharesTests
{
    private static TeamCapacityFeatureTeamRef Team(int id, int? storyPoints, bool primary = false, string? name = null) =>
        new(id, name ?? $"Team {id}", primary, storyPoints);

    [Fact]
    public void Two_teams_without_their_own_story_points_split_the_feature_s_equally()
    {
        var shares = TeamStoryPointShares.Of(21, [Team(1, null), Team(2, null)]);

        Assert.Equal(10.5m, shares[1]);
        Assert.Equal(10.5m, shares[2]);
    }

    [Fact]
    public void A_team_keeps_its_own_story_points_and_the_rest_goes_to_the_team_without()
    {
        var shares = TeamStoryPointShares.Of(21, [Team(1, 8), Team(2, null)]);

        Assert.Equal(8m, shares[1]);
        Assert.Equal(13m, shares[2]);
    }

    [Fact]
    public void Teams_with_their_own_story_points_keep_them_whatever_the_feature_s()
    {
        var shares = TeamStoryPointShares.Of(21, [Team(1, 8), Team(2, 5)]);

        Assert.Equal(8m, shares[1]);
        Assert.Equal(5m, shares[2]);
    }

    [Fact]
    public void An_uneven_split_adds_up_exactly_and_gives_the_odd_hundredth_to_the_primary_team()
    {
        var shares = TeamStoryPointShares.Of(10, [Team(1, null), Team(2, null), Team(3, null, primary: true)]);

        Assert.Equal(3.34m, shares[3]);
        Assert.Equal(3.33m, shares[1]);
        Assert.Equal(3.33m, shares[2]);
        Assert.Equal(10m, shares.Values.Sum());
    }

    [Fact]
    public void Without_a_primary_team_the_odd_hundredths_go_by_team_name()
    {
        var shares = TeamStoryPointShares.Of(1, [Team(1, null, name: "Zeta"), Team(2, null, name: "argon"), Team(3, null, name: "Boron")]);

        Assert.Equal(0.34m, shares[2]);
        Assert.Equal(0.33m, shares[3]);
        Assert.Equal(0.33m, shares[1]);
    }

    [Fact]
    public void A_team_missing_from_the_list_falls_back_to_its_own_or_the_feature_s_story_points()
    {
        Assert.Equal(10.5m, TeamStoryPointShares.ShareOf(1, null, 21, [Team(1, null), Team(2, null)]));
        Assert.Equal(21m, TeamStoryPointShares.ShareOf(3, null, 21, [Team(1, null), Team(2, null)]));
        Assert.Equal(5m, TeamStoryPointShares.ShareOf(3, 5, 21, null));
        Assert.Equal(0m, TeamStoryPointShares.ShareOf(3, null, null, null));
    }

    [Fact]
    public void Estimates_above_the_feature_s_story_points_leave_nothing_for_the_teams_without()
    {
        var shares = TeamStoryPointShares.Of(10, [Team(1, 8), Team(2, 5), Team(3, null)]);

        Assert.Equal(0m, shares[3]);
        Assert.Equal(13m, shares.Values.Sum());
    }

    [Fact]
    public void An_own_estimate_of_zero_counts_as_an_estimate()
    {
        var shares = TeamStoryPointShares.Of(21, [Team(1, 0), Team(2, null)]);

        Assert.Equal(0m, shares[1]);
        Assert.Equal(21m, shares[2]);
    }

    [Fact]
    public void Without_story_points_on_the_feature_the_teams_without_their_own_get_nothing()
    {
        var shares = TeamStoryPointShares.Of(null, [Team(1, null), Team(2, null)]);

        Assert.Equal(0m, shares[1]);
        Assert.Equal(0m, shares[2]);
    }

    [Fact]
    public void A_team_listed_twice_is_counted_once()
    {
        var shares = TeamStoryPointShares.Of(20, [Team(1, null), Team(1, null), Team(2, null)]);

        Assert.Equal(10m, shares[1]);
        Assert.Equal(10m, shares[2]);
    }
}
