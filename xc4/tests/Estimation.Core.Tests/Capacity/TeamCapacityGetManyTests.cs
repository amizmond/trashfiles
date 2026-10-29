using System.Text.Json;
using Estimation.Core.Calendar.Models;
using Estimation.Core.Capacity.Models;
using Estimation.Core.Capacity.Services;
using Estimation.Core.Features.Models;
using Estimation.Core.PlanningIncrement.Models;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Estimation.Core.Train.Models;
using Xunit;

namespace Estimation.Core.Tests.Capacity;

public class TeamCapacityGetManyTests
{
    private const int PiId = 1;
    private const int NextPiId = 2;
    private const int Argon = 1;
    private const int Boron = 2;
    private const int Cobalt = 3;
    private const int Empty = 4;
    private const int Unknown = 99;
    private const int Ann = 1;
    private const int Ben = 2;
    private const int Cat = 3;
    private const int Dan = 4;
    private const int Krakow = 10;
    private const int Warsaw = 11;
    private const int CSharp = 1;
    private const int Sql = 2;
    private const int Backend = 1;

    private static readonly int[] AllTeams = [Argon, Boron, Cobalt, Empty, Unknown];

    private readonly InMemoryDatabase _db = new();
    private readonly TeamCapacityService _service;

    public TeamCapacityGetManyTests()
    {
        _service = new TeamCapacityService(_db);
    }

