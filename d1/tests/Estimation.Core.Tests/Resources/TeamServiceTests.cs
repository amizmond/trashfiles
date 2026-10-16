using Estimation.Core.Resources.Models;
using Estimation.Core.Resources.Services;
using Estimation.Core.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Estimation.Core.Tests.Resources;

public class TeamServiceTests
{
    private readonly InMemoryDatabase _db = new();

    [Fact]
    public async Task A_team_counts_one_story_point_a_day_until_its_edit_saves_another_value()
    {
        await _db.SeedAsync(db => db.Teams.Add(new Team { Id = 1, Name = "Argon" }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new TeamService(_db, cache);

        var team = (await service.GetByIdAsync(1))!;
        Assert.Equal(1m, team.StoryPointsPerDay);

        team.StoryPointsPerDay = 2m;
        await service.UpdateAsync(team);

        Assert.Equal(2m, (await service.GetByIdAsync(1))!.StoryPointsPerDay);
    }
}
