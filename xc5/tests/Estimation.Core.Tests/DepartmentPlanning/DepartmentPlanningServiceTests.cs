using Estimation.Core.Capacity.Models;
using Estimation.Core.Capacity.Services;
using Estimation.Core.DepartmentPlanning.Services;
using Estimation.Core.Features.DependencyMap.Services;
using Estimation.Core.Features.Hygiene.Models;
using Estimation.Core.Features.Hygiene.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;
using Deps = Estimation.Core.Tests.Features.DependencyMap.DependencyTestData;

namespace Estimation.Core.Tests.DepartmentPlanning;

public class DepartmentPlanningServiceTests
{
    private const int PiId = 3;
    private const int Retail = 1;
    private const int CorePayments = 1;
    private const int DataPlatform = 2;
    private const int Argon = 1;
    private const int Boron = 2;
    private const int Zeta = 3;

    private readonly InMemoryDatabase _db = new();
    private readonly FakeHygiene _hygiene = new();
    private readonly FakeDependencyMap _dependencyMap = new();
    private readonly DepartmentPlanningService _service;

    public DepartmentPlanningServiceTests()
    {
        _service = new DepartmentPlanningService(_db, new TeamCapacityService(_db), _dependencyMap, _hygiene);
    }

    private Task SeedAsync() => _db.SeedAsync(db =>
    {
        db.Pis.Add(new Pi { Id = PiId, Name = "PI 26.09", StartDate = new DateTime(2026, 9, 14), EndDate = new DateTime(2026, 12, 6) });
        db.Pis.Add(new Pi { Id = 4, Name = "PI 26.12", StartDate = new DateTime(2026, 12, 7), EndDate = new DateTime(2027, 2, 28) });
        db.Departments.Add(new Department { Id = Retail, Name = "Retail" });
        db.CapitalProjects.AddRange(
            new Art { Id = CorePayments, Name = "Core Payments", DepartmentId = Retail, Prioritization = ArtPrioritization.PortfolioEpic },
            new Art { Id = DataPlatform, Name = "Data Platform", DepartmentId = Retail, Prioritization = ArtPrioritization.BusinessOutcome },
            new Art { Id = 3, Name = "Elsewhere", DepartmentId = 2 });
        db.Departments.Add(new Department { Id = 2, Name = "Other" });
        db.Teams.AddRange(
            new Team { Id = Argon, Name = "Argon" },
            new Team { Id = Boron, Name = "Boron", StoryPointsPerDay = 2m },
            new Team { Id = Zeta, Name = "Zeta" },
            new Team { Id = 4, Name = "Omega" });
        db.CapitalProjectTeams.AddRange(
            new ArtTeam { CapitalProjectId = CorePayments, TeamId = Argon },
            new ArtTeam { CapitalProjectId = CorePayments, TeamId = Boron },
            new ArtTeam { CapitalProjectId = DataPlatform, TeamId = Zeta },
            new ArtTeam { CapitalProjectId = 3, TeamId = 4 });

        db.StrategicObjectives.AddRange(
            new StrategicObjective { Id = 1, JiraId = "STRAT-1", Summary = "Grow payments" },
            new StrategicObjective { Id = 2, JiraId = "STRAT-2", Summary = "Unrelated objective" });
        db.CapitalProjectStrategicObjectives.Add(new ArtStrategicObjective { CapitalProjectId = CorePayments, StrategicObjectiveId = 1 });
        db.PortfolioEpics.AddRange(
            new PortfolioEpic { Id = 10, JiraId = "EPIC-10", Summary = "Instant payments" },
            new PortfolioEpic { Id = 11, JiraId = "EPIC-11", Summary = "Tokenisation" },
            new PortfolioEpic { Id = 12, JiraId = "EPIC-12", Summary = "Orphan epic" });
        db.StrategicObjectivePortfolioEpics.AddRange(
            new StrategicObjectivePortfolioEpic { StrategicObjectiveId = 1, PortfolioEpicId = 10 },
            new StrategicObjectivePortfolioEpic { StrategicObjectiveId = 1, PortfolioEpicId = 11 },
            new StrategicObjectivePortfolioEpic { StrategicObjectiveId = 2, PortfolioEpicId = 12 });
        db.BusinessOutcomes.AddRange(
            new BusinessOutcome { Id = 100, JiraId = "BO-100", Summary = "Rails", PortfolioEpicId = 10, RagStatus = "Amber - vendor" },
            new BusinessOutcome { Id = 101, JiraId = "BO-101", Summary = "Vault", PortfolioEpicId = 11 },
            new BusinessOutcome { Id = 102, JiraId = "BO-102", Summary = "Ingestion", PortfolioEpicId = 12 },
            new BusinessOutcome { Id = 103, JiraId = "BO-103", Summary = "Empty outcome", PortfolioEpicId = 10 });

        AddFeature(db, 1, 100, PiId, "In Progress", (Argon, 30), (Boron, 20));
        AddFeature(db, 2, 101, PiId, "To Do", (Argon, 40));
        AddFeature(db, 3, null, PiId, "Blocked", (Boron, 25));
        AddFeature(db, 4, 102, PiId, "Funnel", (Zeta, 30));
        AddFeature(db, 5, 100, 4, "To Do", (Argon, 99));
        AddFeature(db, 6, 100, PiId, "To Do", (4, 70));

        db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder { TeamId = Boron, PiId = PiId, FeatureId = 3, SortOrder = 1, IsIncluded = false });
        db.ArtPrioritizationOrders.AddRange(
            new ArtPrioritizationOrder { CapitalProjectId = CorePayments, Level = ArtPrioritization.PortfolioEpic, PortfolioEpicId = 10, SortOrder = 1, IsIncluded = true },
            new ArtPrioritizationOrder { CapitalProjectId = CorePayments, Level = ArtPrioritization.PortfolioEpic, PortfolioEpicId = 11, SortOrder = 2, IsIncluded = false });
    });

    private static void AddFeature(EstimationDbContext db, int id, int? outcomeId, int piId, string status, params (int TeamId, int StoryPoints)[] teams)
    {
        db.Features.Add(new Feature
        {
            Id = id,
            JiraId = $"PAY-{id}",
            Name = $"Feature {id}",
            Summary = $"Feature {id}",
            Status = status,
            Ranking = id,
            BusinessOutcomeId = outcomeId,
            PiId = piId,
        });
        foreach (var (teamId, storyPoints) in teams)
        {
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = id, TeamId = teamId, StoryPoints = storyPoints });
        }
    }

    [Fact]
    public async Task An_unknown_pi_gives_null()
    {
        await SeedAsync();

        Assert.Null(await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments], 99)));
    }

    [Fact]
    public async Task The_department_scope_loads_its_arts_teams_and_the_business_epics_on_their_boards()
    {
        await SeedAsync();

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        Assert.NotNull(data);
        Assert.Equal("PI 26.09", data.PiName);
        Assert.Equal(["Core Payments", "Data Platform"], data.Arts.Select(a => a.Name));
        Assert.Equal(["CP", "DP"], data.Arts.Select(a => a.Abbreviation));
        Assert.Equal(["Argon", "Boron", "Zeta"], data.Teams.Select(t => t.Name));
        Assert.Equal(2m, data.Teams.Single(t => t.Name == "Boron").StoryPointsPerDay);
        Assert.Equal([1, 2, 3, 4], data.Features.Select(f => f.Id));
    }

    [Fact]
    public async Task Team_links_carry_demand_inclusion_and_the_art_line()
    {
        await SeedAsync();
        await SeedMembersAsync(Argon, Boron);

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var first = data!.Features.Single(f => f.Id == 1);
        Assert.Equal([(Argon, 30m, true), (Boron, 20m, true)], first.Teams.Select(t => (t.TeamId, t.DemandSp, t.IsPlanned)).OrderBy(t => t.TeamId));
        Assert.Equal(50m, first.PlannedSp);
        Assert.All(first.Teams, t => Assert.True(t.HasTeamEstimate));

        var vault = data.Features.Single(f => f.Id == 2);
        var link = vault.Teams.Single();
        Assert.True(link.IsIncluded);
        Assert.Equal(["Core Payments"], link.BelowArtLine.Select(a => a.ArtName));
        Assert.False(link.IsPlanned);
        Assert.Equal(40m, vault.UnplannedSp);

        var blocked = data.Features.Single(f => f.Id == 3);
        Assert.False(blocked.Teams.Single().IsIncluded);
        Assert.Equal(25m, blocked.UnplannedSp);
        Assert.Equal(1, blocked.Teams.Single().BoardPosition);
    }

    [Fact]
    public async Task A_feature_s_story_points_count_once_when_its_teams_have_none_of_their_own()
    {
        await SeedAsync();
        await SeedMembersAsync(Argon, Boron);
        await _db.SeedAsync(db => AddUnestimatedFeature(db, 20, 21, Argon, Boron));

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var shared = data!.Features.Single(f => f.Id == 20);
        Assert.Equal(21, shared.StoryPoints);
        Assert.Equal(21m, shared.DemandSp);
        Assert.Equal(21m, shared.PlannedSp);
        Assert.Equal(
            [(Argon, 10.5m, false), (Boron, 10.5m, false)],
            shared.Teams.Select(t => (t.TeamId, t.DemandSp, t.HasTeamEstimate)).OrderBy(t => t.TeamId));

        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions());
        var row = tree.FlattenAll().Single(r => r.Feature?.Id == 20);
        Assert.Equal((21m, 21m, 0m), (row.Metrics.DemandSp, row.Metrics.PlannedSp, row.Metrics.UnplannedSp));
        Assert.Equal([(Argon, 10.5m), (Boron, 10.5m)], row.TeamCells.OrderBy(c => c.Key).Select(c => (c.Key, c.Value.DemandSp)));
        Assert.Equal(166m, tree.Total.DemandSp);
    }

    [Fact]
    public async Task A_team_without_its_own_story_points_counts_what_the_other_teams_estimates_leave()
    {
        await SeedAsync();
        await _db.SeedAsync(db =>
        {
            AddUnestimatedFeature(db, 20, 21, Argon);
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = 20, TeamId = Boron, StoryPoints = 8 });
        });

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var mixed = data!.Features.Single(f => f.Id == 20);
        Assert.Equal(
            [(Argon, 13m, false), (Boron, 8m, true)],
            mixed.Teams.Select(t => (t.TeamId, t.DemandSp, t.HasTeamEstimate)).OrderBy(t => t.TeamId));
        Assert.Equal(21m, mixed.DemandSp);
    }

    [Fact]
    public async Task A_team_outside_the_scope_keeps_its_part_of_a_shared_feature()
    {
        await SeedAsync();
        await _db.SeedAsync(db => AddUnestimatedFeature(db, 20, 21, Argon, 4));

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var shared = data!.Features.Single(f => f.Id == 20);
        Assert.Equal((Argon, 10.5m), shared.Teams.Select(t => (t.TeamId, t.DemandSp)).Single());
        Assert.Equal(10.5m, shared.DemandSp);
    }

    [Fact]
    public async Task Only_the_team_s_planned_share_counts_as_planned()
    {
        await SeedAsync();
        await SeedMembersAsync(Argon, Boron);
        await _db.SeedAsync(db =>
        {
            AddUnestimatedFeature(db, 20, 21, Argon, Boron);
            db.TeamCapacityFeatureOrders.Add(new TeamCapacityFeatureOrder { TeamId = Boron, PiId = PiId, FeatureId = 20, SortOrder = 2, IsIncluded = false });
        });

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var shared = data!.Features.Single(f => f.Id == 20);
        Assert.Equal((21m, 10.5m, 10.5m), (shared.DemandSp, shared.PlannedSp, shared.UnplannedSp));
    }

    [Fact]
    public async Task A_team_without_members_plans_nothing()
    {
        await SeedAsync();
        await SeedMembersAsync(Argon);

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        Assert.Equal((1, 0), (data!.Teams.Single(t => t.Id == Argon).MemberCount, data.Teams.Single(t => t.Id == Boron).MemberCount));
        Assert.Equal(0m, data.Teams.Single(t => t.Id == Boron).CapacitySp);
        var shared = data.Features.Single(f => f.Id == 1);
        Assert.Equal([(Argon, true), (Boron, false)], shared.Teams.Select(t => (t.TeamId, t.IsPlanned)).OrderBy(t => t.TeamId));
        Assert.Equal((50m, 30m, 20m), (shared.DemandSp, shared.PlannedSp, shared.UnplannedSp));
    }

    [Fact]
    public async Task A_team_without_working_days_plans_nothing_until_the_ip_sprint_counts()
    {
        await SeedAsync();
        await SeedMembersAsync(Zeta);
        await _db.SeedAsync(db => db.Sprints.Add(
            new Sprint { Id = 9, TeamId = Zeta, PiId = PiId, Name = "IP", StartDate = new DateTime(2026, 11, 30), EndDate = new DateTime(2026, 12, 4), IsIpSprint = true }));

        var without = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));
        var with = await _service.ReloadCapacityAsync(without!, true);

        Assert.Equal(0m, without!.Teams.Single(t => t.Id == Zeta).CapacitySp);
        Assert.Equal((0m, 30m), (without.Features.Single(f => f.Id == 4).PlannedSp, without.Features.Single(f => f.Id == 4).UnplannedSp));
        Assert.True(with!.Teams.Single(t => t.Id == Zeta).CapacitySp > 0m);
        Assert.Equal((30m, 0m), (with.Features.Single(f => f.Id == 4).PlannedSp, with.Features.Single(f => f.Id == 4).UnplannedSp));
    }

    [Fact]
    public async Task Art_orders_are_resolved_for_the_pi()
    {
        await SeedAsync();

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var core = data!.Arts.Single(a => a.Id == CorePayments);
        Assert.Equal(ArtOrderMode.FollowsGeneral, core.OrderMode);
        Assert.Equal([(10, 1, true), (ArtBoardItems.None, 2, true), (11, 3, false)], core.Order.Select(e => (e.ItemId, e.Rank, e.IsIncluded)));

        var platform = data.Arts.Single(a => a.Id == DataPlatform);
        Assert.Equal(ArtOrderMode.Empty, platform.OrderMode);
        Assert.Empty(platform.Order);
    }

    [Fact]
    public async Task An_art_without_a_saved_ranking_ranks_nothing_as_on_the_team_boards()
    {
        await SeedAsync();

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var tree = DepartmentPlanningTree.Build(data!, new PlanningOptions());
        var ingestion = tree.FlattenAll().Single(r => r.Level == PlanningLevel.BusinessOutcome && r.EntityId == 102);
        Assert.Empty(ingestion.Ranks);
        Assert.False(ingestion.HasArtRank);
        Assert.Null(data!.Features.Single(f => f.Id == 4).Teams.Single().ArtRank);
    }

    [Fact]
    public async Task The_hierarchy_includes_items_linked_to_the_arts_even_without_business_epics()
    {
        await SeedAsync();

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        Assert.Equal(["STRAT-1", "STRAT-2"], data!.Objectives.Select(o => o.JiraId).Order());
        Assert.Equal([CorePayments], data.Objectives.Single(o => o.Id == 1).ArtIds);
        Assert.Empty(data.Objectives.Single(o => o.Id == 2).ArtIds);
        Assert.Equal([10, 11, 12], data.Epics.Select(e => e.Id).Order());
        Assert.Equal([1], data.Epics.Single(e => e.Id == 10).StrategicObjectiveIds);
        Assert.Equal([100, 101, 102, 103], data.Outcomes.Select(o => o.Id).Order());
        Assert.Equal("Amber - vendor", data.Outcomes.Single(o => o.Id == 100).RagStatus);
    }

    [Fact]
    public async Task Hygiene_failures_are_counted_per_business_epic()
    {
        await SeedAsync();
        _hygiene.Failures[1] = 2;
        _hygiene.Failures[4] = 1;

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        Assert.True(data!.HygieneLoaded);
        Assert.True(data.DependenciesLoaded);
        Assert.Equal(2, data.Features.Single(f => f.Id == 1).HygieneFailures);
        Assert.Equal(0, data.Features.Single(f => f.Id == 2).HygieneFailures);
        Assert.Equal(1, data.Features.Single(f => f.Id == 4).HygieneFailures);
    }

    [Fact]
    public async Task A_failing_enrichment_is_reported_instead_of_failing_the_page()
    {
        await SeedAsync();
        _hygiene.Throw = true;

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments], PiId));

        Assert.False(data!.HygieneLoaded);
        Assert.All(data.Features, f => Assert.Equal(0, f.HygieneFailures));
    }

    [Fact]
    public async Task Explicit_art_ids_win_over_the_department()
    {
        await SeedAsync();

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [DataPlatform], PiId));

        Assert.Equal(["Data Platform"], data!.Arts.Select(a => a.Name));
        Assert.Equal(["Zeta"], data.Teams.Select(t => t.Name));
        Assert.Equal([4], data.Features.Select(f => f.Id));
    }

    [Fact]
    public async Task Details_come_from_the_item_s_own_table()
    {
        await _db.SeedAsync(db =>
        {
            db.PortfolioEpics.Add(new PortfolioEpic { Id = 10, JiraId = "EPIC-10", Summary = "Instant payments", Description = "h1. Goal", Labels = "basel4" });
            db.Features.Add(new Feature { Id = 1, Summary = "Feature", Description = "Feature text" });
        });

        var epic = await _service.GetDetailsAsync(PlanningLevel.PortfolioEpic, 10);
        var feature = await _service.GetDetailsAsync(PlanningLevel.BusinessEpic, 1);

        Assert.Equal("h1. Goal", epic!.Description);
        Assert.Equal("basel4", epic.Labels);
        Assert.Equal("Feature text", feature!.Description);
        Assert.Null(await _service.GetDetailsAsync(PlanningLevel.BusinessOutcome, 10));
        Assert.Null(await _service.GetDetailsAsync(PlanningLevel.Art, 1));
    }

    [Fact]
    public async Task A_dependency_seen_from_both_ends_and_from_two_arts_counts_once()
    {
        await SeedAsync();
        var map = Deps.Data(
            [
                Deps.Feature(1, "PAY-1", DependencyEndKind.InScope, "In Progress", null, Deps.Team(Deps.Axis, true, false, 1)),
                Deps.Feature(2, "PAY-2", DependencyEndKind.InScope, "In Progress", null, Deps.Team(Deps.Regal, true, false, 3)),
            ],
            [Deps.Blocks("501", "PAY-1", "PAY-2"), Deps.Blocks("502", "PAY-2", "PAY-1")]);
        _dependencyMap.MapFor = _ => map;

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var first = data!.Features.Single(f => f.Id == 1);
        Assert.Equal(["f1>f2", "f2>f1"], first.DependencyIds.Order());
        Assert.Equal(2, first.DependenciesAtRisk);
        Assert.Equal(2, data.Features.Single(f => f.Id == 2).DependencyCount);
        var tree = DepartmentPlanningTree.Build(data, new PlanningOptions());
        Assert.Equal(2, tree.Total.Dependencies);
        Assert.Equal(2, tree.Total.DependenciesAtRisk);
        Assert.Equal(2, tree.Total.FeaturesAtRisk);
    }

    [Fact]
    public async Task A_board_feature_of_an_art_outside_the_scope_is_counted_from_its_own_art_s_map()
    {
        await SeedAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectJiraKeys.AddRange(
                new ArtJiraKey { CapitalProjectId = CorePayments, JiraKey = "PAY" },
                new ArtJiraKey { CapitalProjectId = 3, JiraKey = "DATA" });
            AddBoardFeature(db, 77, "DATA-77");
        });
        var corePayments = Deps.Data([Deps.Feature(1, "PAY-1"), Deps.Feature(2, "PAY-2"), Deps.Feature(3, "PAY-3")], []);
        var elsewhere = Deps.Data(
            [Deps.Feature(77, "DATA-77"), Deps.Feature(80, "DATA-80")],
            [Deps.Blocks("901", "DATA-80", "DATA-77")]);
        _dependencyMap.MapFor = artId => artId switch
        {
            CorePayments => corePayments,
            3 => elsewhere,
            _ => null,
        };

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments], PiId));

        Assert.Equal(["f80>f77"], data!.Features.Single(f => f.Id == 77).DependencyIds);
        Assert.Equal([CorePayments, 3], _dependencyMap.Requested);
        Assert.Equal(0, data.FeaturesOffDependencyMaps);
        Assert.True(data.DependenciesLoaded);
    }

    [Fact]
    public async Task A_board_feature_no_art_owns_is_reported_as_off_the_dependency_maps_unless_it_has_no_jira_key()
    {
        await SeedAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectJiraKeys.Add(new ArtJiraKey { CapitalProjectId = CorePayments, JiraKey = "PAY" });
            AddBoardFeature(db, 78, "MISC-78");
            AddBoardFeature(db, 79, null);
        });
        var corePayments = Deps.Data(
            [Deps.Feature(1, "PAY-1"), Deps.Feature(2, "PAY-2"), Deps.Feature(3, "PAY-3"), Deps.WithoutArt(Deps.Feature(78, "MISC-78", DependencyEndKind.OtherArt))],
            [Deps.Blocks("902", "PAY-1", "MISC-78")]);
        _dependencyMap.MapFor = artId => artId == CorePayments ? corePayments : null;

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments], PiId));

        Assert.Equal(1, data!.FeaturesOffDependencyMaps);
        Assert.Equal([CorePayments], _dependencyMap.Requested);
        Assert.Equal(1, data.Features.Single(f => f.Id == 78).DependencyCount);
        Assert.True(data.DependenciesLoaded);
    }

    [Fact]
    public async Task Each_board_feature_remembers_the_map_that_lists_it_and_keeps_it_through_a_capacity_reload()
    {
        await SeedAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectJiraKeys.AddRange(
                new ArtJiraKey { CapitalProjectId = CorePayments, JiraKey = "PAY" },
                new ArtJiraKey { CapitalProjectId = 3, JiraKey = "DATA" });
            AddBoardFeature(db, 77, "DATA-77");
        });
        var corePayments = Deps.Data(
            [Deps.Feature(1, "PAY-1"), Deps.Feature(2, "PAY-2"), Deps.Feature(3, "PAY-3"), Deps.Feature(77, "DATA-77", DependencyEndKind.OtherArt)],
            [Deps.Blocks("903", "PAY-1", "DATA-77")]) with { ArtName = "Core Payments" };
        var elsewhere = Deps.Data([Deps.Feature(77, "DATA-77")], []) with { ArtName = "Elsewhere" };
        _dependencyMap.MapFor = artId => artId switch
        {
            CorePayments => corePayments,
            3 => elsewhere,
            _ => null,
        };

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments], PiId));
        _dependencyMap.MapFor = _ => throw new InvalidOperationException("the dependency maps must not be reloaded");
        var reloaded = await _service.ReloadCapacityAsync(data!, true);

        foreach (var loaded in new[] { data!, reloaded! })
        {
            Assert.Equal([new PlanningMapArt(CorePayments, "Core Payments", true)], loaded.Features.Single(f => f.Id == 1).DependencyMaps);
            Assert.Equal(
                [new PlanningMapArt(3, "Elsewhere", true), new PlanningMapArt(CorePayments, "Core Payments", false)],
                loaded.Features.Single(f => f.Id == 77).DependencyMaps);
        }
    }

    [Fact]
    public async Task A_board_feature_on_no_map_points_at_a_map_that_links_to_it()
    {
        await SeedAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectJiraKeys.Add(new ArtJiraKey { CapitalProjectId = CorePayments, JiraKey = "PAY" });
            AddBoardFeature(db, 78, "MISC-78");
            AddBoardFeature(db, 79, null);
            AddBoardFeature(db, 81, "MISC-81");
        });
        var corePayments = Deps.Data(
            [Deps.Feature(1, "PAY-1"), Deps.Feature(2, "PAY-2"), Deps.Feature(3, "PAY-3"), Deps.WithoutArt(Deps.Feature(78, "MISC-78", DependencyEndKind.OtherArt))],
            [Deps.Blocks("902", "PAY-1", "MISC-78")]) with { ArtName = "Core Payments" };
        _dependencyMap.MapFor = artId => artId == CorePayments ? corePayments : null;

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments], PiId));

        Assert.Equal([new PlanningMapArt(CorePayments, "Core Payments", false)], data!.Features.Single(f => f.Id == 78).DependencyMaps);
        Assert.Empty(data.Features.Single(f => f.Id == 79).DependencyMaps);
        Assert.Empty(data.Features.Single(f => f.Id == 81).DependencyMaps);
        Assert.Equal(2, data.FeaturesOffDependencyMaps);
    }

    [Fact]
    public async Task Without_the_dependency_maps_no_feature_points_at_a_map()
    {
        await SeedAsync();
        _dependencyMap.MapFor = _ => throw new InvalidOperationException("the map is down");

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        Assert.False(data!.DependenciesLoaded);
        Assert.All(data.Features, f => Assert.Empty(f.DependencyMaps));
    }

    [Fact]
    public async Task No_extra_map_is_requested_when_every_board_feature_is_on_a_map()
    {
        await SeedAsync();
        await _db.SeedAsync(db => db.CapitalProjectJiraKeys.Add(new ArtJiraKey { CapitalProjectId = 3, JiraKey = "PAY" }));
        var corePayments = Deps.Data([Deps.Feature(1, "PAY-1"), Deps.Feature(2, "PAY-2"), Deps.Feature(3, "PAY-3")], []);
        var dataPlatform = Deps.Data([Deps.Feature(4, "PAY-4")], []);
        _dependencyMap.MapFor = artId => artId switch
        {
            CorePayments => corePayments,
            DataPlatform => dataPlatform,
            _ => null,
        };

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        Assert.Equal([CorePayments, DataPlatform], _dependencyMap.Requested);
        Assert.Equal(0, data!.FeaturesOffDependencyMaps);
    }

    [Fact]
    public async Task A_board_feature_without_a_jira_key_asks_for_no_extra_map()
    {
        await SeedAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectJiraKeys.AddRange(
                new ArtJiraKey { CapitalProjectId = CorePayments, JiraKey = "PAY" },
                new ArtJiraKey { CapitalProjectId = 3, JiraKey = "DATA" });
            AddBoardFeature(db, 79, null, "DATA");
        });
        var corePayments = Deps.Data([Deps.Feature(1, "PAY-1"), Deps.Feature(2, "PAY-2"), Deps.Feature(3, "PAY-3")], []);
        _dependencyMap.MapFor = artId => artId == CorePayments ? corePayments : null;

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments], PiId));

        Assert.Equal([CorePayments], _dependencyMap.Requested);
        Assert.Equal(0, data!.FeaturesOffDependencyMaps);
    }

    [Fact]
    public async Task The_scope_s_maps_come_in_one_call_and_the_maps_of_other_arts_in_a_second_one()
    {
        await SeedAsync();
        await _db.SeedAsync(db =>
        {
            db.CapitalProjectJiraKeys.AddRange(
                new ArtJiraKey { CapitalProjectId = CorePayments, JiraKey = "PAY" },
                new ArtJiraKey { CapitalProjectId = 3, JiraKey = "DATA" });
            AddBoardFeature(db, 77, "DATA-77");
        });
        var maps = new BatchDependencyMap
        {
            MapFor = artId => artId switch
            {
                CorePayments => Deps.Data([Deps.Feature(1, "PAY-1"), Deps.Feature(2, "PAY-2"), Deps.Feature(3, "PAY-3")], []),
                DataPlatform => Deps.Data([Deps.Feature(4, "PAY-4")], []),
                3 => Deps.Data([Deps.Feature(77, "DATA-77"), Deps.Feature(80, "DATA-80")], [Deps.Blocks("901", "DATA-80", "DATA-77")]),
                _ => null,
            },
        };
        var service = new DepartmentPlanningService(_db, new TeamCapacityService(_db), maps, _hygiene);

        var data = await service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        Assert.Collection(
            maps.Batches,
            batch => Assert.Equal([CorePayments, DataPlatform], batch),
            batch => Assert.Equal([3], batch));
        Assert.Equal(["f80>f77"], data!.Features.Single(f => f.Id == 77).DependencyIds);
        Assert.Equal(0, data.FeaturesOffDependencyMaps);
        Assert.True(data.DependenciesLoaded);
    }

    [Fact]
    public async Task Switching_the_ip_sprint_reloads_only_the_team_boards_and_matches_a_full_load()
    {
        await SeedAsync();
        await SeedArgonSprintsAsync();
        var scope = new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId);
        var without = await _service.GetAsync(scope);
        _dependencyMap.MapFor = _ => throw new InvalidOperationException("the dependency maps must not be reloaded");
        var hygieneCalls = _hygiene.Calls;

        var reloaded = await _service.ReloadCapacityAsync(without!, true);

        Assert.Equal(hygieneCalls, _hygiene.Calls);
        Assert.True(reloaded!.DependenciesLoaded);
        Assert.True(reloaded.Scope.IncludeIpSprints);
        _dependencyMap.MapFor = _ => null;
        var full = await _service.GetAsync(scope with { IncludeIpSprints = true });
        Assert.True(reloaded.Teams.Single(t => t.Id == Argon).CapacitySp > without!.Teams.Single(t => t.Id == Argon).CapacitySp);
        Assert.Equal(TeamFacts(full!), TeamFacts(reloaded));
        Assert.Equal(LinkFacts(full!), LinkFacts(reloaded));
        Assert.Equal(full!.Features.Select(f => f.Id), reloaded.Features.Select(f => f.Id));
    }

    [Fact]
    public async Task A_capacity_reload_falls_back_to_a_full_load_when_the_boards_carry_other_features()
    {
        await SeedAsync();
        var scope = new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId);
        var data = await _service.GetAsync(scope);
        var stale = data! with { Features = data.Features.Skip(1).ToList() };

        var reloaded = await _service.ReloadCapacityAsync(stale, true);

        Assert.Equal(data.Features.Select(f => f.Id), reloaded!.Features.Select(f => f.Id));
        Assert.True(reloaded.Scope.IncludeIpSprints);
    }

    [Fact]
    public async Task A_capacity_reload_reads_the_art_order_again_and_keeps_the_load_time()
    {
        await SeedAsync();
        var scope = new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId);
        var data = await _service.GetAsync(scope);
        await _db.SeedAsync(db =>
        {
            var tokenisation = db.ArtPrioritizationOrders.Single(o => o.PortfolioEpicId == 11);
            tokenisation.IsIncluded = true;
        });

        var reloaded = await _service.ReloadCapacityAsync(data!, true);
        var full = await _service.GetAsync(scope with { IncludeIpSprints = true });

        Assert.Equal(data!.LoadedUtc, reloaded!.LoadedUtc);
        Assert.Equal(OrderFacts(full!), OrderFacts(reloaded));
        Assert.Equal(full!.Features.Select(f => (f.Id, f.PlannedSp)), reloaded.Features.Select(f => (f.Id, f.PlannedSp)));
        Assert.NotEqual(OrderFacts(data), OrderFacts(reloaded));
    }

    [Fact]
    public async Task A_capacity_reload_falls_back_to_a_full_load_when_an_art_gains_a_team()
    {
        await SeedAsync();
        var scope = new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId);
        var data = await _service.GetAsync(scope);
        await _db.SeedAsync(db => db.CapitalProjectTeams.Add(new ArtTeam { CapitalProjectId = CorePayments, TeamId = 4 }));

        var reloaded = await _service.ReloadCapacityAsync(data!, true);

        Assert.Contains(reloaded!.Teams, t => t.Name == "Omega");
        Assert.Contains(reloaded.Features, f => f.Id == 6);
    }

    [Fact]
    public async Task A_capacity_reload_retries_everything_when_the_dependencies_failed_to_load()
    {
        await SeedAsync();
        _dependencyMap.MapFor = _ => throw new InvalidOperationException("the map is down");
        var scope = new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId);
        var data = await _service.GetAsync(scope);
        Assert.False(data!.DependenciesLoaded);
        _dependencyMap.MapFor = _ => null;

        var reloaded = await _service.ReloadCapacityAsync(data, true);

        Assert.True(reloaded!.DependenciesLoaded);
    }

    [Fact]
    public async Task The_load_token_reaches_the_team_boards_both_dependency_map_passes_and_the_hygiene_check()
    {
        await SeedAsync();
        await _db.SeedAsync(db => db.CapitalProjectJiraKeys.Add(new ArtJiraKey { CapitalProjectId = 3, JiraKey = "PAY" }));
        var boards = new RecordingTeamCapacity(new TeamCapacityService(_db));
        var service = new DepartmentPlanningService(_db, boards, _dependencyMap, _hygiene);
        using var cancel = new CancellationTokenSource();

        await service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId), cancel.Token);

        Assert.Equal([cancel.Token], boards.Tokens);
        Assert.Equal([CorePayments, DataPlatform, 3], _dependencyMap.Requested);
        Assert.All(_dependencyMap.Tokens, token => Assert.Equal(cancel.Token, token));
        Assert.Equal([cancel.Token], _hygiene.Tokens);
    }

    [Fact]
    public async Task A_capacity_reload_passes_the_token_to_the_team_boards_and_to_its_full_load_fallback()
    {
        await SeedAsync();
        var boards = new RecordingTeamCapacity(new TeamCapacityService(_db));
        var service = new DepartmentPlanningService(_db, boards, _dependencyMap, _hygiene);
        var data = await service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));
        boards.Tokens.Clear();
        _hygiene.Tokens.Clear();
        using var cancel = new CancellationTokenSource();

        await service.ReloadCapacityAsync(data!, true, cancel.Token);
        await service.ReloadCapacityAsync(data! with { Features = data.Features.Skip(1).ToList() }, true, cancel.Token);

        Assert.Equal(3, boards.Tokens.Count);
        Assert.All(boards.Tokens, token => Assert.Equal(cancel.Token, token));
        Assert.Equal([cancel.Token], _hygiene.Tokens);
    }

    [Fact]
    public async Task A_load_cancelled_while_the_dependency_maps_load_is_cancelled_instead_of_missing_its_dependencies()
    {
        await SeedAsync();
        using var cancel = new CancellationTokenSource();
        _dependencyMap.MapFor = _ =>
        {
            cancel.Cancel();
            throw new InvalidOperationException("Operation cancelled by user.");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId), cancel.Token));
        Assert.Equal(0, _hygiene.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_load_cancelled_during_the_hygiene_check_is_cancelled_instead_of_missing_its_hygiene(bool failsAfterTheCancel)
    {
        await SeedAsync();
        using var cancel = new CancellationTokenSource();
        _hygiene.OnCall = cancel.Cancel;
        _hygiene.Throw = failsAfterTheCancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId), cancel.Token));
    }

    [Fact]
    public async Task Enrichment_failures_without_a_cancellation_still_only_degrade_the_page()
    {
        await SeedAsync();
        using var cancel = new CancellationTokenSource();
        _hygiene.Throw = true;
        _dependencyMap.MapFor = _ => throw new InvalidOperationException("the map is down");

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId), cancel.Token);

        Assert.False(data!.HygieneLoaded);
        Assert.False(data.DependenciesLoaded);
    }

    [Fact]
    public async Task A_capacity_reload_cancelled_while_the_boards_load_is_cancelled()
    {
        await SeedAsync();
        var boards = new RecordingTeamCapacity(new TeamCapacityService(_db));
        var service = new DepartmentPlanningService(_db, boards, _dependencyMap, _hygiene);
        var data = await service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));
        boards.Tokens.Clear();
        using var cancel = new CancellationTokenSource();
        boards.OnLoad = cancel.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReloadCapacityAsync(data!, true, cancel.Token));
        Assert.Equal([cancel.Token], boards.Tokens);
    }

    private static List<(int, string, int, int, bool)> OrderFacts(DepartmentPlanningData data) =>
        data.Arts.SelectMany(a => a.Order.Select(e => (a.Id, a.OrderMode.ToString(), e.ItemId, e.Rank, e.IsIncluded))).ToList();

    private Task SeedArgonSprintsAsync() => _db.SeedAsync(db =>
    {
        db.HumanResources.Add(new HumanResource { Id = 1, EmployeeName = "ann", FullName = "Ann" });
        db.TeamMembers.Add(new TeamMember { TeamId = Argon, HumanResourceId = 1 });
        db.Sprints.AddRange(
            new Sprint { Id = 1, TeamId = Argon, PiId = PiId, Name = "Sprint 1", StartDate = new DateTime(2026, 9, 14), EndDate = new DateTime(2026, 9, 25) },
            new Sprint { Id = 2, TeamId = Argon, PiId = PiId, Name = "IP", StartDate = new DateTime(2026, 9, 28), EndDate = new DateTime(2026, 10, 2), IsIpSprint = true });
    });

    private Task SeedMembersAsync(params int[] teamIds) => _db.SeedAsync(db =>
    {
        foreach (var teamId in teamIds)
        {
            db.HumanResources.Add(new HumanResource { Id = 100 + teamId, EmployeeName = $"member{teamId}", FullName = $"Member {teamId}" });
            db.TeamMembers.Add(new TeamMember { TeamId = teamId, HumanResourceId = 100 + teamId });
        }
    });

    private static List<(int, decimal, double, int, string)> TeamFacts(DepartmentPlanningData data) =>
        data.Teams.Select(t => (t.Id, t.CapacitySp, t.CapacityDays, t.MemberCount, t.WindowMode)).ToList();

    private static List<(int, int, decimal, bool, int, decimal)> LinkFacts(DepartmentPlanningData data) =>
        data.Features.SelectMany(f => f.Teams.Select(t => (f.Id, t.TeamId, t.DemandSp, t.IsIncluded, t.BoardPosition, t.ReservedSp))).ToList();

    private static void AddUnestimatedFeature(EstimationDbContext db, int id, int storyPoints, params int[] teamIds)
    {
        db.Features.Add(new Feature
        {
            Id = id,
            JiraId = $"PAY-{id}",
            Name = $"Feature {id}",
            Summary = $"Feature {id}",
            Status = "To Do",
            BusinessOutcomeId = 100,
            PiId = PiId,
            StoryPoints = storyPoints,
        });
        foreach (var teamId in teamIds)
        {
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = id, TeamId = teamId });
        }
    }

    private static void AddBoardFeature(EstimationDbContext db, int id, string? jiraId, string? projectKey = null)
    {
        db.Features.Add(new Feature { Id = id, JiraId = jiraId, ProjectKey = projectKey, Name = $"Feature {id}", Summary = $"Feature {id}", Status = "To Do", PiId = PiId });
        db.FeatureTeams.Add(new FeatureTeam { FeatureId = id, TeamId = Argon, StoryPoints = 10 });
    }

    private sealed class FakeDependencyMap : IDependencyMapService
    {
        public Func<int, DependencyMapData?> MapFor { get; set; } = _ => null;

        public List<int> Requested { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<DependencyMapData?> GetAsync(int capitalProjectId, int piId, bool includePiLabelMatches = true, CancellationToken cancellationToken = default)
        {
            Requested.Add(capitalProjectId);
            Tokens.Add(cancellationToken);
            var map = MapFor(capitalProjectId);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(map);
        }
    }

    private sealed class RecordingTeamCapacity(ITeamCapacityService inner) : ITeamCapacityService
    {
        public List<CancellationToken> Tokens { get; } = [];

        public Action? OnLoad { get; set; }

        public Task<TeamCapacityResult> GetAsync(int teamId, int piId, bool includeIpSprints = false) =>
            inner.GetAsync(teamId, piId, includeIpSprints);

        public Task<IReadOnlyDictionary<int, TeamCapacityResult>> GetManyAsync(
            IReadOnlyCollection<int> teamIds, int piId, bool includeIpSprints = false, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            OnLoad?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return inner.GetManyAsync(teamIds, piId, includeIpSprints, cancellationToken);
        }

        public Task SaveOrderAsync(int teamId, int piId, IList<TeamCapacityOrderRow> rows, IReadOnlyCollection<int>? refreshSnapshotFeatureIds = null) =>
            throw new NotSupportedException();

        public Task<FeatureFindMetadata> GetFeatureFindMetadataAsync() => throw new NotSupportedException();

        public Task<List<FeatureFindRow>> FindFeaturesAsync(int teamId, int piId, FeatureFindCriteria criteria) => throw new NotSupportedException();

        public Task<Dictionary<int, List<FeatureOtherTeamLine>>> GetFeatureOtherTeamLinesAsync(int teamId, int piId, IReadOnlyCollection<int> featureIds) =>
            throw new NotSupportedException();

        public Task PropagateInclusionToOtherTeamsAsync(int sourceTeamId, int piId, IReadOnlyDictionary<int, bool> featureInclusion) =>
            throw new NotSupportedException();
    }

    private sealed class BatchDependencyMap : IDependencyMapService
    {
        public Func<int, DependencyMapData?> MapFor { get; set; } = _ => null;

        public List<List<int>> Batches { get; } = [];

        public Task<DependencyMapData?> GetAsync(int capitalProjectId, int piId, bool includePiLabelMatches = true, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<int, DependencyMapData?>> GetManyAsync(
            IReadOnlyCollection<int> capitalProjectIds, int piId, bool includePiLabelMatches = true, CancellationToken cancellationToken = default)
        {
            Batches.Add(capitalProjectIds.ToList());
            return Task.FromResult<IReadOnlyDictionary<int, DependencyMapData?>>(capitalProjectIds.Distinct().ToDictionary(id => id, MapFor));
        }
    }

    private sealed class FakeHygiene : IFeatureHygieneService
    {
        public Dictionary<int, int> Failures { get; } = new();

        public bool Throw { get; set; }

        public int Calls { get; private set; }

        public List<CancellationToken> Tokens { get; } = [];

        public Action? OnCall { get; set; }

        public Task<FeatureHygieneReport?> EvaluateAsync(int capitalProjectId, int piId, bool includePiLabelMatches = true) =>
            Task.FromResult<FeatureHygieneReport?>(null);

        public Task<IReadOnlyDictionary<int, IReadOnlyList<FeatureHygieneRule>>> GetRulesByArtIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<FeatureHygieneRule>>>(new Dictionary<int, IReadOnlyList<FeatureHygieneRule>>());

        public Task<IReadOnlyDictionary<int, IReadOnlyList<HygieneFailure>>> GetFailuresByFeatureAsync(IReadOnlyCollection<int> featureIds, CancellationToken cancellationToken = default)
        {
            Calls++;
            Tokens.Add(cancellationToken);
            OnCall?.Invoke();
            if (Throw)
            {
                throw new InvalidOperationException("hygiene is down");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var result = new Dictionary<int, IReadOnlyList<HygieneFailure>>();
            foreach (var (featureId, count) in Failures)
            {
                result[featureId] = Enumerable.Range(0, count)
                    .Select(i => new HygieneFailure(i, HygieneField.Description, HygieneCheck.NotEmpty, "rule", "reason", null, null, []))
                    .ToList();
            }

            return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<HygieneFailure>>>(result);
        }
    }
}
