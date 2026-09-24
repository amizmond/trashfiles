using Estimation.Core.Capacity.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

/// <summary>
/// The ART capacity view shows a "Demand capacity" table that is the team board's table rolled up over every
/// team on the ART. These cover the rolling up: one stack demanded by two teams lands on one row, the ART's
/// Allocated stays capped by each team's own member availability, and below-the-line demand is kept apart.
/// </summary>
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
        // 40 SP asked of a stack whose only member has 15 days, in each of two teams. Neither team can cover
        // its own share, so the ART is short 50 — a shortfall a single ART-wide pool would have hidden.
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
    public async Task An_art_with_no_teams_has_no_rows()
    {
        var service = new ArtCapacityService(_db, new StubTeamCapacityService(new()));

        var result = await service.GetAsync(ArtId, piId: 1);

        Assert.Empty(result.Teams);
        Assert.Empty(result.StackDemands);
    }

    private static string StackName(int stackId) => stackId == Sql ? "SQL" : "ETL";

    private static TeamMemberMeta Member(int humanResourceId, double days, params int[] stackIds) =>
        new(humanResourceId, $"Member {humanResourceId}", days, new(), new(), stackIds.ToHashSet());

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
        new(member.EffectiveDays, "PI", null, null, new(),
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
