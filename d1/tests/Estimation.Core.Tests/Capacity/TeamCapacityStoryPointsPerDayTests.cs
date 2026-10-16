using Estimation.Core.Capacity.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class TeamCapacityStoryPointsPerDayTests
{
    private const int PiId = 1;
    private const int TeamId = 1;
    private const int MemberId = 1;
    private const int FeatureId = 1;
    private const int Sql = 10;

    private readonly InMemoryDatabase _db = new();

    private Task SeedAsync(decimal storyPointsPerDay, int featureStoryPoints) =>
        _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi
            {
                Id = PiId,
                Name = "PI 26.11",
                StartDate = new DateTime(2026, 11, 2),
                EndDate = new DateTime(2026, 11, 13),
            });
            db.Teams.Add(new Team { Id = TeamId, Name = "Argon", StoryPointsPerDay = storyPointsPerDay });
            db.TechnologyStacks.Add(new TechnologyStack { Id = Sql, Name = "SQL" });
            db.TeamTechnologyStacks.Add(new TeamTechnologyStack { TeamId = TeamId, TechnologyStackId = Sql });
            db.HumanResources.Add(new HumanResource { Id = MemberId, EmployeeName = "ann", FullName = "Ann" });
            db.TeamMembers.Add(new TeamMember { TeamId = TeamId, HumanResourceId = MemberId });
            db.TeamMemberTechnologyStacks.Add(new TeamMemberTechnologyStack
            {
                TeamId = TeamId,
                HumanResourceId = MemberId,
                TechnologyStackId = Sql,
            });
            db.TeamMemberCoefficients.Add(new TeamMemberCoefficient
            {
                TeamId = TeamId,
                HumanResourceId = MemberId,
                PiId = PiId,
                Value = 0.5m,
            });
            db.Features.Add(new Feature { Id = FeatureId, JiraId = "PAY-1", Summary = "Feature 1", PiId = PiId });
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = FeatureId, TeamId = TeamId, StoryPoints = featureStoryPoints });
            db.FeatureTeamTechnologyStacks.Add(new FeatureTeamTechnologyStack
            {
                FeatureId = FeatureId,
                TeamId = TeamId,
                TechnologyStackId = Sql,
                StoryPoints = featureStoryPoints,
            });
        });

    [Fact]
    public async Task A_member_capacity_is_their_effective_days_times_the_team_story_points_per_day()
    {
        await SeedAsync(storyPointsPerDay: 2m, featureStoryPoints: 10);

        var board = await new TeamCapacityService(_db).GetAsync(TeamId, PiId);

        var member = Assert.Single(board.Members);
        Assert.Equal(10d, member.BusinessDays);
        Assert.Equal(5d, member.EffectiveDays);
        Assert.Equal(10d, member.Capacity);
        Assert.Equal(10d, board.TotalCapacity);
        Assert.Equal(2m, board.StoryPointsPerDay);
        Assert.Equal(10d, Assert.Single(board.MemberMeta).Capacity);
        Assert.Equal(5d, Assert.Single(board.MemberMeta).EffectiveDays);
        Assert.Equal(10d, Assert.Single(board.StackCapacities).Capacity);
    }

    [Theory]
    [InlineData(1.0, 5.0)]
    [InlineData(2.0, 10.0)]
    [InlineData(0.5, 2.5)]
    public async Task A_ten_point_feature_gets_the_member_capacity_in_story_points(double storyPointsPerDay, double reserved)
    {
        await SeedAsync((decimal)storyPointsPerDay, featureStoryPoints: 10);

        var board = await new TeamCapacityService(_db).GetAsync(TeamId, PiId);
        var allocation = TeamBoardAllocator.Allocate([FeatureId], board.Demands, board.MemberMeta)[FeatureId];

        Assert.Equal(10m, allocation.Required);
        Assert.Equal((decimal)reserved, allocation.Reserved);
    }
}
