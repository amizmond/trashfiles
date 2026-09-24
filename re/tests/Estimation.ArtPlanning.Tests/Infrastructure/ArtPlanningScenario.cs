using Estimation.ArtPlanning.Data;
using Estimation.ArtPlanning.Models.Settings;
using Estimation.ArtPlanning.Models.Source;
using Estimation.ArtPlanning.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Estimation.ArtPlanning.Tests.Infrastructure;

internal sealed class ArtPlanningScenario : IDbContextFactory<ArtPlanningDbContext>
{
    private static readonly InMemoryDatabaseRoot Root = new();

    public const int ArtId = 1;
    public const int PiId = 1;

    public const int TeamOneId = 10;
    public const int TeamTwoId = 11;

    public const int AnnaId = 100;
    public const string AnnaNumber = "E100";
    public const int AnnaCityId = 500;

    public const int SkillSql = 1;
    public const int SkillPython = 2;
    public const int SkillTechPo = 3;
    public const int SkillR = 4;

    public const int LevelIntermediate = 21;
    public const int LevelAdvanced = 22;
    public const int LevelBeginner = 23;

    public const int RoleScrumMaster = 31;
    public const int RoleProductOwner = 32;
    public const int RoleDeveloper = 33;

    public static readonly DateTime PiStart = new(2026, 1, 5);

    public static readonly DateTime PiEnd = new(2026, 3, 15);

    public const int FullWorkingDays = 35;

    private readonly DbContextOptions<ArtPlanningDbContext> _options;

    private ArtPlanningScenario()
    {
        _options = new DbContextOptionsBuilder<ArtPlanningDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), Root)
            .ConfigureWarnings(w => w
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
                .Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    public ArtPlanningDbContext CreateDbContext() => new(_options);

    public static ArtPlanningScenario Default() => Default(withPiSettings: true);

    public static ArtPlanningScenario Default(bool withPiSettings)
    {
        var scenario = new ArtPlanningScenario();
        using var db = scenario.CreateDbContext();

        db.CapitalProjects.Add(new SourceArt { Id = ArtId, Name = "Axis" });
        db.Pis.Add(new SourcePi { Id = PiId, Name = "2026 PI3", StartDate = PiStart, EndDate = PiEnd });

        db.Teams.Add(new SourceTeam { Id = TeamOneId, Name = "Alpha" });
        db.Teams.Add(new SourceTeam { Id = TeamTwoId, Name = "Beta" });
        db.CapitalProjectTeams.Add(TeamOnArt(TeamOneId));

        db.Skills.AddRange(
            new SourceSkill { Id = SkillSql, Name = "SQL" },
            new SourceSkill { Id = SkillPython, Name = "Python" },
            new SourceSkill { Id = SkillTechPo, Name = "Tech PO" },
            new SourceSkill { Id = SkillR, Name = "R" });

        db.SkillLevels.AddRange(
            new SourceSkillLevel { Id = LevelBeginner, Name = "Beginner", Value = 1 },
            new SourceSkillLevel { Id = LevelIntermediate, Name = "Intermediate", Value = 2 },
            new SourceSkillLevel { Id = LevelAdvanced, Name = "Advanced", Value = 3 });

        db.TeamRoles.AddRange(
            new SourceTeamRole { Id = RoleScrumMaster, Name = "Scrum Master" },
            new SourceTeamRole { Id = RoleProductOwner, Name = "Product Owner" },
            new SourceTeamRole { Id = RoleDeveloper, Name = "Developer" });

        if (!withPiSettings)
        {
            db.SaveChanges();
            return scenario;
        }

        db.SkillLevelAllocations.AddRange(
            Allocation(levelValue: 3, ratio: 1.00m, usageOrder: 1),
            Allocation(levelValue: 4, ratio: 1.00m, usageOrder: 1),
            Allocation(levelValue: 2, ratio: 0.75m, usageOrder: 2));

        db.SkillRankings.AddRange(
            Ranking(SkillSql, 3),
            Ranking(SkillPython, 8),
            Ranking(SkillTechPo, 14));

        db.SaveChanges();
        return scenario;
    }

    public ArtPlanningScenario With(Action<ArtPlanningDbContext> seed)
    {
        using var db = CreateDbContext();
        seed(db);
        db.SaveChanges();
        return this;
    }

    public IArtPlanningSettingsService SettingsService => new ArtPlanningSettingsService(this);

    public Task<Models.ArtPlanningCapacityResult> CalculateAsync()
    {
        var settings = new ArtPlanningSettingsService(this);
        var service = new ArtPlanningCapacityService(this, settings);
        return service.CalculateAsync(ArtId, PiId);
    }


    public static SourceHumanResource Person(
        int id, string? number, string name, bool isActive = true, int? cityId = null) =>
        new()
        {
            Id = id,
            EmployeeNumber = number,
            EmployeeName = name,
            FullName = name,
            IsActive = isActive,
            CityId = cityId,
        };

    public static SourceArtTeam TeamOnArt(int teamId, int capitalProjectId = ArtId) =>
        new() { CapitalProjectId = capitalProjectId, TeamId = teamId };

    public static SourceTeamMember Member(int teamId, int humanResourceId, int? teamRoleId) =>
        new() { TeamId = teamId, HumanResourceId = humanResourceId, TeamRoleId = teamRoleId };

    public static SourceHumanResourceSkill Holds(int humanResourceId, int skillId, int skillLevelId) =>
        new() { HumanResourceId = humanResourceId, SkillId = skillId, SkillLevelId = skillLevelId };

    public static SourceHoliday Leave(int humanResourceId, DateTime start, DateTime end, int halfDay = 0) =>
        new() { HumanResourceId = humanResourceId, StartDate = start, EndDate = end, HalfDay = halfDay };

    public static SourcePublicHoliday CityHoliday(int cityId, DateTime date, string name = "Public holiday") =>
        new() { CityId = cityId, Date = date, Name = name };

    public static SourcePublicHoliday CountryHoliday(int countryId, DateTime date, string name = "Public holiday") =>
        new() { CountryId = countryId, Date = date, Name = name };

    public static ArtPlanningSkillRanking Ranking(int skillId, int ranking, int? piId = PiId) =>
        new() { CapitalProjectId = ArtId, PiId = piId, SkillId = skillId, Ranking = ranking };

    public static ArtPlanningSkillLevelAllocation Allocation(
        int levelValue, decimal ratio, int usageOrder, int? piId = PiId) =>
        new()
        {
            CapitalProjectId = ArtId,
            PiId = piId,
            SkillLevelValue = levelValue,
            Ratio = ratio,
            UsageOrder = usageOrder,
        };

    public static ArtPlanningRoleCoefficient RoleCoefficient(int teamRoleId, decimal coefficient, int? piId = PiId) =>
        new() { CapitalProjectId = ArtId, PiId = piId, TeamRoleId = teamRoleId, Coefficient = coefficient };

    public static ArtPlanningRoleSkillLimit RoleSkillLimit(int teamRoleId, int skillId, int? piId = PiId) =>
        new() { CapitalProjectId = ArtId, PiId = piId, TeamRoleId = teamRoleId, SkillId = skillId };

    public static ArtPlanningTrainCoefficient TrainCoefficient(decimal coefficient, int? piId = PiId) =>
        new() { CapitalProjectId = ArtId, PiId = piId, Coefficient = coefficient };
}