    private Task SeedAsync() => _db.SeedAsync(db =>
    {
        db.Pis.AddRange(
            new Pi
            {
                Id = PiId,
                Name = "PI 26.11",
                StartDate = new DateTime(2026, 11, 2),
                EndDate = new DateTime(2026, 11, 27),
                FeatureLabels = "pi-26.11",
            },
            new Pi { Id = NextPiId, Name = "PI 26.12", StartDate = new DateTime(2026, 11, 30), EndDate = new DateTime(2026, 12, 18) });

        db.Countries.Add(new Country { Id = 1, Name = "Poland" });
        db.Cities.AddRange(
            new City { Id = Krakow, Name = "Krakow", CountryId = 1 },
            new City { Id = Warsaw, Name = "Warsaw", CountryId = 1 });
        db.PublicHolidays.AddRange(
            new PublicHoliday { Id = 1, CityId = Krakow, Date = new DateTime(2026, 11, 4), Name = "Krakow day" },
            new PublicHoliday { Id = 2, CityId = Krakow, Date = new DateTime(2026, 11, 5), Name = "Krakow day 2" },
            new PublicHoliday { Id = 3, CountryId = 1, Date = new DateTime(2026, 11, 11), Name = "Independence Day" },
            new PublicHoliday { Id = 4, CountryId = 1, Date = new DateTime(2026, 11, 17), Name = "National day" });

        db.Teams.AddRange(
            new Team { Id = Argon, Name = "Argon" },
            new Team { Id = Boron, Name = "Boron", StoryPointsPerDay = 2m },
            new Team { Id = Cobalt, Name = "Cobalt" },
            new Team { Id = Empty, Name = "Empty" });
        db.CapitalProjects.Add(new Art { Id = 1, Name = "Core Payments", Prioritization = ArtPrioritization.BusinessOutcome });
        db.CapitalProjectTeams.AddRange(
            new ArtTeam { CapitalProjectId = 1, TeamId = Argon },
            new ArtTeam { CapitalProjectId = 1, TeamId = Boron });

        db.Sprints.AddRange(
            Sprint(1, Argon, 2, 6),
            Sprint(2, Argon, 9, 13),
            Sprint(3, Argon, 16, 20, isIpSprint: true),
            Sprint(4, Boron, 9, 13),
            Sprint(5, Boron, 16, 20));

        db.TechnologyStacks.AddRange(
            new TechnologyStack { Id = CSharp, Name = "C#" },
            new TechnologyStack { Id = Sql, Name = "SQL" });
        db.TeamTechnologyStacks.AddRange(
            new TeamTechnologyStack { TeamId = Argon, TechnologyStackId = CSharp },
            new TeamTechnologyStack { TeamId = Argon, TechnologyStackId = Sql },
            new TeamTechnologyStack { TeamId = Boron, TechnologyStackId = Sql });
        var skill = new Skill { Id = Backend, Name = "Backend", Created = new DateTime(2026, 1, 1) };
        skill.Levels.Add(new SkillLevel { Id = 1, Name = "Senior", Value = 3 });
        db.Skills.Add(skill);

        AddPerson(db, Ann, "Ann", Krakow);
        AddPerson(db, Ben, "Ben", Warsaw);
        AddPerson(db, Cat, "Cat", null);
        AddPerson(db, Dan, "Dan", Krakow, isActive: false);
        db.HumanResourceSkills.Add(new HumanResourceSkill { HumanResourceId = Ann, SkillId = Backend, SkillLevelId = 1 });

        AddMember(db, Argon, Ann, CSharp);
        AddMember(db, Argon, Dan, CSharp);
        AddMember(db, Boron, Ann, Sql);
        AddMember(db, Boron, Ben, Sql);
        AddMember(db, Cobalt, Cat, null);

        db.TeamMemberCoefficients.AddRange(
            new TeamMemberCoefficient { TeamId = Argon, HumanResourceId = Ann, PiId = PiId, Value = 0.5m },
            new TeamMemberCoefficient { TeamId = Boron, HumanResourceId = Ann, PiId = null, Value = 0.8m },
            new TeamMemberCoefficient { TeamId = Boron, HumanResourceId = Ann, PiId = NextPiId, Value = 0.1m },
            new TeamMemberCoefficient { TeamId = Boron, HumanResourceId = Ben, PiId = PiId, Value = 0.9m });

        db.HolidayTypes.Add(new HolidayType { Id = 1, Name = "Annual leave" });
        db.Holidays.AddRange(
            new Holiday
            {
                Id = 1,
                HumanResourceId = Ann,
                HolidayTypeId = 1,
                StartDate = new DateTime(2026, 11, 12),
                EndDate = new DateTime(2026, 11, 13),
            },
            new Holiday
            {
                Id = 2,
                HumanResourceId = Ben,
                HolidayTypeId = 1,
                StartDate = new DateTime(2026, 11, 16),
                EndDate = new DateTime(2026, 11, 16),
                HalfDay = HalfDayPortion.AM,
            });

        db.BusinessOutcomes.AddRange(
            new BusinessOutcome { Id = 1, JiraId = "BO-1", Summary = "Rails" },
            new BusinessOutcome { Id = 2, JiraId = "BO-2", Summary = "Vault" });
        db.ArtPrioritizationOrders.AddRange(
            new ArtPrioritizationOrder
            {
                CapitalProjectId = 1,
                PiId = PiId,
                Level = ArtPrioritization.BusinessOutcome,
                BusinessOutcomeId = 2,
                SortOrder = 1,
                IsIncluded = true,
            },
            new ArtPrioritizationOrder
            {
                CapitalProjectId = 1,
                PiId = PiId,
                Level = ArtPrioritization.BusinessOutcome,
                BusinessOutcomeId = 1,
                SortOrder = 2,
                IsIncluded = true,
            });

        AddFeature(db, 1, PiId, 1, null, (Argon, 10, true), (Boron, 5, false));
        AddFeature(db, 2, PiId, 2, null, (Argon, 8, true));
        AddFeature(db, 3, NextPiId, null, "pi-26.11", (Boron, 3, true));
        AddFeature(db, 4, NextPiId, null, null, (Argon, 4, true), (Boron, 6, true));
        AddFeature(db, 5, PiId, null, null, (Cobalt, 7, true));

        db.TeamCapacityFeatureOrders.AddRange(
            new TeamCapacityFeatureOrder { TeamId = Argon, PiId = PiId, FeatureId = 2, SortOrder = 1, IsIncluded = true },
            new TeamCapacityFeatureOrder
            {
                TeamId = Argon,
                PiId = PiId,
                FeatureId = 4,
                SortOrder = 2,
                IsIncluded = false,
                IsManuallyAdded = true,
                AddedFeaturePiId = PiId,
            });

        db.FeatureTeamTechnologyStacks.AddRange(
            new FeatureTeamTechnologyStack { FeatureId = 1, TeamId = Argon, TechnologyStackId = CSharp, StoryPoints = 10 },
            new FeatureTeamTechnologyStack { FeatureId = 1, TeamId = Boron, TechnologyStackId = Sql, StoryPoints = 5 });
        db.FeatureTeamStackSkills.Add(new FeatureTeamStackSkill
        {
            FeatureId = 1,
            TeamId = Argon,
            TechnologyStackId = CSharp,
            SkillId = Backend,
            Value = 2m,
        });
        db.FeatureSkills.Add(new FeatureSkill { FeatureId = 1, SkillId = Backend, Value = 1m });
    });

