using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.JiraIntegration.Client.JiraSync;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Estimation.Core.Train.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class StrategicObjectiveArtLinkTests
{
    private readonly InMemoryDatabase _db = new();
    private readonly StrategicObjectiveParentConverter _converter = new();

    private static Art Art(int id, string name, params ArtJiraKey[] keys) =>
        new() { Id = id, Name = name, JiraKeys = keys.ToList() };

    private static ArtJiraKey Key(string key, string? components = null) =>
        new() { JiraKey = key, Components = components };

    private async Task<int> SeedObjectiveAsync(string jiraId = "PAY-100")
    {
        var so = new StrategicObjective { JiraId = jiraId, ProjectKey = "PAY", Summary = "Grow payments" };
        await _db.SeedAsync(db => db.StrategicObjectives.Add(so));
        return so.Id;
    }

    private async Task ConvertAsync(int soId, int syncingArtId)
    {
        await using var db = _db.CreateDbContext();
        var so = await db.StrategicObjectives.SingleAsync(s => s.Id == soId);
        await _converter.ApplyAsync(
            so,
            new JiraIssueResponse { Key = so.JiraId!, IssueType = "Strategic Objective" },
            new JiraSyncConvertContext
            {
                Db = db,
                ProjectKey = "PAY",
                CapitalProjectId = syncingArtId,
                Teams = Array.Empty<Team>(),
                PisByName = new Dictionary<string, Pi>(),
                Warnings = new List<string>(),
            },
            CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private Task<List<int>> LinkedArtIdsAsync(int soId) =>
        _db.ReadAsync(db => db.CapitalProjectStrategicObjectives
            .Where(l => l.StrategicObjectiveId == soId)
            .Select(l => l.CapitalProjectId)
            .ToListAsync());

    [Fact]
    public async Task The_background_sync_links_an_objective_to_the_only_art_on_its_key()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments", Key("PAY"), Key("LOAN"))));
        var soId = await SeedObjectiveAsync();

        await ConvertAsync(soId, syncingArtId: 1);

        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync(soId));
    }

    [Fact]
    public async Task The_background_sync_links_an_objective_on_a_shared_key_to_no_art()
    {
        await _db.SeedAsync(db =>
        {
            db.CapitalProjects.Add(Art(1, "Payments", Key("PAY")));
            db.CapitalProjects.Add(Art(2, "Cards", Key("PAY", components: "Cards")));
        });
        var soId = await SeedObjectiveAsync();

        await ConvertAsync(soId, syncingArtId: 1);
        await ConvertAsync(soId, syncingArtId: 2);

        Assert.Empty(await LinkedArtIdsAsync(soId));
    }

    [Fact]
    public async Task Update_filtered_from_jira_updates_objectives_without_linking_them_to_an_art()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments", Key("PAY"))));
        var soId = await SeedObjectiveAsync();
        var service = new StrategicObjectiveService(_db);
        var item = new JiraIssueResponse
        {
            Key = "PAY-100",
            Summary = "Grow payments faster",
            IssueType = "Strategic Objective",
            Components = ["Cards"]
        }.ToStrategicObjectiveSyncItem();

        var result = await service.SyncFromJiraAsync(0, string.Empty, [item]);

        var so = await _db.ReadAsync(db => db.StrategicObjectives.SingleAsync(s => s.Id == soId));
        Assert.Equal(new JiraSyncResult(0, 1, 0), result);
        Assert.Equal("Grow payments faster", so.Summary);
        Assert.Equal("Cards", so.Components);
        Assert.Empty(await LinkedArtIdsAsync(soId));
    }

    [Fact]
    public async Task Importing_an_objective_for_an_art_links_it_to_that_art()
    {
        await _db.SeedAsync(db => db.CapitalProjects.Add(Art(1, "Payments", Key("PAY"))));
        var service = new StrategicObjectiveService(_db);
        var item = new JiraIssueResponse { Key = "PAY-200", Summary = "New", IssueType = "Strategic Objective" }
            .ToStrategicObjectiveSyncItem();

        var result = await service.SyncFromJiraAsync(1, "PAY", [item]);

        var soId = await _db.ReadAsync(db => db.StrategicObjectives.Where(s => s.JiraId == "PAY-200").Select(s => s.Id).SingleAsync());
        Assert.Equal(new JiraSyncResult(1, 0, 1), result);
        Assert.Equal(new[] { 1 }, await LinkedArtIdsAsync(soId));
    }
}
