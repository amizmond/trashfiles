using Estimation.Core.Administration.Audit;
using Estimation.Core.Features.Models;
using Estimation.Core.Features.Services;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Features;

public class FeatureTeamSprintPlanningTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly FeatureService _features;

    public FeatureTeamSprintPlanningTests()
    {
        _features = new FeatureService(_db, new StubAuditUser());
    }

    private sealed class StubAuditUser : IAuditUserProvider
    {
        public string? GetCurrentUserName() => "tester";
    }

    private static Sprint TeamSprint(int id, int teamId, int day)
    {
        var start = new DateTime(2026, 9, 1).AddDays(day - 1);
        return new Sprint
        {
            Id = id,
            TeamId = teamId,
            Name = $"S{id}",
            StartDate = start,
            EndDate = start.AddDays(9),
            UatStart = start.AddDays(10),
            UatEnd = start.AddDays(12),
        };
    }

    private Task SeedAsync() => _db.SeedAsync(db =>
    {
        db.Teams.Add(new Team { Id = 10, Name = "Axis" });
        db.Teams.Add(new Team { Id = 11, Name = "Regal" });
        db.Teams.Add(new Team { Id = 12, Name = "Not on feature" });
        db.Features.Add(new Feature { Id = 1, Summary = "Feature" });
        db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = 10, StoryPoints = 13, IsPrimary = true });
        db.FeatureTeams.Add(new FeatureTeam { FeatureId = 1, TeamId = 11 });
        db.Sprints.AddRange(
            TeamSprint(100, 10, 1),
            TeamSprint(101, 10, 11),
            TeamSprint(102, 10, 21),
            TeamSprint(200, 11, 1),
            TeamSprint(300, 12, 1));
    });

    private Task<List<FeatureTeamSprint>> PlansAsync() =>
        _db.ReadAsync(db => db.FeatureTeamSprints.OrderBy(x => x.TeamId).ThenBy(x => x.SprintId).ToListAsync());

    [Fact]
    public async Task Selections_are_stored_per_team_and_can_skip_sprints()
    {
        await SeedAsync();

        await _features.SyncTeamSprintsAsync(1, new Dictionary<int, HashSet<int>>
        {
            [10] = [100, 102],
            [11] = [200],
        });

        var selections = await _features.GetTeamSprintSelectionsAsync(1);
        Assert.Equal(new[] { 100, 102 }, selections[10].OrderBy(id => id));
        Assert.Equal(new[] { 200 }, selections[11]);
    }

    [Fact]
    public async Task Syncing_again_adds_and_removes_only_the_difference()
    {
        await SeedAsync();
        await _features.SyncTeamSprintsAsync(1, new Dictionary<int, HashSet<int>> { [10] = [100, 101] });

        await _features.SyncTeamSprintsAsync(1, new Dictionary<int, HashSet<int>> { [10] = [101, 102] });

        Assert.Equal(new[] { 101, 102 }, (await PlansAsync()).Select(p => p.SprintId));
    }

    [Fact]
    public async Task A_sprint_belonging_to_another_team_is_ignored()
    {
        await SeedAsync();

        await _features.SyncTeamSprintsAsync(1, new Dictionary<int, HashSet<int>> { [10] = [100, 200] });

        Assert.Equal(new[] { 100 }, (await PlansAsync()).Select(p => p.SprintId));
    }

    [Fact]
    public async Task A_team_that_is_not_on_the_feature_is_ignored()
    {
        await SeedAsync();

        await _features.SyncTeamSprintsAsync(1, new Dictionary<int, HashSet<int>> { [12] = [300] });

        Assert.Empty(await PlansAsync());
    }

    [Fact]
    public async Task An_empty_selection_clears_a_team()
    {
        await SeedAsync();
        await _features.SyncTeamSprintsAsync(1, new Dictionary<int, HashSet<int>> { [10] = [100], [11] = [200] });

        await _features.SyncTeamSprintsAsync(1, new Dictionary<int, HashSet<int>> { [10] = [], [11] = [200] });

        Assert.Equal(new[] { 200 }, (await PlansAsync()).Select(p => p.SprintId));
    }

    [Fact]
    public void Plans_are_deleted_with_their_feature_team_or_their_sprint()
    {
        using var db = _db.CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(FeatureTeamSprint))!;

        var toFeatureTeam = entity.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(FeatureTeam));
        var toSprint = entity.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Sprint));

        Assert.Equal(DeleteBehavior.Cascade, toFeatureTeam.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, toSprint.DeleteBehavior);
    }

    [Fact]
    public async Task Uploading_the_same_teams_keeps_their_settings_and_plans()
    {
        await SeedAsync();
        await _features.SyncTeamSprintsAsync(1, new Dictionary<int, HashSet<int>> { [10] = [100] });

        await _features.UpsertFromUploadAsync(new FeatureUploadData
        {
            ExistingFeatureId = 1,
            Summary = "Feature",
            TeamIds = [10, 11],
        });

        var axis = await _db.ReadAsync(db => db.FeatureTeams.FirstAsync(ft => ft.FeatureId == 1 && ft.TeamId == 10));
        Assert.Equal(13, axis.StoryPoints);
        Assert.True(axis.IsPrimary);
        Assert.Equal(new[] { 100 }, (await PlansAsync()).Select(p => p.SprintId));
    }

    [Fact]
    public async Task Uploading_a_changed_team_list_only_touches_the_difference()
    {
        await SeedAsync();

        await _features.UpsertFromUploadAsync(new FeatureUploadData
        {
            ExistingFeatureId = 1,
            Summary = "Feature",
            TeamIds = [10, 12],
        });

        var teams = await _db.ReadAsync(db => db.FeatureTeams.Where(ft => ft.FeatureId == 1).OrderBy(ft => ft.TeamId).ToListAsync());
        Assert.Equal(new[] { 10, 12 }, teams.Select(t => t.TeamId));
        Assert.Equal(13, teams[0].StoryPoints);
    }
}
