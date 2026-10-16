using Estimation.Core.Administration.Models;
using Estimation.Core.Calendar.Models;
using Estimation.Core.Poker.Services;
using Estimation.Core.Resources.Models;
using Estimation.Core.Tests.Infrastructure;
using Xunit;

namespace Estimation.Core.Tests.Poker;

public class PokerAbsenceServiceTests
{
    private const int TeamId = 1;

    private static readonly DateTime SprintStart = new(2026, 09, 01);
    private static readonly DateTime SprintEnd = new(2026, 09, 14);

    private readonly InMemoryDatabase _db = new();
    private readonly PokerAbsenceService _service;

    public PokerAbsenceServiceTests()
    {
        _service = new PokerAbsenceService(_db);
    }

    private Task SeedTeamAsync() => _db.SeedAsync(db =>
    {
        db.Countries.Add(new Country { Id = 1, Name = "Poland" });
        db.Cities.Add(new City { Id = 10, Name = "Krakow", CountryId = 1 });
        db.Cities.Add(new City { Id = 11, Name = "London", CountryId = 2 });
        db.Countries.Add(new Country { Id = 2, Name = "United Kingdom" });

        db.Teams.Add(new Team { Id = TeamId, Name = "Team A" });

        db.HumanResources.Add(new HumanResource
        {
            Id = 100, FullName = "Anna Nowak", EmployeeName = "Nowak", EmployeeNumber = "E100", CityId = 10,
        });
        db.HumanResources.Add(new HumanResource
        {
            Id = 200, FullName = "Ben Smith", EmployeeName = "Smith", EmployeeNumber = "E200", CityId = 11,
        });
        db.HumanResources.Add(new HumanResource
        {
            Id = 300, FullName = "Carl Left", EmployeeName = "Left", EmployeeNumber = "E300", CityId = 10,
            IsActive = false,
        });

        db.TeamMembers.Add(new TeamMember { TeamId = TeamId, HumanResourceId = 100 });
        db.TeamMembers.Add(new TeamMember { TeamId = TeamId, HumanResourceId = 200 });
        db.TeamMembers.Add(new TeamMember { TeamId = TeamId, HumanResourceId = 300 });

        db.HolidayTypes.Add(new HolidayType { Id = 1, Name = "Annual leave", ColorHex = "#4CAF50" });
    });

    private Task AddLeaveAsync(int hrId, DateTime start, DateTime end,
        HalfDayPortion half = HalfDayPortion.None) => _db.SeedAsync(db =>
        db.Holidays.Add(new Holiday
        {
            HumanResourceId = hrId,
            HolidayTypeId = 1,
            StartDate = start,
            EndDate = end,
            HalfDay = half,
        }));

    private Task<PokerSprintAbsences> GetAsync() =>
        _service.GetSprintAbsencesAsync(TeamId, SprintStart, SprintEnd);

    [Fact]
    public async Task A_sprint_without_absences_is_empty()
    {
        await SeedTeamAsync();

        var result = await GetAsync();

        Assert.True(result.IsEmpty);
        Assert.Equal(10, result.WorkingDays);
        Assert.Equal(2, result.TeamMemberCount);
    }

    [Fact]
    public async Task Only_leave_overlapping_the_sprint_is_reported_and_it_is_clipped_to_the_period()
    {
        await SeedTeamAsync();
        await AddLeaveAsync(100, new DateTime(2026, 08, 26), new DateTime(2026, 09, 02));
        await AddLeaveAsync(200, new DateTime(2026, 09, 21), new DateTime(2026, 09, 25));

        var result = await GetAsync();

        var member = Assert.Single(result.Members);
        Assert.Equal("Anna Nowak", member.FullName);
        var leave = Assert.Single(member.Leaves);
        Assert.Equal(SprintStart, leave.Start);
        Assert.Equal(new DateTime(2026, 09, 02), leave.End);
        Assert.Equal(2, leave.Days);
        Assert.Equal("Annual leave", leave.TypeName);
        Assert.Equal("#4CAF50", leave.ColorHex);
    }

    [Fact]
    public async Task Weekends_do_not_count_as_leave_days()
    {
        await SeedTeamAsync();
        await AddLeaveAsync(100, new DateTime(2026, 09, 04), new DateTime(2026, 09, 07));

        var member = Assert.Single((await GetAsync()).Members);

        Assert.Equal(2, member.LeaveDays);
    }

    [Fact]
    public async Task A_single_half_day_is_charged_as_half_a_day()
    {
        await SeedTeamAsync();
        await AddLeaveAsync(100, new DateTime(2026, 09, 03), new DateTime(2026, 09, 03), HalfDayPortion.AM);

        var member = Assert.Single((await GetAsync()).Members);

        Assert.Equal(0.5, member.LeaveDays);
        Assert.True(member.Leaves[0].IsHalfDay);
    }

