using Estimation.Components.DepartmentPlanning;
using Xunit;

namespace Estimation.Tests.DepartmentPlanning;

public class PlanningDetailsPanelTests
{
    [Fact]
    public void The_team_board_link_opens_the_board_on_the_pi_of_the_details()
    {
        Assert.Equal($"{RoutingPath.Teams}/7/capacity?piId=42", PlanningDetailsPanel.TeamBoardUrl(7, 42));
    }

    [Fact]
    public void The_team_board_link_carries_the_ip_sprint_switch_when_it_is_on()
    {
        Assert.Equal($"{RoutingPath.Teams}/7/capacity?piId=42&ip=1", PlanningDetailsPanel.TeamBoardUrl(7, 42, true));
        Assert.Equal($"{RoutingPath.Teams}/7/capacity?piId=42", PlanningDetailsPanel.TeamBoardUrl(7, 42, false));
    }

    [Fact]
    public void Without_a_pi_the_team_board_link_lets_the_board_choose_its_pi()
    {
        Assert.Equal($"{RoutingPath.Teams}/7/capacity", PlanningDetailsPanel.TeamBoardUrl(7, null));
    }
}
