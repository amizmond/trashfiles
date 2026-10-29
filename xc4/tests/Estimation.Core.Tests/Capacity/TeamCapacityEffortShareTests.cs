using Estimation.Core.Capacity.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class TeamCapacityEffortShareTests
{
    private const int PiId = 1;
    private const int Argon = 1;
    private const int Boron = 2;
    private const int Cobalt = 3;

    private static readonly FeatureFindCriteria ByJiraId = new(null, ["PAY-1"], [], false, [], false, [], false);

    private readonly InMemoryDatabase _db = new();

    private Task SeedAsync(int? featureStoryPoints, params (int TeamId, int? StoryPoints, bool Primary)[] teams) =>
        _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = PiId, Name = "PI 26.11", StartDate = new DateTime(2026, 11, 2), EndDate = new DateTime(2026, 11, 13) });
            db.Teams.AddRange(
                new Team { Id = Argon, Name = "Argon" },
                new Team { Id = Boron, Name = "Boron" },
                new Team { Id = Cobalt, Name = "Cobalt" });
            db.Features.Add(new Feature { Id = 1, JiraId = "PAY-1", Summary = "Feature 1", PiId = PiId, StoryPoints = featureStoryPoints });
            foreach (var (teamId, storyPoints, primary) in teams)
            {
                db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = teamId, StoryPoints = storyPoints, IsPrimary = primary });
            }
        });

    private async Task<TeamCapacityFeatureRow> RowAsync(int teamId) =>
        Assert.Single((await new TeamCapacityService(_db).GetAsync(teamId, PiId)).Features);

    [Fact]
    public async Task Teams_without_their_own_story_points_split_the_feature_s_on_their_boards()
    {
        await SeedAsync(21, (Argon, null, false), (Boron, null, false));

        var argon = await RowAsync(Argon);
        var boron = await RowAsync(Boron);

        Assert.Equal((10.5m, false, (int?)21, true), (argon.Effort, argon.HasTeamEstimate, argon.FeatureStoryPoints, argon.IsShareOfFeature));
        Assert.Equal((10.5m, false, (int?)21, true), (boron.Effort, boron.HasTeamEstimate, boron.FeatureStoryPoints, boron.IsShareOfFeature));
        Assert.Equal([null, null], argon.Teams!.Select(t => t.StoryPoints));
    }

    [Fact]
    public async Task A_team_with_its_own_story_points_keeps_them_and_the_other_gets_what_is_left()
    {
        await SeedAsync(21, (Argon, 8, false), (Boron, null, false));

        var argon = await RowAsync(Argon);
        var boron = await RowAsync(Boron);

        Assert.Equal((8m, true, false), (argon.Effort, argon.HasTeamEstimate, argon.IsShareOfFeature));
        Assert.Equal((13m, false, true), (boron.Effort, boron.HasTeamEstimate, boron.IsShareOfFeature));
    }

    [Fact]
    public async Task A_team_alone_on_a_feature_keeps_the_feature_s_story_points()
    {
        await SeedAsync(21, (Argon, null, false));

        var argon = await RowAsync(Argon);

        Assert.Equal((21m, false), (argon.Effort, argon.IsShareOfFeature));
    }

    [Fact]
    public async Task The_primary_team_takes_the_odd_hundredth_of_an_uneven_split()
    {
        await SeedAsync(10, (Argon, null, false), (Boron, null, false), (Cobalt, null, true));

        Assert.Equal(3.33m, (await RowAsync(Argon)).Effort);
        Assert.Equal(3.33m, (await RowAsync(Boron)).Effort);
        Assert.Equal(3.34m, (await RowAsync(Cobalt)).Effort);
    }

    [Fact]
    public async Task The_add_features_dialog_shows_the_team_s_share_or_the_whole_feature_for_a_team_not_on_it()
    {
        await SeedAsync(21, (Argon, null, true), (Boron, null, false));
        var service = new TeamCapacityService(_db);

        var linked = Assert.Single(await service.FindFeaturesAsync(Argon, PiId, ByJiraId));
        var notLinked = Assert.Single(await service.FindFeaturesAsync(Cobalt, PiId, ByJiraId));

        Assert.Equal((10.5m, false, (int?)21), (linked.Effort, linked.HasTeamEstimate, linked.FeatureStoryPoints));
        Assert.Equal((21m, false), (notLinked.Effort, notLinked.LinkedToTeam));
    }
}
