using Estimation.Core.Dashboard.Services;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Estimation.Core.Tests.Dashboard;

public class ArtSprintTimelineTests
{
    private static readonly DateTime Today = DateTime.Today;

    private readonly InMemoryDatabase _db = new();
    private readonly DashboardService _service;

    public ArtSprintTimelineTests()
    {
        _service = new DashboardService(_db, new MemoryCache(new MemoryCacheOptions()));
    }

    private static CapitalProjectSprint ArtSprint(int id, int artId, string name, int startOffset, int endOffset) => new()
    {
        Id = id,
        CapitalProjectId = artId,
        PiId = 7,
        Name = name,
        StartDate = Today.AddDays(startOffset),
        EndDate = Today.AddDays(endOffset),
        UatStart = Today.AddDays(endOffset + 1),
        UatEnd = Today.AddDays(endOffset + 5),
    };

    private Task SeedAsync() => _db.SeedAsync(db =>
    {
        db.Pis.Add(new Pi { Id = 7, Name = "2026.PI3" });
        db.CapitalProjects.Add(new CapitalProject { Id = 1, Name = "Borealis" });
        db.CapitalProjects.Add(new CapitalProject { Id = 2, Name = "Atlas" });
        db.CapitalProjects.Add(new CapitalProject { Id = 3, Name = "No sprints yet" });

        var upcoming = ArtSprint(12, 1, "188", 11, 24);
        upcoming.SignOffDate = Today.AddDays(30);
        upcoming.ReleaseDate = Today.AddDays(33);
        upcoming.FixVersion = "R26.4";
        upcoming.IsIpSprint = true;

        db.CapitalProjectSprints.AddRange(
            upcoming,
            ArtSprint(10, 1, "186", -30, -17),
            ArtSprint(11, 1, "187", -3, 10),
            ArtSprint(20, 2, "A1", 0, 13));
    });

    [Fact]
    public async Task Only_arts_with_sprints_are_offered_in_name_order()
    {
        await SeedAsync();

        var options = await _service.GetArtSprintOptionsAsync(null);

        Assert.Equal(new[] { "Atlas", "Borealis" }, options.Select(o => o.Name));
    }

    [Fact]
    public async Task Options_are_limited_to_the_arts_the_user_may_see()
    {
        await SeedAsync();

        var options = await _service.GetArtSprintOptionsAsync([1, 3]);

        Assert.Equal("Borealis", Assert.Single(options).Name);
    }

    [Fact]
    public async Task The_timeline_runs_in_date_order_and_marks_past_and_current_sprints()
    {
        await SeedAsync();

        var timeline = await _service.GetArtSprintTimelineAsync(1);

        Assert.Equal(new[] { "186", "187", "188" }, timeline.Select(s => s.Name));
        Assert.True(timeline[0].IsPast);
        Assert.False(timeline[0].IsCurrent);
        Assert.True(timeline[1].IsCurrent);
        Assert.False(timeline[1].IsPast);
        Assert.False(timeline[2].IsCurrent);
        Assert.False(timeline[2].IsPast);
    }

    [Fact]
    public async Task Timeline_items_carry_every_sprint_detail()
    {
        await SeedAsync();

        var sprint = (await _service.GetArtSprintTimelineAsync(1)).Single(s => s.Name == "188");

        Assert.Equal("2026.PI3", sprint.PiName);
        Assert.Equal(Today.AddDays(11), sprint.DevStart);
        Assert.Equal(Today.AddDays(24), sprint.DevEnd);
        Assert.Equal(Today.AddDays(25), sprint.UatStart);
        Assert.Equal(Today.AddDays(29), sprint.UatEnd);
        Assert.Equal(Today.AddDays(30), sprint.SignOffDate);
        Assert.Equal(Today.AddDays(33), sprint.ReleaseDate);
        Assert.Equal("R26.4", sprint.FixVersion);
        Assert.True(sprint.IsIpSprint);
    }

    [Fact]
    public async Task A_sprint_starting_today_is_current()
    {
        await SeedAsync();

        var sprint = Assert.Single(await _service.GetArtSprintTimelineAsync(2));

        Assert.True(sprint.IsCurrent);
    }
}
