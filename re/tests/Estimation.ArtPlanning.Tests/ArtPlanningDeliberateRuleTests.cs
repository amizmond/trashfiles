using Estimation.ArtPlanning.Models.Source;
using Estimation.ArtPlanning.Tests.Infrastructure;
using Xunit;
using static Estimation.ArtPlanning.Tests.Infrastructure.ArtPlanningScenario;

namespace Estimation.ArtPlanning.Tests;

public class ArtPlanningDeliberateRuleTests
{
    [Theory]
    [InlineData(0, 33)]
    [InlineData(1, 34)]
    [InlineData(2, 34)]
    public async Task Half_day_leave_costs_half_a_day(int halfDay, decimal expectedWorkingDays)
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.Holidays.Add(Leave(AnnaId, new DateTime(2026, 1, 12), new DateTime(2026, 1, 13), halfDay));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(expectedWorkingDays, result.Row("SQL").WorkingDays);
    }

    [Fact]
    public async Task Overlapping_leave_rows_never_deduct_more_than_the_day()
    {
        var day = new DateTime(2026, 1, 12);
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.Holidays.AddRange(
                Leave(AnnaId, day, day, halfDay: 2),
                Leave(AnnaId, day, day, halfDay: 2));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(34.5m, result.Row("SQL").WorkingDays);
    }

    [Fact]
    public async Task An_unranked_skill_outranks_every_ranked_skill()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.AddRange(
                Holds(AnnaId, SkillSql, LevelAdvanced),
                Holds(AnnaId, SkillR, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal("R", Assert.Single(result.Rows).SkillName);
    }

    [Fact]
    public async Task An_inactive_member_still_accrues_working_days_in_the_unassigned_row()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.AddRange(
                Person(AnnaId, AnnaNumber, "Anna"),
                Person(101, "E101", "Bob", isActive: false));
            db.TeamMembers.AddRange(
                Member(TeamOneId, AnnaId, RoleDeveloper),
                Member(TeamOneId, 101, RoleDeveloper));
            db.HumanResourceSkills.AddRange(
                Holds(AnnaId, SkillSql, LevelAdvanced),
                Holds(101, SkillPython, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(35, result.Row("SQL").WorkingDays);

        var unassigned = result.Unassigned();
        Assert.Equal(35, unassigned.WorkingDays);
        Assert.Equal(0m, unassigned.Capacity);
        Assert.Null(Assert.Single(unassigned.Members).EmployeeName);
    }

    [Fact]
    public async Task A_team_with_no_members_becomes_a_phantom_person()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.CapitalProjectTeams.Add(TeamOnArt(TeamTwoId));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(35, result.Unassigned().WorkingDays);
        Assert.Equal(1, result.Diagnostics.PhantomRowCount);
    }

    [Fact]
    public async Task A_member_with_no_team_role_matches_no_skill()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, teamRoleId: null));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        var row = Assert.Single(result.Rows);
        Assert.Null(row.SkillName);
        Assert.Equal(35, row.WorkingDays);
        Assert.Equal(0m, row.Capacity);
    }

    [Fact]
    public async Task A_limited_role_without_its_allowed_skill_contributes_nothing()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleProductOwner));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.RoleSkillLimits.Add(RoleSkillLimit(RoleProductOwner, SkillTechPo));
        });

        var result = await scenario.CalculateAsync();

        var row = Assert.Single(result.Rows);
        Assert.Null(row.SkillName);
        Assert.Equal(0m, row.Capacity);
    }

    [Fact]
    public async Task A_member_who_is_limited_on_one_team_and_not_on_another_uses_the_limited_role()
    {
        var scenario = Default().With(db =>
        {
            db.CapitalProjectTeams.Add(TeamOnArt(TeamTwoId));
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.AddRange(
                Member(TeamOneId, AnnaId, RoleDeveloper),
                Member(TeamTwoId, AnnaId, RoleProductOwner));
            db.HumanResourceSkills.AddRange(
                Holds(AnnaId, SkillSql, LevelAdvanced),
                Holds(AnnaId, SkillTechPo, LevelAdvanced));
            db.RoleSkillLimits.Add(RoleSkillLimit(RoleProductOwner, SkillTechPo));
        });

        var result = await scenario.CalculateAsync();

        var row = Assert.Single(result.Rows);
        Assert.Equal("Tech PO", row.SkillName);
        Assert.Equal(35, row.WorkingDays);
        Assert.Equal(35, result.TotalWorkingDays);
        Assert.Equal(35m, result.TotalCapacity);
    }

    [Fact]
    public async Task A_limited_role_held_on_several_teams_is_counted_once()
    {
        var scenario = Default().With(db =>
        {
            db.CapitalProjectTeams.Add(TeamOnArt(TeamTwoId));
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.AddRange(
                Member(TeamOneId, AnnaId, RoleProductOwner),
                Member(TeamTwoId, AnnaId, RoleProductOwner));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillTechPo, LevelAdvanced));
            db.RoleSkillLimits.Add(RoleSkillLimit(RoleProductOwner, SkillTechPo));
        });

        var result = await scenario.CalculateAsync();

        var row = Assert.Single(result.Rows);
        Assert.Equal("Tech PO", row.SkillName);
        Assert.Equal(35, row.WorkingDays);
        Assert.Equal(35m, row.Capacity);
        Assert.Single(row.Members);
    }

    [Fact]
    public async Task Two_teams_with_the_same_name_do_not_double_a_shared_member()
    {
        var scenario = Default().With(db =>
        {
            db.Teams.Single(t => t.Id == TeamTwoId).Name = "Alpha";
            db.CapitalProjectTeams.Add(TeamOnArt(TeamTwoId));
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.AddRange(
                Member(TeamOneId, AnnaId, RoleDeveloper),
                Member(TeamTwoId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(35, result.Row("SQL").WorkingDays);
    }

    [Fact]
    public async Task A_member_on_two_differently_named_teams_is_counted_once()
    {
        var scenario = Default().With(db =>
        {
            db.CapitalProjectTeams.Add(TeamOnArt(TeamTwoId));
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.AddRange(
                Member(TeamOneId, AnnaId, RoleDeveloper),
                Member(TeamTwoId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(35, result.Row("SQL").WorkingDays);
    }

    [Fact]
    public async Task A_country_scoped_public_holiday_deducts_nothing()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna", cityId: AnnaCityId));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
            db.PublicHolidays.Add(CountryHoliday(countryId: 7, date: new DateTime(2026, 1, 6)));
        });

        var result = await scenario.CalculateAsync();

        Assert.Equal(35, result.Row("SQL").WorkingDays);
    }

    [Fact]
    public async Task A_member_with_no_employee_number_matches_no_skill()
    {
        var scenario = Default().With(db =>
        {
            db.HumanResources.Add(Person(AnnaId, number: null, "Anna"));
            db.TeamMembers.Add(Member(TeamOneId, AnnaId, RoleDeveloper));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));
        });

        var result = await scenario.CalculateAsync();

        Assert.Null(Assert.Single(result.Rows).SkillName);
    }

    [Fact]
    public async Task A_role_held_in_another_art_can_decide_this_arts_coefficient()
    {
        const int otherArtId = 2;
        const int otherTeamId = 12;
        const int architectRoleId = 34;

        var scenario = Default().With(db =>
        {
            db.CapitalProjects.Add(new SourceArt { Id = otherArtId, Name = "Orion" });
            db.Teams.Add(new SourceTeam { Id = otherTeamId, Name = "Gamma" });
            db.CapitalProjectTeams.Add(
                new SourceArtTeam { CapitalProjectId = otherArtId, TeamId = otherTeamId });
            db.TeamRoles.Add(new SourceTeamRole { Id = architectRoleId, Name = "Architect" });

            db.HumanResources.Add(Person(AnnaId, AnnaNumber, "Anna"));
            db.TeamMembers.AddRange(
                Member(TeamOneId, AnnaId, RoleDeveloper),
                Member(otherTeamId, AnnaId, architectRoleId));
            db.HumanResourceSkills.Add(Holds(AnnaId, SkillSql, LevelAdvanced));

            db.RoleCoefficients.Add(RoleCoefficient(architectRoleId, 0.50m));
        });

        var result = await scenario.CalculateAsync();

        var row = result.Row("SQL");
        Assert.Equal(35, row.WorkingDays);
        Assert.Equal("Architect", Assert.Single(row.Members).TeamRoleName);
        Assert.Equal(17.5m, row.Capacity);
    }
}
