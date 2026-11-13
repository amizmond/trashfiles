using Estimation.HrReporting.Models;
using Estimation.HrReporting.Services;
using Estimation.HrReporting.Tests.Infrastructure;
using Xunit;

namespace Estimation.HrReporting.Tests;

public sealed class TeamsAnalysisExclusionServiceTests : IDisposable
{
    private readonly HrReportingDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private TeamsAnalysisExclusionService Service() => new(_db, _db.Clock);

    [Fact]
    public async Task Adds_a_trimmed_entry_once_whatever_the_casing()
    {
        var service = Service();

        var first = await service.AddAsync(" RFT - Capital ", TeamsAnalysisExclusionKind.Train, " ART-CCR-Reporting&BA ", @"CORP\hr");
        var again = await service.AddAsync("rft - capital", TeamsAnalysisExclusionKind.Train, "art-ccr-reporting&ba", @"CORP\other");

        Assert.Equal(first.Id, again.Id);
        var stored = Assert.Single(await service.GetAllAsync());
        Assert.Equal(("RFT - Capital", "ART-CCR-Reporting&BA", @"CORP\hr"), (stored.Domain, stored.Value, stored.CreatedBy));
        Assert.Equal(_db.Clock.Now.UtcDateTime, stored.CreatedAt);
    }

    [Fact]
    public async Task The_same_name_can_be_hidden_as_a_train_and_as_a_team()
    {
        var service = Service();

        await service.AddAsync("RFT - Market Risk", TeamsAnalysisExclusionKind.Team, "Solution Train", null);
        await service.AddAsync("RFT - Market Risk", TeamsAnalysisExclusionKind.Train, "Solution Train", null);
        await service.AddAsync("RFT - Capital", TeamsAnalysisExclusionKind.Team, "", null);

        var all = await service.GetAllAsync();

        Assert.Equal(
            [("RFT - Capital", TeamsAnalysisExclusionKind.Team, ""),
             ("RFT - Market Risk", TeamsAnalysisExclusionKind.Train, "Solution Train"),
             ("RFT - Market Risk", TeamsAnalysisExclusionKind.Team, "Solution Train")],
            all.Select(e => (e.Domain, e.Kind, e.Value)));
    }

    [Fact]
    public async Task Removes_an_entry()
    {
        var service = Service();
        var entry = await service.AddAsync("RFT - Capital", TeamsAnalysisExclusionKind.Team, "CCR-LPM", null);

        Assert.True(await service.RemoveAsync(entry.Id));
        Assert.False(await service.RemoveAsync(entry.Id));
        Assert.Empty(await service.GetAllAsync());
    }
}
