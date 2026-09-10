using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Train;

public class ArtSprintServiceTests
{
    private static readonly DateTime PiStart = new(2026, 1, 5);
    private static readonly DateTime PiEnd = new(2026, 4, 5);

    private readonly InMemoryDatabase _db = new();
    private readonly ArtSprintService _service;

    public ArtSprintServiceTests()
    {
        _service = new ArtSprintService(_db);
    }

    private Task SeedAsync(params Team[] teams) => _db.SeedAsync(db =>
    {
        db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Atlas" });
        db.Pis.Add(new Pi { Id = 7, Name = "2026.PI1", StartDate = PiStart, EndDate = PiEnd });

        foreach (var team in teams)
        {
            db.Teams.Add(team);
            db.CapitalProjectTeams.Add(new CapitalProjectTeam { CapitalProjectId = 1, TeamId = team.Id });
        }
    });

    private static CapitalProjectSprint Sprint(string name, DateTime start, DateTime end, int id = 0) => new()
    {
        Id = id,
        CapitalProjectId = 1,
        PiId = 7,
        Name = name,
        StartDate = start,
        EndDate = end,
    };

    private Task<List<Sprint>> TeamSprintsAsync(int teamId) =>
        _db.ReadAsync(db => db.Sprints.Where(s => s.TeamId == teamId).OrderBy(s => s.StartDate).ToListAsync());

