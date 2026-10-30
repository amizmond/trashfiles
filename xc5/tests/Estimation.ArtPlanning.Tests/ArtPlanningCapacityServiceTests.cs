using Estimation.ArtPlanning.Tests.Infrastructure;
using Xunit;
using static Estimation.ArtPlanning.Tests.Infrastructure.ArtPlanningScenario;

namespace Estimation.ArtPlanning.Tests;

public class ArtPlanningCapacityServiceTests
{
    [Fact]
    public async Task Window_is_the_pi_minus_its_last_21_days_counted_in_weekdays()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(new DateTime(2026, 1, 5), result.Diagnostics.WindowStart);
        Assert.Equal(new DateTime(2026, 2, 22), result.Diagnostics.WindowEnd);
        Assert.Equal(FullWorkingDays, result.Diagnostics.WorkDayCount);
    }

    [Fact]
    public async Task Capacity_is_working_days_times_level_ratio_times_role_coefficient()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna", cityId: AnnaCityId));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleScrumMaster));
            db.HumanResourceSkills.AddRange(
                Holds(AnnaId, SkillSql, LevelAdvanced),
                Holds(AnnaId, SkillPython, LevelIntermediate));
            db.RoleCoefficients.Add(RoleCoefficient(RoleScrumMaster, 0.60m));

            db.PublicHolidays.Add(CityHoliday(AnnaCityId, new DateTime(2026, 1, 6)));
            db.Holidays.Add(Leave(AnnaId, new DateTime(2026, 1, 12), new DateTime(2026, 1, 13)));
        });

        var result = await scenario.CalculateAsync();

        var row = Assert.Single(result.Rows);
        Assert.Equal("SQL", row.SkillName);
        Assert.Equal(32, row.WorkingDays);
        Assert.Equal(19.2m, row.Capacity);
    }

    [Fact]
    public async Task Public_holidays_of_the_city_and_of_its_country_are_deducted_and_no_others()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna", cityId: AnnaCityId));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.PublicHolidays.AddRange(
                CityHoliday(AnnaCityId, new DateTime(2026, 1, 6)),
                CountryHoliday(AnnaCountryId, new DateTime(2026, 1, 7)),
                CountryHoliday(AnnaCountryId + 1, new DateTime(2026, 1, 8)));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(FullWorkingDays - 2, result.Row("SQL").WorkingDays);
    }

    [Fact]
    public async Task A_date_on_both_the_city_and_the_country_list_is_deducted_once()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna", cityId: AnnaCityId));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.PublicHolidays.AddRange(
                CityHoliday(AnnaCityId, new DateTime(2026, 1, 6)),
                CountryHoliday(AnnaCountryId, new DateTime(2026, 1, 6)));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(FullWorkingDays - 1, result.Row("SQL").WorkingDays);
    }

    [Fact]
    public async Task Order_of_usage_beats_skill_ranking()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.AddRange(
                Holds(AnnaId, SkillSql, LevelIntermediate),
                Holds(AnnaId, SkillPython, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        var row = Assert.Single(result.Rows);
        Assert.Equal("Python", row.SkillName);
        Assert.Equal(35m, row.Capacity);
    }

    [Fact]
    public async Task Skill_ranking_breaks_ties_within_the_same_usage_order()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.AddRange(
                Holds(AnnaId, SkillSql, LevelAdvanced),
                Holds(AnnaId, SkillTechPo, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal("SQL", Assert.Single(result.Rows).SkillName);
    }

    [Fact]
    public async Task A_role_limited_to_a_skill_uses_that_skill_rather_than_its_best_one()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleProductOwner));
            db.HumanResourceSkills.AddRange(
                Holds(AnnaId, SkillSql, LevelAdvanced),
                Holds(AnnaId, SkillTechPo, LevelAdvanced));
            db.RoleSkillLimits.Add(RoleSkillLimit(RoleProductOwner, SkillTechPo));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal("Tech PO", Assert.Single(result.Rows).SkillName);
    }

    [Fact]
    public async Task Role_coefficient_defaults_to_one_when_the_role_is_not_listed()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.RoleCoefficients.Add(RoleCoefficient(RoleScrumMaster, 0.60m));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(35m, Assert.Single(result.Rows).Capacity);
    }

    [Fact]
    public async Task Intermediate_level_delivers_its_configured_ratio()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelIntermediate));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(26.25m, Assert.Single(result.Rows).Capacity);
    }

    [Fact]
    public async Task Train_coefficient_is_applied_to_the_finished_total()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.TrainCoefficients.Add(TrainCoefficient(0.50m));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(0.50m, result.TrainCoefficient);
        Assert.Equal(17.5m, Assert.Single(result.Rows).Capacity);
    }

    [Fact]
    public async Task A_pi_shorter_than_the_excluded_tail_produces_nothing()
    {
        var scenario = Default().With(db =>
        {
            var pi = db.Pis.Single();
            pi.EndDate = pi.StartDate!.Value.AddDays(20);
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.Diagnostics.WorkDayCount);
    }

    [Fact]
    public async Task The_member_breakdown_explains_every_multiplier()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleScrumMaster));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelIntermediate));
            db.RoleCoefficients.Add(RoleCoefficient(RoleScrumMaster, 0.60m));
        });

        var result = await scenario.CalculateAsync();

        var member = Assert.Single(Assert.Single(result.Rows).Members);
        Assert.Equal("Anna", member.EmployeeName);
        Assert.Equal(AnnaNumber, member.EmployeeNumber);
        Assert.Equal("Scrum Master", member.TeamRoleName);
        Assert.Equal(2, member.SkillLevelValue);
        Assert.Equal("Intermediate", member.SkillLevelName);
        Assert.Equal(35, member.WorkingDays);
        Assert.Equal(0.75m, member.Ratio);
        Assert.Equal(0.60m, member.RoleCoefficient);
        Assert.Equal(15.75m, member.Capacity);
    }

    [Fact]
    public async Task A_member_with_no_eligible_skill_level_lands_in_the_unassigned_row()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelBeginner));
        });

        var result = await scenario.CalculateAsync();

        var row = result.Unassigned();
        Assert.Equal(35, row.WorkingDays);
        Assert.Equal(0m, row.Capacity);
    }
}
