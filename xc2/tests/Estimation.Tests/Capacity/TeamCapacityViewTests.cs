using Estimation.Components.Capacity;
using Xunit;

namespace Estimation.Tests.Capacity;

public class TeamCapacityViewTests
{
    [Fact]
    public void A_linked_pi_wins_over_the_pi_last_used_on_the_board()
    {
        Assert.Equal(12, TeamCapacityView.ChoosePi([10, 11, 12], requested: 12, stored: 11));
    }

    [Fact]
    public void A_linked_pi_that_does_not_exist_falls_back_to_the_pi_last_used_on_the_board()
    {
        Assert.Equal(11, TeamCapacityView.ChoosePi([10, 11, 12], requested: 99, stored: 11));
    }

    [Fact]
    public void Without_a_link_the_board_opens_on_the_pi_last_used()
    {
        Assert.Equal(11, TeamCapacityView.ChoosePi([10, 11, 12], requested: null, stored: 11));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(99, null)]
    [InlineData(null, 98)]
    [InlineData(99, 98)]
    public void Without_a_known_linked_or_last_used_pi_the_board_opens_on_the_first_pi(int? requested, int? stored)
    {
        Assert.Equal(10, TeamCapacityView.ChoosePi([10, 11, 12], requested, stored));
    }

    [Fact]
    public void Without_any_pi_nothing_is_chosen()
    {
        Assert.Null(TeamCapacityView.ChoosePi([], requested: 12, stored: 11));
    }
}