    private static Sprint Sprint(int id, int teamId, int startDay, int endDay, bool isIpSprint = false) => new()
    {
        Id = id,
        TeamId = teamId,
        PiId = PiId,
        Name = $"Sprint {id}",
        StartDate = new DateTime(2026, 11, startDay),
        EndDate = new DateTime(2026, 11, endDay),
        IsIpSprint = isIpSprint,
    };

    private static void AddPerson(EstimationDbContext db, int id, string name, int? cityId, bool isActive = true) =>
        db.HumanResources.Add(new HumanResource
        {
            Id = id,
            EmployeeName = name.ToLowerInvariant(),
            FullName = name,
            CityId = cityId,
            IsActive = isActive,
        });

    private static void AddMember(EstimationDbContext db, int teamId, int personId, int? stackId)
    {
        db.TeamMembers.Add(new TeamMember { TeamId = teamId, HumanResourceId = personId });
        if (stackId is { } stack)
        {
            db.TeamMemberTechnologyStacks.Add(new TeamMemberTechnologyStack
            {
                TeamId = teamId,
                HumanResourceId = personId,
                TechnologyStackId = stack,
            });
        }
    }

    private static void AddFeature(
        EstimationDbContext db, int id, int piId, int? outcomeId, string? labels,
        params (int TeamId, int StoryPoints, bool IsPrimary)[] teams)
    {
        db.Features.Add(new Feature
        {
            Id = id,
            JiraId = $"PAY-{id}",
            Name = $"Feature {id}",
            Summary = $"Feature {id}",
            PiId = piId,
            BusinessOutcomeId = outcomeId,
            Labels = labels,
        });
        foreach (var (teamId, storyPoints, isPrimary) in teams)
        {
            db.FeatureTeams.Add(new FeatureTeam { FeatureId = id, TeamId = teamId, StoryPoints = storyPoints, IsPrimary = isPrimary });
        }
    }

    private static string Json(TeamCapacityResult board) => JsonSerializer.Serialize(board);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batching_teams_together_does_not_change_any_team_board(bool includeIpSprints)
    {
        await SeedAsync();

        var boards = await _service.GetManyAsync(AllTeams, PiId, includeIpSprints);

        Assert.Equal(AllTeams, boards.Keys);
        foreach (var teamId in AllTeams)
        {
            var single = await _service.GetAsync(teamId, PiId, includeIpSprints);
            Assert.Equal(Json(single), Json(boards[teamId]));
        }
    }

    [Fact]
    public async Task A_member_of_two_teams_gets_the_public_holidays_of_each_team_window()
    {
        await SeedAsync();

        var boards = await _service.GetManyAsync(AllTeams, PiId);

        var annOnArgon = Assert.Single(boards[Argon].Members);
        Assert.Equal((Ann, 10d, 2d, 3d, 0.5m), (annOnArgon.HumanResourceId, annOnArgon.BusinessDays, annOnArgon.HolidayDays, annOnArgon.PublicHolidayDays, annOnArgon.Coefficient));
        Assert.Equal(2.5d, annOnArgon.Capacity, 6);

        Assert.Equal(["Ann", "Ben"], boards[Boron].Members.Select(m => m.FullName));
        var annOnBoron = boards[Boron].Members[0];
        Assert.Equal((10d, 2d, 2d, 0.8m), (annOnBoron.BusinessDays, annOnBoron.HolidayDays, annOnBoron.PublicHolidayDays, annOnBoron.Coefficient));
        Assert.Equal(9.6d, annOnBoron.Capacity, 6);
        var ben = boards[Boron].Members[1];
        Assert.Equal((10d, 0.5d, 2d, 0.9m), (ben.BusinessDays, ben.HolidayDays, ben.PublicHolidayDays, ben.Coefficient));
        Assert.Equal(13.5d, ben.Capacity, 6);
        Assert.Equal(23.1d, boards[Boron].TotalCapacity, 6);

        var cat = Assert.Single(boards[Cobalt].Members);
        Assert.Equal(("PI", 20d, 0d), (boards[Cobalt].WindowMode, cat.BusinessDays, cat.PublicHolidayDays));
    }

