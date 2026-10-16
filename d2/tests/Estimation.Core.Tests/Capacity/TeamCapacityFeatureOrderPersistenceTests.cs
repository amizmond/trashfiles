using Estimation.Core.Capacity.Models;
using Estimation.Core.Capacity.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Administration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

[Collection(LocalDbCollection.Name)]
public class TeamCapacityFeatureOrderPersistenceTests : IAsyncLifetime
{
    private readonly LocalDbFixture _localDb;

    private int _sourceTeamId;
    private int _otherTeamId;
    private int _piId;
    private int _featureId;

    public TeamCapacityFeatureOrderPersistenceTests(LocalDbFixture localDb) => _localDb = localDb;

    public async Task InitializeAsync()
    {
        if (LocalDbAvailability.UnavailableReason is not null)
        {
            return;
        }

        await using var db = await _localDb.Factory.CreateDbContextAsync();

        var sourceTeam = new Team { Name = "Inclusion source" };
        var otherTeam = new Team { Name = "Inclusion target" };
        var pi = new Pi { Name = "Inclusion PI" };
        db.Teams.AddRange(sourceTeam, otherTeam);
        db.Pis.Add(pi);
        await db.SaveChangesAsync();

        var feature = new Feature { Summary = "Feature on two teams", PiId = pi.Id };
        db.Features.Add(feature);
        await db.SaveChangesAsync();

        db.FeatureTeams.AddRange(
            new FeatureTeam { FeatureId = feature.Id, TeamId = sourceTeam.Id },
            new FeatureTeam { FeatureId = feature.Id, TeamId = otherTeam.Id });
        await db.SaveChangesAsync();

        _sourceTeamId = sourceTeam.Id;
        _otherTeamId = otherTeam.Id;
        _piId = pi.Id;
        _featureId = feature.Id;
    }

    public async Task DisposeAsync()
    {
        if (LocalDbAvailability.UnavailableReason is not null || _piId == 0)
        {
            return;
        }

        await using var db = await _localDb.Factory.CreateDbContextAsync();
        db.TeamCapacityFeatureOrders.RemoveRange(
            await db.TeamCapacityFeatureOrders.Where(o => o.PiId == _piId).ToListAsync());
        db.FeatureTeams.RemoveRange(
            await db.FeatureTeams.Where(ft => ft.FeatureId == _featureId).ToListAsync());
        await db.SaveChangesAsync();

        db.Features.RemoveRange(await db.Features.Where(f => f.Id == _featureId).ToListAsync());
        db.Teams.RemoveRange(
            await db.Teams.Where(t => t.Id == _sourceTeamId || t.Id == _otherTeamId).ToListAsync());
        db.Pis.RemoveRange(await db.Pis.Where(p => p.Id == _piId).ToListAsync());
        await db.SaveChangesAsync();
    }

    [LocalDbFact]
    public async Task A_feature_order_row_saves_as_excluded()
    {
        await using (var db = await _localDb.Factory.CreateDbContextAsync())
        {
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder
            {
                TeamId = _otherTeamId,
                PiId = _piId,
                FeatureId = _featureId,
                SortOrder = 1,
                IsIncluded = false,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await _localDb.Factory.CreateDbContextAsync();
        var saved = await read.TeamCapacityFeatureOrders
            .AsNoTracking()
            .SingleAsync(o => o.PiId == _piId && o.TeamId == _otherTeamId);

        Assert.False(saved.IsIncluded);
    }

    [LocalDbFact]
    public async Task Excluding_a_feature_propagates_the_exclusion_to_a_team_with_no_row_yet()
    {
        var service = new TeamCapacityService(_localDb.Factory);

        await service.PropagateInclusionToOtherTeamsAsync(
            _sourceTeamId, _piId, new Dictionary<int, bool> { [_featureId] = false });

        await using var db = await _localDb.Factory.CreateDbContextAsync();
        var propagated = await db.TeamCapacityFeatureOrders
            .AsNoTracking()
            .SingleAsync(o => o.PiId == _piId && o.TeamId == _otherTeamId && o.FeatureId == _featureId);

        Assert.False(propagated.IsIncluded);
    }

    [LocalDbFact]
    public async Task An_included_feature_order_row_still_saves_as_included()
    {
        await using (var db = await _localDb.Factory.CreateDbContextAsync())
        {
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder
            {
                TeamId = _otherTeamId,
                PiId = _piId,
                FeatureId = _featureId,
                SortOrder = 1,
                IsIncluded = true,
            });
            await db.SaveChangesAsync();
        }

        await using var read = await _localDb.Factory.CreateDbContextAsync();
        var saved = await read.TeamCapacityFeatureOrders
            .AsNoTracking()
            .SingleAsync(o => o.PiId == _piId && o.TeamId == _otherTeamId);

        Assert.True(saved.IsIncluded);
    }
}
