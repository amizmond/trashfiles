using Estimation.Core.Capacity.Models;
using Estimation.Core.Capacity.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class TeamCapacityPrioritizationTests
{
    private const int PiId = 1;
    private const int TeamId = 1;
    private const int OtherTeamId = 2;
    private const int CorePaymentsId = 1;
    private const int DataPlatformId = 2;

    private readonly InMemoryDatabase _db = new();
    private readonly TeamCapacityService _service;

    public TeamCapacityPrioritizationTests()
    {
        _service = new TeamCapacityService(_db);
    }

    private Task SeedAsync(ArtPrioritization? corePayments, ArtPrioritization? dataPlatform = null, bool sharedWithDataPlatform = false) =>
        _db.SeedAsync(db =>
        {
            db.Pis.Add(new Pi { Id = PiId, Name = "PI 26.10" });
            db.CapitalProjects.AddRange(
                new Art { Id = CorePaymentsId, Name = "Core Payments", Prioritization = corePayments },
                new Art { Id = DataPlatformId, Name = "Data Platform", Prioritization = dataPlatform });
            db.Teams.AddRange(
                new Team { Id = TeamId, Name = "Argon", IsArtSharedTeam = sharedWithDataPlatform },
                new Team { Id = OtherTeamId, Name = "Iris" });
            db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = CorePaymentsId, TeamId = TeamId });
            db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = DataPlatformId, TeamId = OtherTeamId });
            if (sharedWithDataPlatform)
            {
                db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = DataPlatformId, TeamId = TeamId });
            }

            db.PortfolioEpics.AddRange(
                new PortfolioEpic { Id = 1, JiraId = "EPIC-1", Summary = "Instant payments" },
                new PortfolioEpic { Id = 2, JiraId = "EPIC-2", Summary = "Card tokenisation" },
                new PortfolioEpic { Id = 3, JiraId = "EPIC-3", Summary = "Observability" });
            db.BusinessOutcomes.AddRange(
                new BusinessOutcome { Id = 1, JiraId = "BO-1", Summary = "Payment rails", PortfolioEpicId = 1 },
                new BusinessOutcome { Id = 2, JiraId = "BO-2", Summary = "Token vault", PortfolioEpicId = 2 },
                new BusinessOutcome { Id = 3, JiraId = "BO-3", Summary = "Tracing", PortfolioEpicId = 3 });

            AddFeature(db, 1, ranking: 1, outcomeId: 1);
            AddFeature(db, 2, ranking: 2, outcomeId: 2);
            AddFeature(db, 3, ranking: 3, outcomeId: 3);
            AddFeature(db, 4, ranking: 4, outcomeId: null);
            AddFeature(db, 5, ranking: 5, outcomeId: 2);
            AddFeature(db, 6, ranking: null, outcomeId: 1);
        });

    private static void AddFeature(EstimationDbContext db, int id, int? ranking, int? outcomeId)
    {
        db.Features.Add(new Feature
        {
            Id = id,
            JiraId = $"PAY-{id}",
            Summary = $"Feature {id}",
            Ranking = ranking,
            BusinessOutcomeId = outcomeId,
            PiId = PiId,
        });
        db.FeatureTeams.Add(new FeatureTeam { FeatureId = id, TeamId = TeamId });
    }

    private Task SaveOrderAsync(params (int FeatureId, int SortOrder)[] rows) =>
        _db.SeedAsync(db =>
        {
            foreach (var (featureId, sortOrder) in rows)
            {
                db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder
                {
                    TeamId = TeamId,
                    PiId = PiId,
                    FeatureId = featureId,
                    SortOrder = sortOrder,
                    IsIncluded = true,
                });
            }
        });

    private async Task<List<int>> BoardOrderAsync() =>
        (await _service.GetAsync(TeamId, PiId)).Features.Select(f => f.FeatureId).ToList();

    private Task SaveArtBoardAsync(int artId, ArtPrioritization level, params (int? ItemId, int SortOrder, bool IsIncluded)[] rows) =>
        SaveArtRowsAsync(artId, PiId, level, rows);

    private Task SaveGeneralOrderAsync(int artId, ArtPrioritization level, params (int? ItemId, int SortOrder, bool IsIncluded)[] rows) =>
        SaveArtRowsAsync(artId, null, level, rows);

    private Task SaveArtRowsAsync(int artId, int? piId, ArtPrioritization level, (int? ItemId, int SortOrder, bool IsIncluded)[] rows) =>
        _db.SeedAsync(db =>
        {
            foreach (var (itemId, sortOrder, isIncluded) in rows)
            {
                db.ArtPrioritizationOrders.Add(new ArtPrioritizationOrder
                {
                    CapitalProjectId = artId,
                    PiId = piId,
                    Level = level,
                    PortfolioEpicId = level == ArtPrioritization.PortfolioEpic ? itemId : null,
                    BusinessOutcomeId = level == ArtPrioritization.BusinessOutcome ? itemId : null,
                    SortOrder = sortOrder,
                    IsIncluded = isIncluded,
                });
            }
        });

    [Fact]
    public async Task A_saved_art_board_orders_unsaved_features_by_its_positions()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveArtBoardAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (3, 1, true), (1, 2, true), (null, 3, true), (2, 4, true));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Equal("Core Payments", result.Prioritization.OrderArt?.ArtName);
        Assert.Equal(ArtOrderMode.PiOrder, result.Prioritization.OrderArt?.Kind);
        Assert.Equal([3, 1, 6, 4, 2, 5], result.Features.Select(f => f.FeatureId));
        Assert.Equal([1, 2, 2, 3, 4, 4], result.Features.Select(f => f.ArtBoardPosition));
        Assert.Equal([1, 2, 2, 3, 4, 4], result.Features.Select(f => f.PrioritizationRank));
        Assert.All(result.Features, f => Assert.Empty(f.BelowArtLine));
    }

    [Fact]
    public async Task A_general_order_orders_a_pi_without_its_own_order()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveGeneralOrderAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (2, 1, true), (3, 2, true), (1, 3, false));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Equal(ArtOrderMode.FollowsGeneral, result.Prioritization.OrderArt?.Kind);
        Assert.Equal([2, 5, 3, 4, 1, 6], result.Features.Select(f => f.FeatureId));
        Assert.Equal([1, 1, 2, 3, null, null], result.Features.Select(f => f.ArtBoardPosition));
        Assert.Equal(["Core Payments"], result.Features.Single(f => f.FeatureId == 1).BelowArtLine.Select(a => a.ArtName));
    }

    [Fact]
    public async Task A_pi_order_wins_over_the_general_order()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveGeneralOrderAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (2, 1, true), (1, 2, false));
        await SaveArtBoardAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (1, 1, true));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Equal(ArtOrderMode.PiOrder, result.Prioritization.OrderArt?.Kind);
        Assert.Equal([1, 6, 2, 5, 3, 4], result.Features.Select(f => f.FeatureId));
        Assert.All(result.Features, f => Assert.Empty(f.BelowArtLine));
    }

    [Fact]
    public async Task A_card_below_the_art_line_takes_its_features_out_of_capacity_but_keeps_the_team_choice()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveOrderAsync((2, 1), (5, 2));
        await SaveArtBoardAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (1, 1, true), (2, 2, false));

        var features = (await _service.GetAsync(TeamId, PiId)).Features.ToDictionary(f => f.FeatureId);

        foreach (var id in new[] { 2, 5 })
        {
            Assert.True(features[id].IsIncluded);
            Assert.False(features[id].IsIncludedInCapacity);
            Assert.Equal([new ArtLineRef(CorePaymentsId, "Core Payments")], features[id].BelowArtLine);
            Assert.Null(features[id].ArtBoardPosition);
        }

        Assert.True(features[1].IsIncludedInCapacity);
        Assert.Equal(1, features[1].ArtBoardPosition);
        Assert.True(features[3].IsIncludedInCapacity);
        Assert.Equal(2, features[3].ArtBoardPosition);
    }

    [Fact]
    public async Task A_shared_team_drops_features_below_the_line_of_its_other_art()
    {
        await SeedAsync(
            corePayments: ArtPrioritization.PortfolioEpic,
            dataPlatform: ArtPrioritization.BusinessOutcome,
            sharedWithDataPlatform: true);
        await SaveArtBoardAsync(DataPlatformId, ArtPrioritization.BusinessOutcome, (1, 1, false));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.True(result.Prioritization.IsConflict);
        var below = result.Features.Where(f => f.BelowArtLine.Count > 0).Select(f => f.FeatureId).Order().ToList();
        Assert.Equal([1, 6], below);
        Assert.Equal("Data Platform", result.Features.Single(f => f.FeatureId == 1).BelowArtLine.Single().ArtName);
    }

    [Fact]
    public async Task A_shared_team_is_below_the_general_line_of_its_other_art()
    {
        await SeedAsync(
            corePayments: ArtPrioritization.PortfolioEpic,
            dataPlatform: ArtPrioritization.BusinessOutcome,
            sharedWithDataPlatform: true);
        await SaveGeneralOrderAsync(DataPlatformId, ArtPrioritization.BusinessOutcome, (3, 1, true), (2, 2, false));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Equal([2, 5], result.Features.Where(f => f.BelowArtLine.Count > 0).Select(f => f.FeatureId).Order());
    }

    [Fact]
    public async Task Rows_saved_for_the_other_level_are_ignored()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveArtBoardAsync(CorePaymentsId, ArtPrioritization.BusinessOutcome, (2, 1, false), (1, 2, true));
        await SaveGeneralOrderAsync(CorePaymentsId, ArtPrioritization.BusinessOutcome, (3, 1, false));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Null(result.Prioritization.OrderArt);
        Assert.All(result.Features, f => Assert.Empty(f.BelowArtLine));
        Assert.Equal([1, 2, 3, 4, 5, 6], result.Features.Select(f => f.FeatureId));
    }

    [Fact]
    public async Task Find_returns_the_art_position_and_the_art_line()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveArtBoardAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (1, 1, true), (2, 2, false));

        var rows = await _service.FindFeaturesAsync(TeamId, PiId,
            new FeatureFindCriteria(null, ["PAY-1", "PAY-2"], [], false, [], false, [], false));

        var placed = rows.Single(r => r.FeatureId == 1);
        Assert.Equal(1, placed.ArtBoardPosition);
        Assert.Empty(placed.BelowArtLine);

        var below = rows.Single(r => r.FeatureId == 2);
        Assert.Null(below.ArtBoardPosition);
        Assert.Equal("Core Payments", below.BelowArtLine.Single().ArtName);
    }

    [Fact]
    public async Task Excluding_a_feature_places_it_on_another_team_by_that_team_art_board()
    {
        await SeedAsync(corePayments: null, dataPlatform: ArtPrioritization.BusinessOutcome);
        await _db.SeedAsync(db => db.FeatureTeams.Add(new FeatureTeam { FeatureId = 3, TeamId = OtherTeamId }));
        await SaveArtBoardAsync(DataPlatformId, ArtPrioritization.BusinessOutcome, (3, 1, true));

        await _service.PropagateInclusionToOtherTeamsAsync(TeamId, PiId, new Dictionary<int, bool> { [3] = false });

        var propagated = await _db.ReadAsync(db => db.TeamCapacityFeatureOrders
            .SingleAsync(o => o.TeamId == OtherTeamId && o.FeatureId == 3));
        Assert.False(propagated.IsIncluded);
        Assert.Equal(1, propagated.SortOrder);
    }

    [Fact]
    public async Task Without_a_prioritization_unsaved_features_follow_the_feature_ranking()
    {
        await SeedAsync(corePayments: null);

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Null(result.Prioritization.Mode);
        Assert.Equal([1, 2, 3, 4, 5, 6], result.Features.Select(f => f.FeatureId));
    }

    [Fact]
    public async Task A_level_without_any_art_order_follows_the_feature_ranking()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Equal(ArtPrioritization.PortfolioEpic, result.Prioritization.Mode);
        Assert.Equal(["Core Payments"], result.Prioritization.SourceArtNames);
        Assert.Null(result.Prioritization.OrderArt);
        Assert.Equal([1, 2, 3, 4, 5, 6], result.Features.Select(f => f.FeatureId));
        Assert.All(result.Features, f => Assert.Null(f.PrioritizationRank));
        Assert.All(result.Features, f => Assert.Null(f.ArtBoardPosition));
        Assert.All(result.Features, f => Assert.False(f.IsSaved));
    }

    [Fact]
    public async Task Rows_carry_the_feature_ranking_for_the_reset()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveGeneralOrderAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (2, 1, true), (1, 2, true));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Equal(
            result.Features.Select(f => f.FeatureId),
            result.Prioritization.DefaultOrder(result.Features.AsEnumerable().Reverse()).Select(f => f.FeatureId));
        Assert.Equal(5, result.Features.Single(f => f.FeatureId == 5).FeatureRanking);
    }

    [Fact]
    public async Task A_saved_order_is_kept_whatever_the_prioritization()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveOrderAsync((6, 1), (5, 2), (4, 3), (3, 4), (2, 5), (1, 6));
        await SaveArtBoardAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (2, 1, true), (1, 2, true));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Equal([6, 5, 4, 3, 2, 1], result.Features.Select(f => f.FeatureId));
        Assert.All(result.Features, f => Assert.True(f.IsSaved));
        Assert.Equal(1, result.Features.Single(f => f.FeatureId == 2).PrioritizationRank);
    }

    [Fact]
    public async Task An_unsaved_feature_joins_a_saved_board_by_its_rank_after_the_saved_feature_at_that_position()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveArtBoardAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (2, 1, true), (1, 2, true), (3, 3, true), (null, 4, true));
        await SaveOrderAsync((4, 1), (3, 2));

        Assert.Equal([4, 2, 5, 3, 1, 6], await BoardOrderAsync());
    }

    [Fact]
    public async Task A_shared_team_follows_the_only_art_that_sets_a_prioritization()
    {
        await SeedAsync(corePayments: null, dataPlatform: ArtPrioritization.BusinessOutcome, sharedWithDataPlatform: true);
        await SaveGeneralOrderAsync(DataPlatformId, ArtPrioritization.BusinessOutcome, (2, 1, true), (3, 2, true), (1, 3, true));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Equal(ArtPrioritization.BusinessOutcome, result.Prioritization.Mode);
        Assert.Equal(["Data Platform"], result.Prioritization.SourceArtNames);
        Assert.Equal([2, 5, 3, 1, 6, 4], result.Features.Select(f => f.FeatureId));
    }

    [Fact]
    public async Task A_shared_team_whose_arts_disagree_falls_back_to_feature_ranking()
    {
        await SeedAsync(
            corePayments: ArtPrioritization.PortfolioEpic,
            dataPlatform: ArtPrioritization.BusinessOutcome,
            sharedWithDataPlatform: true);
        await SaveGeneralOrderAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (3, 1, true));

        var result = await _service.GetAsync(TeamId, PiId);

        Assert.Null(result.Prioritization.Mode);
        Assert.True(result.Prioritization.IsConflict);
        Assert.Equal(2, result.Prioritization.Arts.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], result.Features.Select(f => f.FeatureId));
        Assert.All(result.Features, f => Assert.Null(f.PrioritizationRank));
    }

    [Fact]
    public async Task Find_returns_the_rank_and_the_parents_of_each_feature()
    {
        await SeedAsync(corePayments: ArtPrioritization.PortfolioEpic);
        await SaveGeneralOrderAsync(CorePaymentsId, ArtPrioritization.PortfolioEpic, (2, 1, true), (1, 2, true));

        var rows = await _service.FindFeaturesAsync(TeamId, PiId,
            new FeatureFindCriteria(null, ["PAY-1", "PAY-4"], [], false, [], false, [], false));

        var withEpic = rows.Single(r => r.FeatureId == 1);
        Assert.Equal(2, withEpic.PrioritizationRank);
        Assert.Equal(1, withEpic.FeatureRanking);
        Assert.Equal("BO-1", withEpic.BusinessOutcomeJiraId);
        Assert.Equal("Payment rails", withEpic.BusinessOutcomeSummary);
        Assert.Equal("EPIC-1", withEpic.PortfolioEpicJiraId);
        Assert.Equal("Instant payments", withEpic.PortfolioEpicSummary);

        var withoutOutcome = rows.Single(r => r.FeatureId == 4);
        Assert.Equal(4, withoutOutcome.PrioritizationRank);
        Assert.Null(withoutOutcome.BusinessOutcomeJiraId);
    }

    [Fact]
    public async Task Excluding_a_feature_on_a_team_whose_art_has_no_order_uses_the_feature_ranking()
    {
        await SeedAsync(corePayments: null, dataPlatform: ArtPrioritization.BusinessOutcome);
        await _db.SeedAsync(db => db.FeatureTeams.Add(new FeatureTeam { FeatureId = 3, TeamId = OtherTeamId }));

        await _service.PropagateInclusionToOtherTeamsAsync(TeamId, PiId, new Dictionary<int, bool> { [3] = false });

        var propagated = await _db.ReadAsync(db => db.TeamCapacityFeatureOrders
            .SingleAsync(o => o.TeamId == OtherTeamId && o.FeatureId == 3));
        Assert.False(propagated.IsIncluded);
        Assert.Equal(3, propagated.SortOrder);
    }

    [Fact]
    public async Task Excluding_a_feature_on_a_team_without_prioritization_still_uses_the_feature_ranking()
    {
        await SeedAsync(corePayments: null, dataPlatform: null);
        await _db.SeedAsync(db => db.FeatureTeams.Add(new FeatureTeam { FeatureId = 3, TeamId = OtherTeamId }));

        await _service.PropagateInclusionToOtherTeamsAsync(TeamId, PiId, new Dictionary<int, bool> { [3] = false });

        var propagated = await _db.ReadAsync(db => db.TeamCapacityFeatureOrders
            .SingleAsync(o => o.TeamId == OtherTeamId && o.FeatureId == 3));
        Assert.Equal(3, propagated.SortOrder);
    }
}
