using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Train;

public class CapitalProjectServiceTeamLinkTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly CapitalProjectService _service;
    private readonly ArtSprintService _artSprints;

    public CapitalProjectServiceTeamLinkTests()
    {
        _service = new CapitalProjectService(_db);
        _artSprints = new ArtSprintService(_db);
    }

    private Task SeedAsync() => _db.SeedAsync(db =>
    {
        db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Atlas" });
        db.CapitalProjects.Add(new CapitalProject { Id = 2, Name = "Borealis" });
        db.Pis.Add(new Pi { Id = 7, Name = "2026.PI1", StartDate = new DateTime(2026, 1, 5), EndDate = new DateTime(2026, 4, 5) });
        db.Teams.Add(new Team { Id = 10, Name = "Regal" });
        db.Teams.Add(new Team { Id = 11, Name = "Shared Ops", IsArtSharedTeam = true });
    });

    [Fact]
    public async Task A_non_shared_team_cannot_join_a_second_art()
    {
        await SeedAsync();
        await _service.AddTeamAsync(1, 10);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.AddTeamAsync(2, 10));

        Assert.Contains("already belongs to ART 'Atlas'", ex.Message);
        Assert.Empty(await _db.ReadAsync(db => db.CapitalProjectTeams.Where(c => c.CapitalProjectId == 2).ToListAsync()));
    }

    [Fact]
    public async Task A_shared_team_can_join_several_arts()
    {
        await SeedAsync();

        await _service.AddTeamAsync(1, 11);
        await _service.AddTeamAsync(2, 11);

        var links = await _db.ReadAsync(db => db.CapitalProjectTeams.Where(c => c.TeamId == 11).ToListAsync());
        Assert.Equal(2, links.Count);
    }

    [Fact]
    public async Task Removing_a_team_unlinks_its_art_sprints_but_keeps_them()
    {
        await SeedAsync();
        await _service.AddTeamAsync(1, 10);
        await _artSprints.SaveAsync(new CapitalProjectSprint
        {
            CapitalProjectId = 1,
            PiId = 7,
            Name = "186",
            StartDate = new DateTime(2026, 1, 5),
            EndDate = new DateTime(2026, 1, 18),
            UatStart = new DateTime(2026, 1, 19),
            UatEnd = new DateTime(2026, 1, 25),
        });

        await _service.RemoveTeamAsync(1, 10);

        var sprint = Assert.Single(await _db.ReadAsync(db => db.Sprints.Where(s => s.TeamId == 10).ToListAsync()));
        Assert.Equal("Regal 186", sprint.Name);
        Assert.Null(sprint.SourceArtSprintId);
        Assert.Empty(await _db.ReadAsync(db => db.CapitalProjectTeams.Where(c => c.TeamId == 10).ToListAsync()));
    }

    [Fact]
    public async Task A_team_freed_from_its_art_can_then_join_another_one()
    {
        await SeedAsync();
        await _service.AddTeamAsync(1, 10);
        await _service.RemoveTeamAsync(1, 10);

        await _service.AddTeamAsync(2, 10);

        var link = Assert.Single(await _db.ReadAsync(db => db.CapitalProjectTeams.Where(c => c.TeamId == 10).ToListAsync()));
        Assert.Equal(2, link.CapitalProjectId);
    }
}