    [Fact]
    public async Task Inactive_team_members_are_ignored()
    {
        await SeedTeamAsync();
        await AddLeaveAsync(300, new DateTime(2026, 09, 02), new DateTime(2026, 09, 03));

        var result = await GetAsync();

        Assert.Empty(result.Members);
        Assert.Equal(2, result.TeamMemberCount);
    }

    [Fact]
    public async Task Public_holidays_are_listed_per_date_with_the_cities_and_members_they_apply_to()
    {
        await SeedTeamAsync();
        await _db.SeedAsync(db =>
        {
            db.PublicHolidays.Add(new PublicHoliday
            {
                Id = 1, CountryId = 1, Date = new DateTime(2026, 09, 03), Name = "Country day",
            });
            db.PublicHolidays.Add(new PublicHoliday
            {
                Id = 2, CityId = 11, CountryId = 2, Date = new DateTime(2026, 09, 03), Name = "City day",
            });
            db.PublicHolidays.Add(new PublicHoliday
            {
                Id = 3, CountryId = 1, Date = new DateTime(2026, 09, 05), Name = "Weekend day",
            });
        });

        var result = await GetAsync();

        Assert.Collection(result.PublicHolidays,
            first =>
            {
                Assert.Equal(new DateTime(2026, 09, 03), first.Date);
                Assert.Equal("City day", first.Name);
                Assert.Equal(["London"], first.CityNames);
                Assert.Equal(["Ben Smith"], first.MemberNames);
            },
            second =>
            {
                Assert.Equal(new DateTime(2026, 09, 03), second.Date);
                Assert.Equal("Country day", second.Name);
                Assert.Equal(["Krakow"], second.CityNames);
                Assert.Equal(["Anna Nowak"], second.MemberNames);
            });
    }

    [Fact]
    public async Task A_country_holiday_lists_every_city_of_the_members_it_covers()
    {
        await SeedTeamAsync();
        await _db.SeedAsync(db =>
        {
            db.Cities.Add(new City { Id = 12, Name = "Warsaw", CountryId = 1 });
            db.HumanResources.Add(new HumanResource
            {
                Id = 400, FullName = "Dora Kowal", EmployeeName = "Kowal", EmployeeNumber = "E400", CityId = 12,
            });
            db.TeamMembers.Add(new TeamMember { TeamId = TeamId, HumanResourceId = 400 });
            db.PublicHolidays.Add(new PublicHoliday
            {
                Id = 1, CountryId = 1, Date = new DateTime(2026, 09, 03), Name = "Country day",
            });
        });

        var holiday = Assert.Single((await GetAsync()).PublicHolidays);

        Assert.Equal("Country day", holiday.Name);
        Assert.Equal(["Krakow", "Warsaw"], holiday.CityNames);
        Assert.Equal(["Anna Nowak", "Dora Kowal"], holiday.MemberNames);
    }

    [Fact]
    public async Task A_public_holiday_inside_a_leave_is_not_charged_twice()
    {
        await SeedTeamAsync();
        await _db.SeedAsync(db => db.PublicHolidays.Add(new PublicHoliday
        {
            Id = 1, CountryId = 1, Date = new DateTime(2026, 09, 03), Name = "Country day",
        }));
        await AddLeaveAsync(100, new DateTime(2026, 09, 02), new DateTime(2026, 09, 04));

        var member = Assert.Single((await GetAsync()).Members);

        Assert.Equal(2, member.LeaveDays);
        Assert.Equal([new DateTime(2026, 09, 03)], member.PublicHolidays);
    }

    [Fact]
    public async Task A_player_is_matched_to_a_team_member_through_the_employee_number()
    {
        await SeedTeamAsync();
        await _db.SeedAsync(db =>
        {
            db.AppUsers.Add(new AppUser
            {
                Id = 1, WindowsUserName = "DOMAIN\\anna", SamAccountName = "anna", EmployeeId = "PLE100",
                IsApproved = true,
            });
            db.AppUsers.Add(new AppUser
            {
                Id = 2, WindowsUserName = "DOMAIN\\ben", SamAccountName = "ben", EmployeeId = "E200",
                IsApproved = false,
            });
        });
        await AddLeaveAsync(100, new DateTime(2026, 09, 02), new DateTime(2026, 09, 03));

        var result = await GetAsync();

        Assert.Equal("Anna Nowak", result.ForPlayer("domain\\anna")?.FullName);
        Assert.Null(result.ForPlayer("DOMAIN\\ben"));
        Assert.Null(result.ForPlayer("DOMAIN\\nobody"));
    }

    [Fact]
    public async Task A_team_without_members_reports_the_period_only()
    {
        var result = await _service.GetSprintAbsencesAsync(TeamId, SprintStart, SprintEnd);

        Assert.True(result.IsEmpty);
        Assert.Equal(SprintStart, result.From);
        Assert.Equal(SprintEnd, result.To);
        Assert.Equal(10, result.WorkingDays);
    }
}