    [Fact]
    public async Task Saving_creates_a_sprint_for_every_non_shared_team()
    {
        await SeedAsync(
            new Team { Id = 10, Name = "Axis Team", JiraName = "Axis" },
            new Team { Id = 11, Name = "Regal" });

        var plan = await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));

        Assert.False(plan.HasErrors);
        Assert.Equal("Axis 186", Assert.Single(await TeamSprintsAsync(10)).Name);
        Assert.Equal("Regal 186", Assert.Single(await TeamSprintsAsync(11)).Name);
    }

    [Fact]
    public async Task Shared_teams_are_not_touched_by_the_cascade()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis", IsArtSharedTeam = true });

        await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));

        Assert.Empty(await TeamSprintsAsync(10));
    }

    [Fact]
    public async Task A_sprint_with_the_same_dates_and_a_valid_name_is_adopted_unchanged()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        await _db.SeedAsync(db => db.Sprints.Add(new Sprint
        {
            Id = 90,
            TeamId = 10,
            Name = "Axis 186",
            StartDate = new DateTime(2026, 1, 5),
            EndDate = new DateTime(2026, 1, 18),
            Comment = "keep me",
        }));

        await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));

        var sprint = Assert.Single(await TeamSprintsAsync(10));
        Assert.Equal(90, sprint.Id);
        Assert.Equal("Axis 186", sprint.Name);
        Assert.Equal("keep me", sprint.Comment);
        Assert.Equal(7, sprint.PiId);
        Assert.NotNull(sprint.SourceArtSprintId);
    }

    [Fact]
    public async Task A_sprint_with_the_same_dates_but_a_foreign_name_is_renamed()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis Team", JiraName = "Axis" });
        await _db.SeedAsync(db => db.Sprints.Add(new Sprint
        {
            Id = 90,
            TeamId = 10,
            Name = "Old sprint 4",
            StartDate = new DateTime(2026, 1, 5),
            EndDate = new DateTime(2026, 1, 18),
        }));

        await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));

        var sprint = Assert.Single(await TeamSprintsAsync(10));
        Assert.Equal(90, sprint.Id);
        Assert.Equal("Axis 186", sprint.Name);
    }

    [Fact]
    public async Task A_partially_overlapping_sprint_is_dropped_and_its_comment_is_carried_over()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        await _db.SeedAsync(db => db.Sprints.Add(new Sprint
        {
            Id = 90,
            TeamId = 10,
            Name = "Axis 185",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 10),
            Comment = "carry me",
        }));

        var plan = await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));

        var sprint = Assert.Single(await TeamSprintsAsync(10));
        Assert.NotEqual(90, sprint.Id);
        Assert.Equal("Axis 186", sprint.Name);
        Assert.Equal("carry me", sprint.Comment);
        Assert.Contains(plan.Actions, a => a.Kind == ArtSprintCascadeKind.Deleted && a.SprintName == "Axis 185");
    }

    [Fact]
    public async Task Overlapping_art_sprints_are_rejected()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));

        var plan = await _service.SaveAsync(Sprint("187", new DateTime(2026, 1, 15), new DateTime(2026, 1, 28)));

        Assert.True(plan.HasErrors);
        Assert.Contains(plan.Errors, e => e.Contains("overlap"));
    }

    [Fact]
    public async Task An_end_date_on_or_before_the_start_date_is_rejected()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });

        var plan = await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 5)));

        Assert.True(plan.HasErrors);
        Assert.Contains(plan.Errors, e => e.Contains("End date"));
    }

    [Fact]
    public async Task A_gap_between_consecutive_sprints_is_reported_as_a_warning()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));

        var plan = await _service.PlanSaveAsync(Sprint("187", new DateTime(2026, 1, 26), new DateTime(2026, 2, 8)));

        Assert.False(plan.HasErrors);
        Assert.Contains(plan.Warnings, w => w.Contains("7 free day(s)"));
    }

    [Fact]
    public async Task A_sprint_that_continues_the_previous_one_produces_no_gap_warning()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));

        var plan = await _service.PlanSaveAsync(Sprint("187", new DateTime(2026, 1, 19), new DateTime(2026, 2, 1)));

        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task Dates_outside_the_selected_pi_are_reported_as_a_warning()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });

        var plan = await _service.PlanSaveAsync(Sprint("186", new DateTime(2025, 12, 1), new DateTime(2025, 12, 14)));

        Assert.False(plan.HasErrors);
        Assert.Contains(plan.Warnings, w => w.Contains("outside PI"));
    }

    [Fact]
    public async Task Editing_an_art_sprint_moves_the_derived_team_sprints()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        var art = Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18));
        await _service.SaveAsync(art);

        art.StartDate = new DateTime(2026, 1, 12);
        art.EndDate = new DateTime(2026, 1, 25);
        await _service.SaveAsync(art);

        var sprint = Assert.Single(await TeamSprintsAsync(10));
        Assert.Equal(new DateTime(2026, 1, 12), sprint.StartDate);
        Assert.Equal(new DateTime(2026, 1, 25), sprint.EndDate);
    }

    [Fact]
    public async Task Deleting_an_art_sprint_can_keep_the_team_sprints_and_unlink_them()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        var art = Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18));
        await _service.SaveAsync(art);

        await _service.DeleteAsync(art.Id, deleteTeamSprints: false);

        var sprint = Assert.Single(await TeamSprintsAsync(10));
        Assert.Null(sprint.SourceArtSprintId);
    }

    [Fact]
    public async Task Deleting_an_art_sprint_can_remove_the_team_sprints()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        var art = Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18));
        await _service.SaveAsync(art);

        await _service.DeleteAsync(art.Id, deleteTeamSprints: true);

        Assert.Empty(await TeamSprintsAsync(10));
    }

    [Fact]
    public async Task Backfill_applies_every_existing_art_sprint_to_a_newly_added_team()
    {
        await SeedAsync(new Team { Id = 10, Name = "Axis" });
        await _service.SaveAsync(Sprint("186", new DateTime(2026, 1, 5), new DateTime(2026, 1, 18)));
        await _service.SaveAsync(Sprint("187", new DateTime(2026, 1, 19), new DateTime(2026, 2, 1)));

        await _db.SeedAsync(db =>
        {
            db.Teams.Add(new Team { Id = 11, Name = "Regal" });
            db.CapitalProjectTeams.Add(new CapitalProjectTeam { CapitalProjectId = 1, TeamId = 11 });
        });

        await _service.BackfillTeamAsync(1, 11);

        var sprints = await TeamSprintsAsync(11);
        Assert.Equal(new[] { "Regal 186", "Regal 187" }, sprints.Select(s => s.Name));
    }
}
