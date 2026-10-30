using Estimation.Core.Administration.Models;
using Estimation.Core.Administration.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Administration;

[Collection(LocalDbCollection.Name)]
public class HousekeepingServiceSqlServerTests : IAsyncLifetime
{
    private const string Prefix = "ZQXHOUSE-";
    private const string ArtName = "ZQXHOUSE ART";

    private readonly LocalDbFixture _localDb;

    public HousekeepingServiceSqlServerTests(LocalDbFixture localDb) => _localDb = localDb;

    public async Task InitializeAsync()
    {
        if (LocalDbAvailability.UnavailableReason is not null)
        {
            return;
        }

        await DeleteOwnRowsAsync();
    }

    public async Task DisposeAsync()
    {
        if (LocalDbAvailability.UnavailableReason is not null)
        {
            return;
        }

        await DeleteOwnRowsAsync();
    }

    [LocalDbFact]
    public async Task A_copy_stored_in_another_case_or_with_spaces_counts_as_a_copy()
    {
        await using (var db = await _localDb.Factory.CreateDbContextAsync())
        {
            db.Features.AddRange(
                new Feature { JiraId = Prefix + "1", Summary = "Still a feature" },
                new Feature { JiraId = Prefix + "3", Summary = "Story" },
                new Feature { JiraId = Prefix + "4", Summary = "Outcome" });
            db.BusinessOutcomes.Add(new BusinessOutcome { JiraId = Prefix.ToLowerInvariant() + "4 ", Summary = "Copy" });
            await db.SaveChangesAsync();
        }
        var jira = new HousekeepingServiceTests.FakeJira();
        jira.Add(Prefix + "1", "Feature", "Open");
        jira.Add(Prefix + "3", "Story", "Open");
        jira.Add(Prefix + "4", "Business Outcome", "Open");

        var result = await new HousekeepingService(_localDb.Factory, jira, TimeSpan.Zero)
            .CheckIssueTypesAsync(HousekeepingTable.Features, "admin");
        var items = result.Items.Where(i => i.JiraKey.StartsWith(Prefix)).ToList();

        Assert.Equal([Prefix + "3", Prefix + "4"], items.Select(i => i.JiraKey));
        Assert.False(items[0].ExistsInIssueTypeTable);
        Assert.True(items[1].ExistsInIssueTypeTable);
    }

