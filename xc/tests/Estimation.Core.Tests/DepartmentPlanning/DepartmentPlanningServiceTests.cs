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
    private readonly DepartmentPlanningService _service;

    public DepartmentPlanningServiceTests()
    {
        _service = new DepartmentPlanningService(_db, new TeamCapacityService(_db), new FakeDependencyMap(), _hygiene);
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

        var data = await _service.GetAsync(new DepartmentPlanningScope(Retail, [CorePayments, DataPlatform], PiId));

        var first = data!.Features.Single(f => f.Id == 1);
        Assert.Equal([(Argon, 30, true), (Boron, 20, true)], first.Teams.Select(t => (t.TeamId, t.DemandSp, t.IsPlanned)).OrderBy(t => t.TeamId));
        Assert.Equal(50, first.PlannedSp);

        var vault = data.Features.Single(f => f.Id == 2);
        var link = vault.Teams.Single();
        Assert.True(link.IsIncluded);
        Assert.Equal(["Core Payments"], link.BelowArtLine.Select(a => a.ArtName));
        Assert.False(link.IsPlanned);
        Assert.Equal(40, vault.UnplannedSp);

        var blocked = data.Features.Single(f => f.Id == 3);
        Assert.False(blocked.Teams.Single().IsIncluded);
        Assert.Equal(25, blocked.UnplannedSp);
        Assert.Equal(1, blocked.Teams.Single().BoardPosition);
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
        Assert.Equal([102], platform.Order.Select(e => e.ItemId));
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

    private sealed class FakeDependencyMap : IDependencyMapService
    {
        public Task<DependencyMapData?> GetAsync(int capitalProjectId, int piId, bool includePiLabelMatches = true, CancellationToken cancellationToken = default) =>
            Task.FromResult<DependencyMapData?>(null);
    }

    private sealed class FakeHygiene : IFeatureHygieneService
    {
        public Dictionary<int, int> Failures { get; } = new();

        public bool Throw { get; set; }

        public Task<FeatureHygieneReport?> EvaluateAsync(int capitalProjectId, int piId, bool includePiLabelMatches = true) =>
            Task.FromResult<FeatureHygieneReport?>(null);

        public Task<IReadOnlyDictionary<int, IReadOnlyList<FeatureHygieneRule>>> GetRulesByArtIdAsync() =>
            Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<FeatureHygieneRule>>>(new Dictionary<int, IReadOnlyList<FeatureHygieneRule>>());

        public Task<IReadOnlyDictionary<int, IReadOnlyList<HygieneFailure>>> GetFailuresByFeatureAsync(IReadOnlyCollection<int> featureIds)
        {
            if (Throw)
            {
                throw new InvalidOperationException("hygiene is down");
            }

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
