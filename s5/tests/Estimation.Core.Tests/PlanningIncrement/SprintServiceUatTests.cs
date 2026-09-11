using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.PlanningIncrement.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.PlanningIncrement;

public class SprintServiceUatTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly SprintService _service;

    public SprintServiceUatTests()
    {
        _service = new SprintService(_db);
    }

    private static Sprint NewSprint(DateTime? uatStart, DateTime? uatEnd) => new()
    {
        TeamId = 10,
        Name = "Shared 1",
        StartDate = new DateTime(2026, 9, 7),
        EndDate = new DateTime(2026, 9, 20),
        UatStart = uatStart ?? default,
        UatEnd = uatEnd ?? default,
    };

    private Task SeedTeamAsync() => _db.SeedAsync(db => db.Teams.Add(new Team { Id = 10, Name = "Shared", IsArtSharedTeam = true }));

    [Fact]
    public async Task A_team_sprint_is_created_with_its_uat_and_fix_version()
    {
        await SeedTeamAsync();
        var sprint = NewSprint(new DateTime(2026, 9, 21), new DateTime(2026, 9, 27));
        sprint.FixVersion = " R26.3 ";

        await _service.CreateAsync(sprint);

        var stored = await _db.ReadAsync(db => db.Sprints.SingleAsync());
        Assert.Equal(new DateTime(2026, 9, 27), stored.UatEnd);
        Assert.Equal("R26.3", stored.FixVersion);
    }

    [Fact]
    public async Task A_team_sprint_without_uat_is_rejected()
    {
        await SeedTeamAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(NewSprint(null, null)));

        Assert.Contains("UAT start and UAT end are required", ex.Message);
    }

    [Fact]
    public async Task A_team_sprint_whose_uat_ends_before_the_sprint_is_rejected()
    {
        await SeedTeamAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateAsync(NewSprint(new DateTime(2026, 9, 10), new DateTime(2026, 9, 15))));

        Assert.Contains("UAT cannot end before dev end", ex.Message);
    }

    [Fact]
    public async Task An_art_owned_sprint_keeps_its_uat_and_fix_version_when_edited_from_the_team()
    {
        await _db.SeedAsync(db =>
        {
            db.Teams.Add(new Team { Id = 10, Name = "Axis" });
            db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Atlas" });
            db.Pis.Add(new Pi { Id = 7, Name = "2026.PI1" });
            db.CapitalProjectSprints.Add(new CapitalProjectSprint
            {
                Id = 5,
                CapitalProjectId = 1,
                PiId = 7,
                Name = "186",
                StartDate = new DateTime(2026, 9, 7),
                EndDate = new DateTime(2026, 9, 20),
                UatStart = new DateTime(2026, 9, 21),
                UatEnd = new DateTime(2026, 9, 27),
                FixVersion = "R26.3",
                SignOffDate = new DateTime(2026, 9, 29),
                ReleaseDate = new DateTime(2026, 10, 2),
            });
            db.Sprints.Add(new Sprint
            {
                Id = 50,
                TeamId = 10,
                SourceArtSprintId = 5,
                PiId = 7,
                Name = "Axis 186",
                StartDate = new DateTime(2026, 9, 7),
                EndDate = new DateTime(2026, 9, 20),
                UatStart = new DateTime(2026, 9, 21),
                UatEnd = new DateTime(2026, 9, 27),
                FixVersion = "R26.3",
                SignOffDate = new DateTime(2026, 9, 29),
                ReleaseDate = new DateTime(2026, 10, 2),
            });
        });

        await _service.UpdateAsync(new Sprint
        {
            Id = 50,
            TeamId = 10,
            Name = "Axis 186 renamed",
            StartDate = new DateTime(2026, 9, 7),
            EndDate = new DateTime(2026, 9, 20),
            UatStart = new DateTime(2026, 10, 1),
            UatEnd = new DateTime(2026, 10, 30),
            FixVersion = "Hacked",
            SignOffDate = new DateTime(2027, 1, 1),
            ReleaseDate = null,
            Comment = "ok",
        });

        var stored = await _db.ReadAsync(db => db.Sprints.SingleAsync(s => s.Id == 50));
        Assert.Equal("Axis 186 renamed", stored.Name);
        Assert.Equal("ok", stored.Comment);
        Assert.Equal(new DateTime(2026, 9, 27), stored.UatEnd);
        Assert.Equal("R26.3", stored.FixVersion);
        Assert.Equal(new DateTime(2026, 9, 29), stored.SignOffDate);
        Assert.Equal(new DateTime(2026, 10, 2), stored.ReleaseDate);
    }

    [Fact]
    public async Task A_shared_team_sprint_stores_its_own_sign_off_and_release_dates()
    {
        await SeedTeamAsync();
        var sprint = NewSprint(new DateTime(2026, 9, 21), new DateTime(2026, 9, 27));
        sprint.SignOffDate = new DateTime(2026, 9, 29, 10, 0, 0);
        sprint.ReleaseDate = new DateTime(2026, 10, 2);

        await _service.CreateAsync(sprint);

        var stored = await _db.ReadAsync(db => db.Sprints.SingleAsync());
        Assert.Equal(new DateTime(2026, 9, 29), stored.SignOffDate);
        Assert.Equal(new DateTime(2026, 10, 2), stored.ReleaseDate);
    }
}
