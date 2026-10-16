using Estimation.Core.Capacity.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class ArtCapacityStackDemandTests
{
    private const int ArtId = 1;
    private const int TeamOneId = 1;
    private const int TeamTwoId = 2;
    private const int Sql = 10;
    private const int Etl = 11;

    private readonly InMemoryDatabase _db = new();

    private async Task<ArtCapacityResult> RunAsync(params (int TeamId, TeamCapacityResult Board)[] boards)
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = ArtId, Name = "ART" });
            foreach (var (teamId, _) in boards)
            {
                db.Teams.Add(new Team { Id = teamId, Name = $"Team {teamId}" });
                db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = ArtId, TeamId = teamId });
            }
        });

        var service = new ArtCapacityService(
            _db, new StubTeamCapacityService(boards.ToDictionary(b => b.TeamId, b => b.Board)));
        return await service.GetAsync(ArtId, piId: 1);
    }

    [Fact]
    public async Task One_stack_demanded_by_two_teams_becomes_a_single_row()
    {
        var result = await RunAsync(
            (TeamOneId, Board(
                Member(TeamOneId, days: 100, Sql),
                Included(featureId: 1, (Sql, 30)))),
            (TeamTwoId, Board(
                Member(TeamTwoId, days: 100, Sql),
                Included(featureId: 2, (Sql, 20)))));

        var row = Assert.Single(result.StackDemands);
        Assert.Equal(Sql, row.TechnologyStackId);
        Assert.Equal("SQL", row.StackName);
        Assert.Equal(50m, row.Demand);
        Assert.Equal(50m, row.Allocated);
        Assert.Equal(0m, row.Remaining);
        Assert.Equal(0m, row.BelowLine);
    }

    [Fact]
    public async Task Allocated_stays_capped_by_each_team_own_members()
    {
        var result = await RunAsync(
            (TeamOneId, Board(
                Member(TeamOneId, days: 15, Sql),
                Included(featureId: 1, (Sql, 40)))),
            (TeamTwoId, Board(
                Member(TeamTwoId, days: 15, Sql),
                Included(featureId: 2, (Sql, 40)))));

        var row = Assert.Single(result.StackDemands);
        Assert.Equal(80m, row.Demand);
        Assert.Equal(30m, row.Allocated);
        Assert.Equal(50m, row.Remaining);
    }

    [Fact]
    public async Task Below_the_line_demand_is_reported_separately_and_never_allocated()
    {
        var result = await RunAsync(
            (TeamOneId, Board(
                Member(TeamOneId, days: 100, Sql),
                Included(featureId: 1, (Sql, 10)),
                Excluded(featureId: 2, (Sql, 7)))));

        var row = Assert.Single(result.StackDemands);
        Assert.Equal(10m, row.Demand);
        Assert.Equal(10m, row.Allocated);
        Assert.Equal(7m, row.BelowLine);
    }

    [Fact]
    public async Task A_feature_below_the_art_line_counts_as_below_the_line()
    {
        var belowArtLine = Included(featureId: 2, (Sql, 7));
        var result = await RunAsync(
            (TeamOneId, Board(
                Member(TeamOneId, days: 100, Sql),
                Included(featureId: 1, (Sql, 10)),
                (belowArtLine.Row with { BelowArtLine = [new ArtLineRef(ArtId, "ART")] }, belowArtLine.Demand))));

        var row = Assert.Single(result.StackDemands);
        Assert.Equal(10m, row.Demand);
        Assert.Equal(10m, row.Allocated);
        Assert.Equal(7m, row.BelowLine);
        Assert.Equal(10m, Assert.Single(result.Teams).Allocated);
    }

    [Fact]
    public async Task Teams_counting_a_day_as_different_story_points_add_up_in_days()
    {
        var result = await RunAsync(
            (TeamOneId, Board(
                Member(TeamOneId, days: 10, Sql),
                Included(featureId: 1, (Sql, 8)),
                Excluded(featureId: 3, (Sql, 4)))),
            (TeamTwoId, Board(
                Member(TeamTwoId, days: 10, Sql) with { Capacity = 20 },
                Included(featureId: 2, (Sql, 30)),
                Excluded(featureId: 4, (Sql, 6))) with { StoryPointsPerDay = 2m }));

        var row = Assert.Single(result.StackDemands);
        Assert.Equal(23m, row.Demand);
        Assert.Equal(18m, row.Allocated);
        Assert.Equal(5m, row.Remaining);
        Assert.Equal(7m, row.BelowLine);

        Assert.Collection(result.Teams,
            one =>
            {
                Assert.Equal(1m, one.StoryPointsPerDay);
                Assert.Equal(10d, one.Capacity);
                Assert.Equal(8m, one.Allocated);
            },
            two =>
            {
                Assert.Equal(2m, two.StoryPointsPerDay);
                Assert.Equal(10d, two.Capacity);
                Assert.Equal(10m, two.Allocated);
            });
        Assert.Equal(20d, result.TotalCapacity);
    }

    [Fact]
    public async Task A_stack_only_below_the_line_still_gets_a_row()
    {
        var result = await RunAsync(
            (TeamOneId, Board(
                Member(TeamOneId, days: 100, Sql, Etl),
                Included(featureId: 1, (Sql, 10)),
                Excluded(featureId: 2, (Etl, 5)))));

        Assert.Collection(result.StackDemands,
            etl =>
            {
                Assert.Equal("ETL", etl.StackName);
                Assert.Equal(0m, etl.Demand);
                Assert.Equal(0m, etl.Allocated);
                Assert.Equal(5m, etl.BelowLine);
            },
            sql =>
            {
                Assert.Equal("SQL", sql.StackName);
                Assert.Equal(10m, sql.Demand);
                Assert.Equal(0m, sql.BelowLine);
            });
    }

    [Fact]
    public async Task Team_boards_come_in_team_name_order_and_no_art_means_teams_without_one()
    {
        var zulu = Board(Member(TeamOneId, days: 10, Sql), Included(featureId: 1, (Sql, 5)));
        var alpha = Board(Member(TeamTwoId, days: 10, Sql), Included(featureId: 2, (Sql, 5)));
        var loose = Board(Member(3, days: 10, Sql), Included(featureId: 3, (Sql, 5)));
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(new Art { Id = ArtId, Name = "ART" });
            db.Teams.Add(new Team { Id = TeamOneId, Name = "Zulu" });
            db.Teams.Add(new Team { Id = TeamTwoId, Name = "Alpha" });
            db.Teams.Add(new Team { Id = 3, Name = "Loose" });
            db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = ArtId, TeamId = TeamOneId });
            db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = ArtId, TeamId = TeamTwoId });
        });
        var service = new ArtCapacityService(_db, new StubTeamCapacityService(new()
        {
            [TeamOneId] = zulu,
            [TeamTwoId] = alpha,
            [3] = loose,
        }));

        var boards = await service.GetTeamBoardsAsync(ArtId, piId: 1);
        var withoutArt = await service.GetTeamBoardsAsync(null, piId: 1);

        Assert.Equal([(TeamTwoId, "Alpha"), (TeamOneId, "Zulu")], boards.Select(b => (b.TeamId, b.TeamName)));
        Assert.Same(alpha, boards[0].Board);
        Assert.Same(zulu, boards[1].Board);
        Assert.Same(loose, Assert.Single(withoutArt).Board);
    }

    [Fact]
    public async Task An_art_with_no_teams_has_no_rows()
    {
        var service = new ArtCapacityService(_db, new StubTeamCapacityService(new()));

        var result = await service.GetAsync(ArtId, piId: 1);

        Assert.Empty(result.Teams);
        Assert.Empty(result.StackDemands);
    }

    private static string StackName(int stackId) => stackId == Sql ? "SQL" : "ETL";

    private static TeamMemberMeta Member(int humanResourceId, double days, params int[] stackIds) =>
        new(humanResourceId, $"Member {humanResourceId}", days, days, new(), new(), stackIds.ToHashSet());

    private static (TeamCapacityFeatureRow Row, TeamFeatureDemand Demand) Included(
        int featureId, params (int StackId, int StoryPoints)[] stacks) => Feature(featureId, true, stacks);

    private static (TeamCapacityFeatureRow Row, TeamFeatureDemand Demand) Excluded(
        int featureId, params (int StackId, int StoryPoints)[] stacks) => Feature(featureId, false, stacks);

    private static (TeamCapacityFeatureRow Row, TeamFeatureDemand Demand) Feature(
        int featureId, bool isIncluded, (int StackId, int StoryPoints)[] stacks) =>
        (new TeamCapacityFeatureRow(
            featureId, $"AXIS-{featureId}", $"Feature {featureId}", null, null,
            stacks.Sum(s => s.StoryPoints), isIncluded, featureId),
         new TeamFeatureDemand(
            featureId,
            stacks.Sum(s => s.StoryPoints),
            stacks.Select(s => new TeamFeatureStackDemand(
                s.StackId, StackName(s.StackId), s.StoryPoints, new())).ToList(),
            new()));

    private static TeamCapacityResult Board(
        TeamMemberMeta member, params (TeamCapacityFeatureRow Row, TeamFeatureDemand Demand)[] features) =>
        new(member.Capacity, "PI", null, null, new(),
            features.Select(f => f.Row).ToList(), new(), new() { member },
            features.ToDictionary(f => f.Row.FeatureId, f => f.Demand), new());

    private sealed class StubTeamCapacityService : ITeamCapacityService
    {
        private readonly Dictionary<int, TeamCapacityResult> _boards;

        public StubTeamCapacityService(Dictionary<int, TeamCapacityResult> boards) => _boards = boards;

        public Task<TeamCapacityResult> GetAsync(int teamId, int piId, bool includeIpSprints = false) =>
            Task.FromResult(_boards[teamId]);

        public Task SaveOrderAsync(int teamId, int piId, IList<TeamCapacityOrderRow> rows,
            IReadOnlyCollection<int>? refreshSnapshotFeatureIds = null) => throw new NotSupportedException();

        public Task<FeatureFindMetadata> GetFeatureFindMetadataAsync() => throw new NotSupportedException();

        public Task<List<FeatureFindRow>> FindFeaturesAsync(int teamId, int piId, FeatureFindCriteria criteria) =>
            throw new NotSupportedException();

        public Task<Dictionary<int, List<FeatureOtherTeamLine>>> GetFeatureOtherTeamLinesAsync(
            int teamId, int piId, IReadOnlyCollection<int> featureIds) => throw new NotSupportedException();

        public Task PropagateInclusionToOtherTeamsAsync(
            int sourceTeamId, int piId, IReadOnlyDictionary<int, bool> featureInclusion) =>
            throw new NotSupportedException();
    }
}