    [Fact]
    public async Task Each_team_keeps_its_own_features_orders_drifts_and_demands()
    {
        await SeedAsync();

        var boards = await _service.GetManyAsync(AllTeams, PiId);

        var argon = boards[Argon];
        Assert.Equal([2, 4, 1], argon.Features.Select(f => f.FeatureId));
        var sharedOnArgon = argon.Features.Single(f => f.FeatureId == 1);
        Assert.Equal(10, sharedOnArgon.Effort);
        Assert.Equal([(Argon, true), (Boron, false)], sharedOnArgon.Teams!.Select(t => (t.TeamId, t.IsPrimary)));
        var manual = argon.Features.Single(f => f.FeatureId == 4);
        Assert.True(manual.IsManuallyAdded);
        Assert.False(manual.IsIncluded);
        var drift = Assert.Single(argon.Drifts);
        Assert.Equal((4, "PI 26.11", "PI 26.12"), (drift.FeatureId, drift.AddedPiName, drift.CurrentPiName));
        var argonStack = Assert.Single(argon.Demands[1].Stacks);
        Assert.Equal(("C#", 10), (argonStack.StackName, argonStack.StoryPoints));
        Assert.Equal(["Backend"], argonStack.Skills.Select(s => s.SkillName));
        Assert.Equal([("C#", 1), ("SQL", 0)], argon.StackCapacities.Select(s => (s.StackName, s.MemberCount)));

        var boron = boards[Boron];
        Assert.Equal([1, 3], boron.Features.Select(f => f.FeatureId).Order());
        Assert.Equal(5, boron.Features.Single(f => f.FeatureId == 1).Effort);
        Assert.True(boron.Features.Single(f => f.FeatureId == 3).MatchedByLabel);
        Assert.Empty(boron.Drifts);
        var boronStack = Assert.Single(boron.Demands[1].Stacks);
        Assert.Equal(("SQL", 5), (boronStack.StackName, boronStack.StoryPoints));
        Assert.Empty(boronStack.Skills);
        Assert.Equal(["Backend"], boron.Demands[1].FeatureSkills.Select(s => s.SkillName));
        Assert.Equal(2m, boron.StoryPointsPerDay);

        Assert.Equal("Core Payments", argon.Prioritization.OrderArt?.ArtName);
        Assert.Equal("Core Payments", boron.Prioritization.OrderArt?.ArtName);
        Assert.Equal([5], boards[Cobalt].Features.Select(f => f.FeatureId));
        Assert.Empty(boards[Cobalt].Prioritization.Arts);
    }

    [Fact]
    public async Task Teams_without_members_or_unknown_ids_get_an_empty_board()
    {
        await SeedAsync();

        var boards = await _service.GetManyAsync([Empty, Unknown], PiId);

        Assert.All(boards.Values, board =>
        {
            Assert.Equal("PI", board.WindowMode);
            Assert.Empty(board.Members);
            Assert.Empty(board.Features);
            Assert.Empty(board.StackCapacities);
            Assert.Equal(1m, board.StoryPointsPerDay);
        });
    }

    [Fact]
    public async Task Repeated_ids_give_one_board_each_and_no_ids_give_no_boards()
    {
        await SeedAsync();

        var boards = await _service.GetManyAsync([Boron, Argon, Boron], PiId);

        Assert.Equal([Boron, Argon], boards.Keys);
        Assert.Empty(await _service.GetManyAsync([], PiId));
    }
}