    [LocalDbFact]
    public async Task Removing_issues_of_every_table_keeps_their_children_and_unlinks_them()
    {
        int wrongObjective, rightObjective, rightEpic, wrongEpic, outcomeUnderWrongEpic, wrongOutcome, featureUnderWrongOutcome, story;
        await using (var db = await _localDb.Factory.CreateDbContextAsync())
        {
            var art = new Art { Name = ArtName };
            var so1 = new StrategicObjective { JiraId = Prefix + "SO1", Summary = "Wrong", IssueType = "Portfolio Epic" };
            var so2 = new StrategicObjective { JiraId = Prefix + "SO2", Summary = "Right", IssueType = "Strategic Objective" };
            var pe1 = new PortfolioEpic { JiraId = Prefix + "PE1", Summary = "Right", IssueType = "Portfolio Epic" };
            var pe2 = new PortfolioEpic { JiraId = Prefix + "PE2", Summary = "Wrong", IssueType = "Business Outcome" };
            var bo1 = new BusinessOutcome { JiraId = Prefix + "BO1", Summary = "Right", IssueType = "Business Outcome", PortfolioEpic = pe2 };
            var bo2 = new BusinessOutcome { JiraId = Prefix + "BO2", Summary = "Wrong", IssueType = "Feature", PortfolioEpic = pe1 };
            var f1 = new Feature { JiraId = Prefix + "F1", Summary = "Right", IssueType = "Feature", BusinessOutcome = bo2 };
            var f2 = new Feature { JiraId = Prefix + "F2", Summary = "Story", IssueType = "Story", BusinessOutcome = bo1 };
            db.AddRange(art, so1, so2, pe1, pe2, bo1, bo2, f1, f2);
            db.CapitalProjectStrategicObjectives.AddRange(
                new ArtStrategicObjective { Art = art, StrategicObjective = so1 },
                new ArtStrategicObjective { Art = art, StrategicObjective = so2 });
            db.StrategicObjectivePortfolioEpics.AddRange(
                new StrategicObjectivePortfolioEpic { StrategicObjective = so1, PortfolioEpic = pe1 },
                new StrategicObjectivePortfolioEpic { StrategicObjective = so2, PortfolioEpic = pe2 },
                new StrategicObjectivePortfolioEpic { StrategicObjective = so2, PortfolioEpic = pe1 });
            db.FeatureComments.Add(new FeatureComment { Feature = f2, Text = "Comment", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();

            (wrongObjective, rightObjective, rightEpic, wrongEpic) = (so1.Id, so2.Id, pe1.Id, pe2.Id);
            (outcomeUnderWrongEpic, wrongOutcome, featureUnderWrongOutcome, story) = (bo1.Id, bo2.Id, f1.Id, f2.Id);
        }

        var result = await new HousekeepingService(_localDb.Factory, new HousekeepingServiceTests.FakeJira(), TimeSpan.Zero).RemoveAsync(
        [
            new HousekeepingItemKey(HousekeepingTable.StrategicObjectives, wrongObjective, Prefix + "SO1"),
            new HousekeepingItemKey(HousekeepingTable.PortfolioEpics, wrongEpic, Prefix + "PE2"),
            new HousekeepingItemKey(HousekeepingTable.BusinessOutcomes, wrongOutcome, Prefix + "BO2"),
            new HousekeepingItemKey(HousekeepingTable.Features, story, Prefix + "F2"),
        ]);

        Assert.Equal(new HousekeepingRemoveResult(4, 0), result);

        await using var read = await _localDb.Factory.CreateDbContextAsync();
        Assert.Equal([rightObjective], await read.StrategicObjectives.Where(x => x.JiraId!.StartsWith(Prefix)).Select(x => x.Id).ToListAsync());
        Assert.Equal([rightEpic], await read.PortfolioEpics.Where(x => x.JiraId!.StartsWith(Prefix)).Select(x => x.Id).ToListAsync());
        var outcome = Assert.Single(await read.BusinessOutcomes.Where(x => x.JiraId!.StartsWith(Prefix)).ToListAsync());
        Assert.Equal(outcomeUnderWrongEpic, outcome.Id);
        Assert.Null(outcome.PortfolioEpicId);
        var feature = Assert.Single(await read.Features.Where(x => x.JiraId!.StartsWith(Prefix)).ToListAsync());
        Assert.Equal(featureUnderWrongOutcome, feature.Id);
        Assert.Null(feature.BusinessOutcomeId);
        Assert.False(await read.FeatureComments.AnyAsync(c => c.FeatureId == story));
        Assert.Equal([rightObjective], await read.CapitalProjectStrategicObjectives
            .Where(l => l.Art.Name == ArtName)
            .Select(l => l.StrategicObjectiveId)
            .ToListAsync());
        var link = Assert.Single(await read.StrategicObjectivePortfolioEpics
            .Where(l => l.StrategicObjective.JiraId!.StartsWith(Prefix) || l.PortfolioEpic.JiraId!.StartsWith(Prefix))
            .ToListAsync());
        Assert.Equal((rightObjective, rightEpic), (link.StrategicObjectiveId, link.PortfolioEpicId));
    }

    private async Task DeleteOwnRowsAsync()
    {
        await using var db = await _localDb.Factory.CreateDbContextAsync();
        db.StrategicObjectivePortfolioEpics.RemoveRange(await db.StrategicObjectivePortfolioEpics
            .Where(l => l.PortfolioEpic.JiraId!.Contains(Prefix) || l.StrategicObjective.JiraId!.Contains(Prefix))
            .ToListAsync());
        db.CapitalProjectStrategicObjectives.RemoveRange(await db.CapitalProjectStrategicObjectives
            .Where(l => l.Art.Name == ArtName || l.StrategicObjective.JiraId!.Contains(Prefix))
            .ToListAsync());
        db.Features.RemoveRange(await db.Features.Where(x => x.JiraId != null && x.JiraId.Contains(Prefix)).ToListAsync());
        db.BusinessOutcomes.RemoveRange(await db.BusinessOutcomes.Where(x => x.JiraId != null && x.JiraId.Contains(Prefix)).ToListAsync());
        db.PortfolioEpics.RemoveRange(await db.PortfolioEpics.Where(x => x.JiraId != null && x.JiraId.Contains(Prefix)).ToListAsync());
        db.StrategicObjectives.RemoveRange(await db.StrategicObjectives.Where(x => x.JiraId != null && x.JiraId.Contains(Prefix)).ToListAsync());
        db.CapitalProjects.RemoveRange(await db.CapitalProjects.Where(a => a.Name == ArtName).ToListAsync());
        await db.SaveChangesAsync();
    }
}
